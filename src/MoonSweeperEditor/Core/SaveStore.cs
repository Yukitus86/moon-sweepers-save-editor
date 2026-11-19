using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace MoonSweeperEditor.Core;

public sealed class SaveStore(string root)
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "DragDropGames", "MoonSweepers");

    public string Root { get; } = root;
    public string SessionsDir => Path.Combine(Root, "MoonSessions");

    /// <summary>Save slots of the "Mars Sweepers" mod (Nexus mod 148); same layout and format as MoonSessions.</summary>
    public string MarsSessionsDir => Path.Combine(Root, "MarsSweepers", "Release", "saves");
    public string BackupRoot => Path.Combine(Root, "EditorBackups");

    static readonly Regex SlotName = new(@"^slot-\d+$", RegexOptions.IgnoreCase);
    static readonly string[] SlotFiles = ["slot.json", "slot.json.bak"];
    // The Mars Sweepers mod keeps its own production state next to the session and links it to the session's
    // SHA256 ("NativeHash"); both are backed up and restored together.
    static readonly string[] MarsProductionFiles = ["mars-production.json", "mars-production.json.bak"];
    static readonly string[] SessionFiles = ["session.moon", "session.moon.bak", .. MarsProductionFiles];

    public IReadOnlyList<SaveSlot> LoadSlots() => [.. LoadSlots(SessionsDir, false), .. LoadSlots(MarsSessionsDir, true)];

    static IEnumerable<SaveSlot> LoadSlots(string dir, bool isMars)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir)
            .Where(d => SlotName.IsMatch(Path.GetFileName(d)))
            .Select(d => SaveSlot.Load(d, isMars))
            .OrderBy(s => s.Number)
            .ToList();
    }

    public static bool IsGameRunning() =>
        OperatingSystem.IsLinux() ? IsGameRunningLinux() : Process.GetProcessesByName("MoonSweepers").Length > 0;

    // Under Proton the kernel name is "MoonSweepers.ex" (cut to 15 chars), a native build would be "MoonSweepers.x86_64".
    static bool IsGameRunningLinux()
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!Path.GetFileName(dir).All(char.IsAsciiDigit)) continue;
                try
                {
                    if (File.ReadAllText(Path.Combine(dir, "comm")).StartsWith("MoonSweepers", StringComparison.Ordinal)) return true;
                }
                catch (IOException) { /* process ended or no access */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception) { /* no /proc: assume the game is not running */ }
        return false;
    }

    /// <summary>Writes the edited session. Returns the backup folder.</summary>
    public string Save(SaveSlot slot, SessionData data) => WriteRaw(slot, data.Build(), data.Raw);

    /// <summary>Writes decompressed session data (from the editor or an imported .bin).</summary>
    public string WriteRaw(SaveSlot slot, byte[] newRaw, byte[]? expectedOnDisk)
    {
        if (IsGameRunning())
            throw new InvalidOperationException(Loc.T("ErrGameRunning"));
        if (slot.SessionPath is null)
            throw new InvalidOperationException(Loc.T("ErrNoSessionSlot"));

        // Refuse to overwrite a save the game wrote after the editor loaded it.
        byte[] oldRaw = MoonContainer.ReadRaw(slot.SessionPath);
        if (expectedOnDisk is not null && !oldRaw.AsSpan().SequenceEqual(expectedOnDisk))
            throw new InvalidOperationException(Loc.T("ErrStale"));

        // The result must parse again, otherwise the game would most likely reject it as well.
        var check = SessionData.Parse(newRaw);
        byte[] encoded = MoonContainer.Encode(newRaw);

        string backupDir = Backup(slot);
        SaveSlot.AtomicWrite(slot.SessionPath, encoded);
        if (File.Exists(slot.SlotJsonPath) && check.Team is { } team)
            SaveSlot.WriteDollars(slot.SlotJsonPath, (double)team.Dollars);

        byte[] onDisk;
        try
        {
            RelinkMarsProduction(Path.GetDirectoryName(slot.SessionPath)!, MoonContainer.Hash(oldRaw), MoonContainer.Hash(newRaw));
            onDisk = MoonContainer.ReadRaw(slot.SessionPath);
        }
        catch { Restore(slot, backupDir); throw; }
        if (!onDisk.AsSpan().SequenceEqual(newRaw))
        {
            Restore(slot, backupDir);
            throw new InvalidDataException(Loc.T("ErrVerify"));
        }
        return backupDir;
    }

    public string Backup(SaveSlot slot)
    {
        string dir = Path.Combine(BackupRoot, slot.Key, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dir);
        foreach (string f in SlotFiles)
            CopyIfExists(Path.Combine(slot.SlotDir, f), Path.Combine(dir, f));
        if (slot.SessionPath is { } session)
        {
            string sessionDir = Path.GetDirectoryName(session)!;
            File.WriteAllText(Path.Combine(dir, "account.txt"), Path.GetFileName(sessionDir));
            foreach (string f in SessionFiles)
                CopyIfExists(Path.Combine(sessionDir, f), Path.Combine(dir, f));
        }
        return dir;
    }

    public IReadOnlyList<string> ListBackups(SaveSlot slot)
    {
        string dir = Path.Combine(BackupRoot, slot.Key);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir)
            .Where(d => File.Exists(Path.Combine(d, "session.moon")))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToList();
    }

    public void Restore(SaveSlot slot, string backupDir)
    {
        if (IsGameRunning())
            throw new InvalidOperationException(Loc.T("ErrGameRunning"));

        foreach (string f in SlotFiles)
            CopyIfExists(Path.Combine(backupDir, f), Path.Combine(slot.SlotDir, f));

        string accountFile = Path.Combine(backupDir, "account.txt");
        string? sessionDir = File.Exists(accountFile)
            ? Path.Combine(slot.SlotDir, File.ReadAllText(accountFile).Trim())
            : slot.SessionPath is null ? null : Path.GetDirectoryName(slot.SessionPath);
        if (sessionDir is null || !Directory.Exists(sessionDir)) return;

        foreach (string f in SessionFiles)
            CopyIfExists(Path.Combine(backupDir, f), Path.Combine(sessionDir, f));
    }

    /// <summary>
    /// Mars Sweepers refuses to load a session whose SHA256 differs from "NativeHash" in mars-production.json
    /// ("production file does not match this save"). After the editor rewrote session.moon, the production files
    /// that belonged to the old session are pointed at the new one; nothing else in them is changed.
    /// </summary>
    static void RelinkMarsProduction(string sessionDir, string oldHash, string newHash)
    {
        if (oldHash == newHash) return;
        var link = new Regex(@"(""NativeHash""\s*:\s*"")" + oldHash + @"""", RegexOptions.IgnoreCase);
        foreach (string name in MarsProductionFiles)
        {
            string path = Path.Combine(sessionDir, name);
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path);
            if (!link.IsMatch(text)) continue;
            string updated = link.Replace(text, m => m.Groups[1].Value + newHash + "\"");
            SaveSlot.AtomicWrite(path, new System.Text.UTF8Encoding(false).GetBytes(updated));
        }
    }

    static void CopyIfExists(string from, string to)
    {
        if (File.Exists(from)) File.Copy(from, to, overwrite: true);
    }
}

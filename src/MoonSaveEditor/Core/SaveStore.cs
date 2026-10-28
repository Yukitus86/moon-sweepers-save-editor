using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MoonSaveEditor.Core;

public sealed record EditResult(string BackupDir, decimal OldBalance, decimal NewBalance, WalletRecord Written);

public sealed class SaveStore(string root)
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "DragDropGames", "MoonSweepers");

    public string Root { get; } = root;
    public string SessionsDir => Path.Combine(Root, "MoonSessions");
    public string BackupRoot => Path.Combine(Root, "EditorBackups");

    static readonly Regex SlotName = new(@"^slot-\d+$", RegexOptions.IgnoreCase);
    static readonly string[] BackedUpFiles = ["slot.json", "slot.json.bak"];
    static readonly string[] BackedUpSessionFiles = ["session.moon", "session.moon.bak"];

    public IReadOnlyList<SaveSlot> LoadSlots()
    {
        if (!Directory.Exists(SessionsDir)) return [];
        return Directory.EnumerateDirectories(SessionsDir)
            .Where(d => SlotName.IsMatch(Path.GetFileName(d)))
            .Select(SaveSlot.Load)
            .OrderBy(s => s.Number)
            .ToList();
    }

    public static bool IsGameRunning() => Process.GetProcessesByName("MoonSweepers").Length > 0;

    public EditResult SetBalance(SaveSlot slot, decimal newBalance)
    {
        if (newBalance < 0)
            throw new ArgumentOutOfRangeException(nameof(newBalance), Loc.T("ErrNegative"));
        if (IsGameRunning())
            throw new InvalidOperationException(Loc.T("ErrGameRunning"));
        if (!slot.CanEdit || slot.SessionPath is null)
            throw new InvalidOperationException(slot.Error ?? Loc.T("ErrNotEditable"));

        // Re-read from disk so a save made after the list was loaded is not overwritten with stale data.
        byte[] raw = MoonFile.ReadRaw(slot.SessionPath);
        var wallet = WalletRecord.Locate(raw);
        byte[] encoded = MoonFile.Encode(wallet.WithBalance(raw, newBalance));

        string backupDir = Backup(slot);

        SaveSlot.AtomicWrite(slot.SessionPath, encoded);
        if (File.Exists(slot.SlotJsonPath))
            SaveSlot.WriteDollars(slot.SlotJsonPath, (double)newBalance);

        // Verify what is on disk now decodes, hashes and contains the expected balance.
        var written = WalletRecord.Locate(MoonFile.ReadRaw(slot.SessionPath));
        if (written.BalanceOffset != wallet.BalanceOffset || written.Balance != newBalance)
        {
            Restore(slot, backupDir);
            throw new InvalidDataException(Loc.T("ErrVerify"));
        }

        return new EditResult(backupDir, wallet.Balance, newBalance, written);
    }

    public string Backup(SaveSlot slot)
    {
        string dir = Path.Combine(BackupRoot, slot.Name, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dir);
        foreach (string f in BackedUpFiles)
            CopyIfExists(Path.Combine(slot.SlotDir, f), Path.Combine(dir, f));
        if (slot.SessionPath is { } session)
        {
            string sessionDir = Path.GetDirectoryName(session)!;
            File.WriteAllText(Path.Combine(dir, "account.txt"), Path.GetFileName(sessionDir));
            foreach (string f in BackedUpSessionFiles)
                CopyIfExists(Path.Combine(sessionDir, f), Path.Combine(dir, f));
        }
        return dir;
    }

    public IReadOnlyList<string> ListBackups(SaveSlot slot)
    {
        string dir = Path.Combine(BackupRoot, slot.Name);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir).OrderByDescending(d => d, StringComparer.Ordinal).ToList();
    }

    public void Restore(SaveSlot slot, string backupDir)
    {
        if (IsGameRunning())
            throw new InvalidOperationException(Loc.T("ErrGameRunning"));

        foreach (string f in BackedUpFiles)
            CopyIfExists(Path.Combine(backupDir, f), Path.Combine(slot.SlotDir, f));

        string accountFile = Path.Combine(backupDir, "account.txt");
        string? sessionDir = File.Exists(accountFile)
            ? Path.Combine(slot.SlotDir, File.ReadAllText(accountFile).Trim())
            : slot.SessionPath is null ? null : Path.GetDirectoryName(slot.SessionPath);
        if (sessionDir is null) return;

        foreach (string f in BackedUpSessionFiles)
            CopyIfExists(Path.Combine(backupDir, f), Path.Combine(sessionDir, f));
    }

    static void CopyIfExists(string from, string to)
    {
        if (File.Exists(from)) File.Copy(from, to, overwrite: true);
    }
}

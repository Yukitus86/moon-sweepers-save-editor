using System.IO;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MoonSaveEditor.Core;

public sealed class SaveSlot
{
    public required string Name { get; init; }
    public required string SlotDir { get; init; }
    public string? SessionPath { get; init; }
    public double? Dollars { get; init; }
    public int? Regions { get; init; }
    public double? PlaySeconds { get; init; }
    public DateTime? LastPlayed { get; init; }
    public WalletRecord? Wallet { get; init; }
    public string? Error { get; init; }

    public int Number => int.TryParse(Name.AsSpan(5), out int n) ? n : int.MaxValue;
    public decimal? Balance => Wallet?.Balance;
    public bool CanEdit => Error is null && Wallet is not null;
    public string SlotJsonPath => Path.Combine(SlotDir, "slot.json");

    public static SaveSlot Load(string slotDir)
    {
        string name = Path.GetFileName(slotDir);
        string? session = FindSession(slotDir);

        double? dollars = null, playSeconds = null;
        int? regions = null;
        DateTime? lastPlayed = null;
        var errors = new List<string>();

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(slotDir, "slot.json")))!;
            dollars = json["dollars"]?.GetValue<double>();
            regions = json["regions"]?.GetValue<int>();
            playSeconds = json["playSeconds"]?.GetValue<double>();
            if (json["lastPlayedTicks"]?.GetValue<long>() is long ticks and > 0 and < 3155378975999999999)
                lastPlayed = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        }
        catch (Exception) { /* slot.json is only the menu preview; the balance comes from session.moon */ }

        WalletRecord? wallet = null;
        if (session is null) errors.Add(Loc.T("ErrNoSession"));
        else
        {
            try { wallet = WalletRecord.Locate(MoonFile.ReadRaw(session)); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }

        return new SaveSlot
        {
            Name = name,
            SlotDir = slotDir,
            SessionPath = session,
            Dollars = dollars,
            Regions = regions,
            PlaySeconds = playSeconds,
            LastPlayed = lastPlayed,
            Wallet = wallet,
            Error = errors.Count == 0 ? null : string.Join(" ", errors),
        };
    }

    // The session lives in a per-account subfolder (64 hex chars) of the slot folder.
    static string? FindSession(string slotDir) =>
        Directory.EnumerateDirectories(slotDir)
            .Select(d => Path.Combine(d, "session.moon"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    /// <summary>Rewrites only the "dollars" number in slot.json, leaving the rest byte-identical.</summary>
    public static void WriteDollars(string slotJsonPath, double dollars)
    {
        string text = File.ReadAllText(slotJsonPath);
        string value = dollars.ToString("R", CultureInfo.InvariantCulture);
        if (!value.Contains('.') && !value.Contains('E')) value += ".0";
        string updated = Regex.Replace(text, @"(""dollars""\s*:\s*)-?[0-9.eE+-]+", m => m.Groups[1].Value + value);
        if (updated == text && !text.Contains(value))
            throw new InvalidDataException(Loc.T("ErrNoDollars"));
        AtomicWrite(slotJsonPath, System.Text.Encoding.UTF8.GetBytes(updated));
    }

    internal static void AtomicWrite(string path, byte[] data)
    {
        string tmp = path + ".editor-tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MoonSweeperEditor.Core;

public sealed class SaveSlot
{
    public required string Name { get; init; }
    public required string SlotDir { get; init; }
    public string? SessionPath { get; init; }
    public double? Dollars { get; init; }
    public int? Regions { get; init; }
    public double? PlaySeconds { get; init; }
    public DateTime? LastPlayed { get; init; }
    public SessionData? Data { get; init; }
    public string? Error { get; init; }

    public int Number => int.TryParse(Name.AsSpan(5), out int n) ? n : int.MaxValue;
    public bool CanEdit => Error is null && Data is not null;
    public string SlotJsonPath => Path.Combine(SlotDir, "slot.json");
    public string Title => Loc.T("SlotTitle", Number);

    public string BalanceText => Data?.Team is { } t ? Format.Money(t.Dollars) : Dollars is { } d ? Format.Money((decimal)d) : "–";
    public string PlayTimeText => PlaySeconds is { } s ? Format.Duration(s) : "–";
    public string LastPlayedText => LastPlayed?.ToString("g", Loc.Instance.Culture) ?? "–";
    public string RegionsText => Regions?.ToString("N0", Loc.Instance.Culture) ?? "–";

    public static SaveSlot Load(string slotDir)
    {
        string name = Path.GetFileName(slotDir);
        string? session = FindSession(slotDir);

        double? dollars = null, playSeconds = null;
        int? regions = null;
        DateTime? lastPlayed = null;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(slotDir, "slot.json")))!;
            dollars = json["dollars"]?.GetValue<double>();
            regions = json["regions"]?.GetValue<int>();
            playSeconds = json["playSeconds"]?.GetValue<double>();
            if (json["lastPlayedTicks"]?.GetValue<long>() is long ticks and > 0 and < 3155378975999999999)
                lastPlayed = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        }
        catch (Exception) { /* slot.json is only the menu preview */ }

        SessionData? data = null;
        string? error = null;
        if (session is null) error = Loc.T("ErrNoSession");
        else
        {
            try { data = SessionData.Parse(MoonContainer.ReadRaw(session)); }
            catch (Exception ex) { error = ex.Message; }
        }

        return new SaveSlot
        {
            Name = name, SlotDir = slotDir, SessionPath = session,
            Dollars = dollars, Regions = regions, PlaySeconds = playSeconds, LastPlayed = lastPlayed,
            Data = data, Error = error,
        };
    }

    // The session lives in a per-account subfolder (64 hex chars) of the slot folder.
    static string? FindSession(string slotDir) =>
        Directory.EnumerateDirectories(slotDir)
            .Select(d => Path.Combine(d, "session.moon"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    /// <summary>Rewrites only the "dollars" number in slot.json, leaving the rest unchanged.</summary>
    public static void WriteDollars(string slotJsonPath, double dollars)
    {
        string text = File.ReadAllText(slotJsonPath);
        string value = dollars.ToString("R", CultureInfo.InvariantCulture);
        if (!value.Contains('.') && !value.Contains('E')) value += ".0";
        string updated = Regex.Replace(text, @"(""dollars""\s*:\s*)-?[0-9.eE+-]+", m => m.Groups[1].Value + value);
        AtomicWrite(slotJsonPath, new UTF8Encoding(false).GetBytes(updated));
    }

    public static void AtomicWrite(string path, byte[] data)
    {
        string tmp = path + ".editor-tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}

public static class Format
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    static CultureInfo Ui => Loc.Instance.Culture;

    public static string Money(decimal value) => "$ " + value.ToString("N2", Ui);
    public static string Number(double value, int decimals = 2) => value.ToString("N" + decimals, Ui);

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours} h {t.Minutes:00} min";
    }

    public static bool TryParseDecimal(string text, out decimal value)
    {
        text = text.Replace("$", "").Replace(" ", "").Replace(" ", "").Trim();
        // The UI language's format first ("1,234.56" en / "1.234,56" de), then the other one.
        if (decimal.TryParse(text, NumberStyles.Number, Ui, out value)) return true;
        if (text.Contains(','))
            return decimal.TryParse(text, NumberStyles.Number, De, out value);
        if (text.Count(c => c == '.') > 1)
            return decimal.TryParse(text, NumberStyles.Number, De, out value);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDouble(string text, out double value)
    {
        value = 0;
        if (!TryParseDecimal(text, out decimal d)) return false;
        value = (double)d;
        return true;
    }
}

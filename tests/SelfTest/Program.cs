using MoonSaveEditor.Core;

// Usage: SelfTest <folder-with-saves>...
// 1) Every *.moon with 1PCM signature: decode, locate wallet, re-encode round trip, shift round trip.
// 2) Every slot-N folder: end-to-end SetDollars + Restore on a temporary copy (originals untouched).

int failures = 0;
void Check(bool ok, string what)
{
    if (!ok) failures++;
    Console.WriteLine($"  [{(ok ? "OK " : "FAIL")}] {what}");
}

foreach (string root in args)
{
    foreach (string file in Directory.EnumerateFiles(root, "session*.moon*", SearchOption.AllDirectories))
    {
        Console.WriteLine(Path.GetRelativePath(root, file));
        try
        {
            byte[] raw = MoonFile.ReadRaw(file);
            var w = WalletRecord.Locate(raw);
            Console.WriteLine($"  {w}");

            byte[] again = MoonFile.Decode(MoonFile.Encode(raw));
            Check(again.AsSpan().SequenceEqual(raw), "Encode/Decode verlustfrei");

            byte[] shifted = MoonFile.Decode(MoonFile.Encode(w.WithBalance(raw, 1234567.89m)));
            var w2 = WalletRecord.Locate(shifted);
            Check(w2.BalanceOffset == w.BalanceOffset && w2.Balance == 1234567.89m, "Kontostand 1.234.567,89 gesetzt");
            Check(shifted.Length == raw.Length, "Länge unverändert");
            int diff = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i] != shifted[i]) diff++;
            Check(diff <= 16, $"nur Geld-Bytes geändert ({diff} Bytes)");
            if (slotDollarsFor(file) is double sd) Check(Math.Abs((double)w.Balance - sd) < 1e-6, $"Kontostand = slot.json dollars ({sd})");
        }
        catch (Exception ex) { failures++; Console.WriteLine("  [FAIL] " + ex.Message); }
    }

    foreach (string slotJson in Directory.EnumerateFiles(root, "slot.json", SearchOption.AllDirectories))
    {
        string slotDir = Path.GetDirectoryName(slotJson)!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(slotDir), @"^slot-\d+$")) continue;
        Console.WriteLine("E2E " + Path.GetRelativePath(root, slotDir));

        string temp = Path.Combine(Path.GetTempPath(), "MoonSelfTest-" + Guid.NewGuid().ToString("N"));
        string tempSlot = Path.Combine(temp, "MoonSessions", Path.GetFileName(slotDir));
        try
        {
            CopyWithoutChunks(slotDir, tempSlot);
            var store = new SaveStore(temp);
            var slot = store.LoadSlots().Single();
            Check(slot.CanEdit, $"Slot lesbar (Geld {slot.Balance:N2}, Error={slot.Error ?? "-"})");
            byte[] origSession = File.ReadAllBytes(slot.SessionPath!);
            byte[] origJson = File.ReadAllBytes(slot.SlotJsonPath);

            var result = store.SetBalance(slot, 1_000_000m);
            var reloaded = store.LoadSlots().Single();
            Check(reloaded.Dollars == 1_000_000, $"slot.json dollars = {reloaded.Dollars}");
            Check(reloaded.Balance == 1_000_000m, $"Team-Konto: {slot.Balance:N2} -> {reloaded.Balance:N2}");
            Console.WriteLine("  slot.json: " + File.ReadAllText(slot.SlotJsonPath));

            store.Restore(reloaded, result.BackupDir);
            Check(File.ReadAllBytes(slot.SessionPath!).AsSpan().SequenceEqual(origSession), "Restore session.moon identisch");
            Check(File.ReadAllBytes(slot.SlotJsonPath).AsSpan().SequenceEqual(origJson), "Restore slot.json identisch");
        }
        catch (Exception ex) { failures++; Console.WriteLine("  [FAIL] " + ex); }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }
}

Console.WriteLine(failures == 0 ? "ALLE TESTS OK" : $"{failures} FEHLER");
return failures == 0 ? 0 : 1;

// slot.json dollars of the live session.moon next to it (only for session.moon itself, not backups).
static double? slotDollarsFor(string sessionFile)
{
    if (Path.GetFileName(sessionFile) != "session.moon") return null;
    string json = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sessionFile)!)!, "slot.json");
    if (!File.Exists(json)) return null;
    var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(json));
    double d = node!["dollars"]!.GetValue<double>();
    return d == 6000.0 ? null : d; // 6000 was written by the old broken edit, not by the game
}

static void CopyWithoutChunks(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (string f in Directory.EnumerateFiles(from))
        if (!Path.GetFileName(f).StartsWith("chunk_")) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    foreach (string d in Directory.EnumerateDirectories(from))
        CopyWithoutChunks(d, Path.Combine(to, Path.GetFileName(d)));
}

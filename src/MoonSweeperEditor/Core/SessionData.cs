using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MoonSweeperEditor.Core;

public sealed class WalletEntry
{
    public required string Owner { get; init; }
    public decimal Dollars { get; set; }
    public decimal PendingKilograms { get; set; }
}

public sealed class OwnedTool
{
    public required string Id { get; init; }
    public int Value { get; set; }
}

public sealed class Inventory
{
    public required string Owner { get; init; }
    public int Header { get; set; }
    public List<OwnedTool> Tools { get; } = [];
}

public sealed class Hotbar
{
    public required string Owner { get; init; }
    public int Selected { get; set; }
    public List<string> Slots { get; } = [];
}

public sealed class Canister
{
    public required string Key { get; init; }
    public double Amount { get; set; }
    public double Capacity { get; set; }
}

public sealed class ToolLevel
{
    public required string Id { get; init; }
    public int Level { get; set; }
    public double Progress { get; set; }
}

public sealed class ToolLevelSet
{
    public required string Owner { get; init; }
    public string Equipped { get; set; } = "";
    public List<ToolLevel> Levels { get; } = [];
}

public sealed class TreeNode
{
    public required string Id { get; init; }
    public int Level { get; set; }
}

/// <summary>One lifetime statistic. Kind is a Loc key ("StatEarned", "StatEarnedBy", "StatEarned2").</summary>
public sealed class StatValue(string kind, string? player, int offset, double value)
{
    public string Kind { get; } = kind;
    public string? Player { get; } = player;
    public string Label => Player is null ? Loc.T(Kind) : Loc.T(Kind, Player);
    public int Offset { get; } = offset;
    public double Value { get; set; } = value;
}

public sealed class UpgradeTree
{
    public required string Owner { get; init; }
    public List<TreeNode> Nodes { get; } = [];
}

/// <summary>
/// The player-state block of the decompressed session data. Everything before it (terrain, regions)
/// and after it (robots, scores, landmarks) is kept byte for byte.
///
///   [statistics, see ReadStats]                          directly in front, patched in place
///   wallet        int32 n × (string owner, decimal dollars, decimal pendingKilograms)
///   inventories   int32 n × (string owner, int32, int32 m × (string tool, int32))
///   piles         int32 nextId, int32 n × (int32, string, 57 bytes)
///   buildings     int32 nextId, int32 n × (int32, string, 32 bytes)
///   hotbars       int32 n × (string owner, int32 selected, int32 m × string tool)
///   canisters     int32 n × (string key, double amount, double capacity)
///   conveyors     int32 nextId, int32 n × (int32, string, 48 bytes)
///   rotations     int32 n × 24 bytes, int32, int32
///   tool levels   int32 n × (string owner, string equipped, int32 m × (string tool, int32 level, double))
///   upgrade tree  int32 n × (string owner, int32 m × (string node, int32 level))
///
/// Strings and decimals use the .NET BinaryWriter layout. Offsets move between saves, so the block is
/// located by trying every "team" wallet entry and accepting only a parse that re-serializes byte-identically.
/// </summary>
public sealed class SessionData
{
    const int MinStatsSize = 21;
    static readonly byte[] TeamKey = [4, (byte)'t', (byte)'e', (byte)'a', (byte)'m'];
    static readonly Regex IdPattern = new(@"^[a-z][a-z0-9_]*$", RegexOptions.Compiled);
    static readonly Regex NodePattern = new(@"^[a-z]+\.[a-z0-9_]+$", RegexOptions.Compiled);

    public byte[] Raw { get; }
    public int Start { get; private set; }
    public int End { get; private set; }

    /// <summary>Lifetime statistics in front of the wallet (empty if the layout was not recognised).</summary>
    public List<StatValue> Stats { get; } = [];

    public List<WalletEntry> Wallet { get; } = [];
    public List<Inventory> Inventories { get; } = [];
    public List<Hotbar> Hotbars { get; } = [];
    public List<Canister> Canisters { get; } = [];
    public List<ToolLevelSet> ToolLevels { get; } = [];
    public List<UpgradeTree> Trees { get; } = [];

    public int PileCount { get; private set; }
    public int BuildingCount { get; private set; }
    public int ConveyorCount { get; private set; }

    byte[] piles = [], buildings = [], conveyors = [], rotations = [];

    SessionData(byte[] raw) => Raw = raw;

    public WalletEntry? Team => Wallet.FirstOrDefault(w => w.Owner == "team");

    public static SessionData Parse(byte[] raw)
    {
        var found = new List<SessionData>();
        int from = 0;
        while (true)
        {
            int i = raw.AsSpan(from).IndexOf(TeamKey);
            if (i < 0) break;
            i += from;
            foreach (int start in CandidateStarts(raw, i))
            {
                var parsed = TryParse(raw, start);
                if (parsed is not null && !found.Any(f => f.Start == parsed.Start)) found.Add(parsed);
            }
            from = i + 1;
        }

        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidDataException(Loc.T("ErrNoPlayerData")),
            _ => throw new InvalidDataException(Loc.T("ErrAmbiguous", found.Count)),
        };
    }

    // The wallet is normally written with "team" first; also allow a few entries in front of it.
    static IEnumerable<int> CandidateStarts(byte[] raw, int teamIndex)
    {
        int p = teamIndex;
        for (int k = 0; k < 4; k++)
        {
            if (p - 4 >= 0) yield return p - 4;
            int prev = -1;
            for (int len = 1; len <= 64; len++)
            {
                int c = p - 32 - len - 1;
                if (c < 4) break;
                if (raw[c] == len && IsPrintable(raw.AsSpan(c + 1, len))) { prev = c; break; }
            }
            if (prev < 0) yield break;
            p = prev;
        }
    }

    static SessionData? TryParse(byte[] raw, int start)
    {
        try
        {
            var data = new SessionData(raw) { Start = start };
            data.Read(new Reader(raw, start));
            byte[] again = data.SerializeBlock();
            if (!again.AsSpan().SequenceEqual(raw.AsSpan(start, data.End - start))) return null;
            data.ReadStats();
            return data;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException or EndOfStreamException or DecoderFallbackException or OverflowException)
        {
            return null;
        }
    }

    void Read(Reader r)
    {
        int n = r.Count(64);
        if (n == 0) throw new FormatException();
        for (int k = 0; k < n; k++)
            Wallet.Add(new WalletEntry { Owner = r.Text(), Dollars = r.Decimal(), PendingKilograms = r.Decimal() });

        n = r.Count(64);
        for (int k = 0; k < n; k++)
        {
            var inv = new Inventory { Owner = r.Text(), Header = r.I32() };
            int m = r.Count(256);
            for (int t = 0; t < m; t++) inv.Tools.Add(new OwnedTool { Id = r.Id(IdPattern), Value = r.I32() });
            Inventories.Add(inv);
        }

        int s = r.Pos;
        r.I32();
        PileCount = r.Count(10_000_000);
        for (int k = 0; k < PileCount; k++) { r.I32(); r.Text(); r.Skip(57); }
        piles = r.Slice(s);

        s = r.Pos;
        r.I32();
        BuildingCount = r.Count(10_000_000);
        for (int k = 0; k < BuildingCount; k++) { r.I32(); r.Text(); r.Skip(32); }
        buildings = r.Slice(s);

        n = r.Count(64);
        for (int k = 0; k < n; k++)
        {
            var bar = new Hotbar { Owner = r.Text(), Selected = r.I32() };
            int m = r.Count(64);
            for (int t = 0; t < m; t++)
            {
                string slot = r.Text();
                if (slot.Length > 0 && !IdPattern.IsMatch(slot)) throw new FormatException();
                bar.Slots.Add(slot);
            }
            Hotbars.Add(bar);
        }

        n = r.Count(1_000_000);
        for (int k = 0; k < n; k++)
            Canisters.Add(new Canister { Key = r.Text(), Amount = r.F64(), Capacity = r.F64() });

        s = r.Pos;
        r.I32();
        ConveyorCount = r.Count(10_000_000);
        for (int k = 0; k < ConveyorCount; k++) { r.I32(); r.Text(); r.Skip(48); }
        conveyors = r.Slice(s);

        s = r.Pos;
        n = r.Count(10_000_000);
        r.Skip(n * 24);
        r.I32();
        r.I32();
        rotations = r.Slice(s);

        n = r.Count(64);
        for (int k = 0; k < n; k++)
        {
            var set = new ToolLevelSet { Owner = r.Text(), Equipped = r.Text() };
            int m = r.Count(256);
            for (int t = 0; t < m; t++)
                set.Levels.Add(new ToolLevel { Id = r.Id(IdPattern), Level = r.I32(), Progress = r.F64() });
            ToolLevels.Add(set);
        }

        n = r.Count(64);
        for (int k = 0; k < n; k++)
        {
            var tree = new UpgradeTree { Owner = r.Text() };
            int m = r.Count(1024);
            for (int t = 0; t < m; t++) tree.Nodes.Add(new TreeNode { Id = r.Id(NodePattern), Level = r.I32() });
            Trees.Add(tree);
        }

        End = r.Pos;
    }

    // Statistics directly in front of the wallet:
    //   bool 1 | double earned | int32 n × (string player, double earned) | double earned
    // Found by scanning backwards for a layout that ends exactly at the wallet; only patched in place.
    void ReadStats()
    {
        for (int p = Start - MinStatsSize; p >= Math.Max(0, Start - MinStatsSize - 1024); p--)
        {
            if (Raw[p] != 1) continue;
            try
            {
                var r = new Reader(Raw, p + 1);
                var offsets = new List<(string Kind, string? Player, int Offset)> { ("StatEarned", null, r.Pos) };
                if (!Finite(r.F64())) continue;
                int n = r.Count(16);
                for (int k = 0; k < n; k++)
                {
                    string who = r.Text();
                    offsets.Add(("StatEarnedBy", who, r.Pos));
                    if (!Finite(r.F64())) throw new FormatException();
                }
                offsets.Add(("StatEarned2", null, r.Pos));
                if (!Finite(r.F64()) || r.Pos != Start) continue;

                Stats.AddRange(offsets.Select(o => new StatValue(o.Kind, o.Player, o.Offset, BitConverter.ToDouble(Raw, o.Offset))));
                return;
            }
            catch (FormatException) { }
            catch (DecoderFallbackException) { }
        }

        static bool Finite(double v) => double.IsFinite(v) && v >= 0;
    }

    byte[] SerializeBlock()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, new UTF8Encoding(false), leaveOpen: true))
        {
            w.Write(Wallet.Count);
            foreach (var e in Wallet) { w.Write(e.Owner); w.Write(e.Dollars); w.Write(e.PendingKilograms); }

            w.Write(Inventories.Count);
            foreach (var inv in Inventories)
            {
                w.Write(inv.Owner); w.Write(inv.Header); w.Write(inv.Tools.Count);
                foreach (var t in inv.Tools) { w.Write(t.Id); w.Write(t.Value); }
            }

            w.Write(piles);
            w.Write(buildings);

            w.Write(Hotbars.Count);
            foreach (var bar in Hotbars)
            {
                w.Write(bar.Owner); w.Write(bar.Selected); w.Write(bar.Slots.Count);
                foreach (var slot in bar.Slots) w.Write(slot);
            }

            w.Write(Canisters.Count);
            foreach (var c in Canisters) { w.Write(c.Key); w.Write(c.Amount); w.Write(c.Capacity); }

            w.Write(conveyors);
            w.Write(rotations);

            w.Write(ToolLevels.Count);
            foreach (var set in ToolLevels)
            {
                w.Write(set.Owner); w.Write(set.Equipped); w.Write(set.Levels.Count);
                foreach (var l in set.Levels) { w.Write(l.Id); w.Write(l.Level); w.Write(l.Progress); }
            }

            w.Write(Trees.Count);
            foreach (var tree in Trees)
            {
                w.Write(tree.Owner); w.Write(tree.Nodes.Count);
                foreach (var node in tree.Nodes) { w.Write(node.Id); w.Write(node.Level); }
            }
        }
        return ms.ToArray();
    }

    /// <summary>Returns new decompressed session data with all edits applied.</summary>
    public byte[] Build()
    {
        byte[] block = SerializeBlock();
        var result = new byte[Start + block.Length + (Raw.Length - End)];
        Raw.AsSpan(0, Start).CopyTo(result);
        block.CopyTo(result, Start);
        Raw.AsSpan(End).CopyTo(result.AsSpan(Start + block.Length));

        foreach (var stat in Stats)
            BinaryPrimitives.WriteDoubleLittleEndian(result.AsSpan(stat.Offset), stat.Value);
        return result;
    }

    static bool IsPrintable(ReadOnlySpan<byte> s)
    {
        foreach (byte ch in s)
            if (ch is < 0x20 or > 0x7e) return false;
        return true;
    }

    sealed class Reader(byte[] data, int pos)
    {
        static readonly UTF8Encoding Strict = new(false, throwOnInvalidBytes: true);

        public int Pos { get; private set; } = pos;
        readonly int start = pos;

        ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || Pos + n > data.Length) throw new FormatException();
            var span = data.AsSpan(Pos, n);
            Pos += n;
            return span;
        }

        public void Skip(int n) => Take(n);
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

        public int Count(int max)
        {
            int n = I32();
            if (n < 0 || n > max) throw new FormatException();
            return n;
        }

        public decimal Decimal()
        {
            var s = Take(16);
            int lo = BinaryPrimitives.ReadInt32LittleEndian(s), mid = BinaryPrimitives.ReadInt32LittleEndian(s[4..]);
            int hi = BinaryPrimitives.ReadInt32LittleEndian(s[8..]), flags = BinaryPrimitives.ReadInt32LittleEndian(s[12..]);
            if ((flags & 0x7F00FFFF) != 0 || ((flags >> 16) & 0xFF) > 28) throw new FormatException();
            return new decimal([lo, mid, hi, flags]);
        }

        public string Text()
        {
            int len = 0, shift = 0;
            while (true)
            {
                if (shift > 28) throw new FormatException();
                byte b = Take(1)[0];
                len |= (b & 0x7F) << shift;
                shift += 7;
                if (b < 0x80) break;
            }
            if (len > 512) throw new FormatException();
            string s = Strict.GetString(Take(len));
            foreach (char ch in s)
                if (char.IsControl(ch)) throw new FormatException();
            return s;
        }

        public string Id(Regex pattern)
        {
            string s = Text();
            if (!pattern.IsMatch(s)) throw new FormatException();
            return s;
        }

        public byte[] Slice(int from) => data.AsSpan(from, Pos - from).ToArray();

        public override string ToString() => $"@0x{Pos:X} (start 0x{start:X})";
    }
}

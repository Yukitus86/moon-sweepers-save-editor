using System.IO;
using System.Buffers.Binary;

namespace MoonSaveEditor.Core;

/// <summary>
/// The team wallet in the decompressed session data (MoonWorld.TeamWallet / MoonWalletSnapshot):
///
///   int32 count | count × (byte len, ASCII player, decimal dollars, decimal pendingKilograms)
///
/// Decimals use the BinaryWriter layout (lo, mid, hi, flags). The balance shown in game is the
/// dollars of the "team" entry. Offsets differ between saves, so the entry is located
/// structurally, never by a fixed offset.
/// </summary>
public sealed record WalletRecord(int BalanceOffset, decimal Balance)
{
    static readonly byte[] TeamKey = [4, (byte)'t', (byte)'e', (byte)'a', (byte)'m'];
    const int EntryValuesSize = 32;

    public static WalletRecord Locate(byte[] raw)
    {
        var found = new List<WalletRecord>();
        int from = 0;
        while (true)
        {
            int i = raw.AsSpan(from).IndexOf(TeamKey);
            if (i < 0) break;
            i += from;
            if (TryParseAt(raw, i) is { } record) found.Add(record);
            from = i + 1;
        }

        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidDataException(Loc.T("ErrNoWallet")),
            _ => throw new InvalidDataException(Loc.T("ErrAmbiguous", found.Count)),
        };
    }

    // The dictionary is usually written with "team" first; try every position for the count in front of it.
    static WalletRecord? TryParseAt(byte[] raw, int teamIndex)
    {
        for (int entriesBefore = 0; entriesBefore < 8; entriesBefore++)
        {
            int start = FindListStart(raw, teamIndex, entriesBefore);
            if (start < 0) continue;
            int count = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(start));
            if (count <= entriesBefore || count > 64) continue;
            if (!ListIsValid(raw, start, count)) continue;

            int balanceOffset = teamIndex + TeamKey.Length;
            if (!TryReadDecimal(raw, balanceOffset, out decimal balance)) continue;
            return new WalletRecord(balanceOffset, balance);
        }
        return null;
    }

    // Walks back over "entriesBefore" complete entries and returns the offset of the int32 count.
    static int FindListStart(byte[] raw, int entryIndex, int entriesBefore)
    {
        int p = entryIndex;
        for (int k = 0; k < entriesBefore; k++)
        {
            int found = -1;
            for (int len = 1; len <= 64; len++)
            {
                int candidate = p - EntryValuesSize - len - 1;
                if (candidate < 4) break;
                if (raw[candidate] == len && IsPrintable(raw.AsSpan(candidate + 1, len))) { found = candidate; break; }
            }
            if (found < 0) return -1;
            p = found;
        }
        return p - 4 >= 0 ? p - 4 : -1;
    }

    static bool ListIsValid(byte[] raw, int start, int count)
    {
        int p = start + 4;
        for (int k = 0; k < count; k++)
        {
            if (p >= raw.Length) return false;
            int len = raw[p++];
            if (len is 0 or > 64 || p + len + EntryValuesSize > raw.Length) return false;
            if (!IsPrintable(raw.AsSpan(p, len))) return false;
            p += len;
            if (!TryReadDecimal(raw, p, out _) || !TryReadDecimal(raw, p + 16, out _)) return false;
            p += EntryValuesSize;
        }
        return true;
    }

    static bool IsPrintable(ReadOnlySpan<byte> s)
    {
        foreach (byte ch in s)
            if (ch is < 0x20 or > 0x7e) return false;
        return true;
    }

    static bool TryReadDecimal(byte[] raw, int offset, out decimal value)
    {
        value = 0;
        var span = raw.AsSpan(offset, 16);
        int lo = BinaryPrimitives.ReadInt32LittleEndian(span);
        int mid = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        int hi = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        int flags = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
        // Only the sign bit and the scale byte (0..28) may be set.
        if ((flags & 0x7F00FFFF) != 0 || ((flags >> 16) & 0xFF) > 28) return false;
        value = new decimal([lo, mid, hi, flags]);
        return true;
    }

    /// <summary>Returns a copy of raw with the team balance replaced.</summary>
    public byte[] WithBalance(byte[] raw, decimal balance)
    {
        if (balance < 0)
            throw new ArgumentOutOfRangeException(nameof(balance), Loc.T("ErrNegative"));

        byte[] copy = (byte[])raw.Clone();
        int[] bits = decimal.GetBits(balance);
        for (int k = 0; k < 4; k++)
            BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(BalanceOffset + k * 4), bits[k]);
        return copy;
    }

    public override string ToString() => $"team balance={Balance} @0x{BalanceOffset:X}";
}

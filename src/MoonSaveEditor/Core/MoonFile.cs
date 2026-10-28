using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MoonSaveEditor.Core;

/// <summary>
/// Container format used by session.moon:
/// "1PCM" | uint32 rawSize | byte hashLen (0x40) | hashLen ASCII hex chars = SHA256(raw) | gzip(raw)
/// The game rejects the file (and falls back to session.moon.bak) when size or hash do not match.
/// </summary>
public static class MoonFile
{
    static readonly byte[] Magic = "1PCM"u8.ToArray();

    public static byte[] ReadRaw(string path) => Decode(File.ReadAllBytes(path));

    public static byte[] Decode(byte[] file)
    {
        if (file.Length < 9 || !file.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException(Loc.T("ErrNotPcm"));

        uint size = BitConverter.ToUInt32(file, 4);
        int hashLen = file[8];
        if (9 + hashLen > file.Length)
            throw new InvalidDataException(Loc.T("ErrHeader"));
        string storedHash = Encoding.ASCII.GetString(file, 9, hashLen);

        byte[] raw;
        using (var input = new MemoryStream(file, 9 + hashLen, file.Length - 9 - hashLen))
        using (var gz = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream((int)Math.Min(size, int.MaxValue)))
        {
            gz.CopyTo(output);
            raw = output.ToArray();
        }

        if (raw.Length != size)
            throw new InvalidDataException(Loc.T("ErrSize", size, raw.Length));
        if (!string.Equals(Hash(raw), storedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Loc.T("ErrHash"));

        return raw;
    }

    public static byte[] Encode(byte[] raw)
    {
        byte[] hash = Encoding.ASCII.GetBytes(Hash(raw));

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(BitConverter.GetBytes((uint)raw.Length));
        ms.WriteByte((byte)hash.Length);
        ms.Write(hash);
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(raw);
        return ms.ToArray();
    }

    public static string Hash(byte[] raw) => Convert.ToHexStringLower(SHA256.HashData(raw));
}

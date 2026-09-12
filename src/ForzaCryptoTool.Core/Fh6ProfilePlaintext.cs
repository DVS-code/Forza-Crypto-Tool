using System.Text;

namespace ForzaCryptoTool;

internal static class Fh6ProfilePlaintext
{
    private static readonly byte[] Preamble4 = { 0xB6, 0xF2, 0x8B, 0x4A };

    public static bool IsFh6Plaintext(byte[] data) =>
        data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual(Preamble4);

    private static List<int> FindAll(byte[] data, ReadOnlySpan<byte> needle)
    {
        var hits = new List<int>();
        for (int i = 0; i + needle.Length <= data.Length; i++)
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
                hits.Add(i);
        return hits;
    }

    public static List<int> FindXuidBytes(byte[] data, ulong xuid, bool bigEndian = false)
    {
        var bytes = BitConverter.GetBytes(xuid);
        if (bigEndian)
            Array.Reverse(bytes);
        return FindAll(data, bytes);
    }

    private static bool IsPlausibleXuid(ulong value)
    {
        const ulong lo = 0x0009_0000_0000_0000UL, hi = 0x0010_0000_0000_0000UL;
        if (value < lo || value >= hi) return false;

        return (value & 0x0000_FFFF_FFFF_FFFFUL) != 0;
    }

    private static List<(int Start, int Stop)> EmbeddedSqliteRanges(byte[] data)
    {
        var ranges = new List<(int Start, int Stop)>();
        var magic = Encoding.ASCII.GetBytes("SQLite format 3\0");
        for (int start = 0; start + magic.Length <= data.Length;)
        {
            int off = IndexOf(data, magic, start);
            if (off < 0) break;
            start = off + 1;
            if (off + 100 > data.Length) continue;

            int pageSize = (data[off + 16] << 8) | data[off + 17];
            if (pageSize == 1) pageSize = 65536;
            if (pageSize < 512 || pageSize > 65536 || (pageSize & (pageSize - 1)) != 0)
                continue;

            uint pageCount = ((uint)data[off + 28] << 24)
                | ((uint)data[off + 29] << 16)
                | ((uint)data[off + 30] << 8)
                | data[off + 31];

            long remainingPages = (data.Length - off) / pageSize;
            if (pageCount == 0 || pageCount > remainingPages)
                pageCount = (uint)remainingPages;
            long dbSize = (long)pageSize * pageCount;
            if (dbSize < pageSize || off + dbSize > data.Length)
                continue;
            ranges.Add((off, off + (int)dbSize));
        }
        return ranges;
    }

    private static int IndexOf(byte[] data, byte[] needle, int start)
    {
        for (int i = start; i + needle.Length <= data.Length; i++)
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }

    private static ulong ReadUInt64BigEndian(byte[] data, int off)
    {
        ulong value = 0;
        for (int i = 0; i < 8; i++)
            value = (value << 8) | data[off + i];
        return value;
    }

    public static (int Offset, ulong Xuid)? FindXuid(byte[] data, ulong? known = null)
    {
        int canon = CanonicalXuidOffset(data);
        if (canon >= 0)
        {
            ulong v = BitConverter.ToUInt64(data, canon);
            if (known is null || v == known) return (canon, v);
        }
        if (known is ulong k)
        {
            var hits = FindAll(data, BitConverter.GetBytes(k));
            return hits.Count == 1 ? (hits[0], k) : (hits.Count > 1 ? (hits[0], k) : ((int, ulong)?)null);
        }

        var sqliteRanges = EmbeddedSqliteRanges(data);
        if (sqliteRanges.Count == 0) return null;

        var values = new HashSet<ulong>();
        foreach (var (start, stop) in sqliteRanges)
        {
            for (int i = start; i + 8 <= stop; i++)
            {
                ulong v = ReadUInt64BigEndian(data, i);
                if (IsPlausibleXuid(v))
                    values.Add(v);
            }
        }

        var candidates = new List<(ulong Xuid, List<int> Le, List<int> Be, int Score)>();
        foreach (var value in values)
        {
            var le = FindXuidBytes(data, value, bigEndian: false);
            var be = new List<int>();
            var needle = BitConverter.GetBytes(value);
            Array.Reverse(needle);
            foreach (var (start, stop) in sqliteRanges)
            {
                for (int i = start; i + needle.Length <= stop; i++)
                    if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
                        be.Add(i);
            }
            if (le.Count > 0 && be.Count > 0)
                candidates.Add((value, le, be, (Math.Min(le.Count, be.Count) * 1000) + le.Count + be.Count));
        }

        if (candidates.Count == 0) return FrequencyFallbackXuid(data);
        var ordered = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Xuid).ToList();
        if (ordered.Count > 1 && ordered[0].Score <= ordered[1].Score * 2)
            return FrequencyFallbackXuid(data);
        return (ordered[0].Le[0], ordered[0].Xuid);
    }

    private static (int Offset, ulong Xuid)? FrequencyFallbackXuid(byte[] data)
    {
        var counts = new Dictionary<ulong, int>();
        for (int o = 0; o + 8 <= data.Length; o++)
        {
            ulong le = BitConverter.ToUInt64(data, o);
            if (le >= 0x0009010000000000UL && le <= 0x000901FFFFFFFFFFUL)
                counts[le] = counts.GetValueOrDefault(le) + 1;
            ulong be = ReadUInt64BigEndian(data, o);
            if (be >= 0x0009010000000000UL && be <= 0x000901FFFFFFFFFFUL)
                counts[be] = counts.GetValueOrDefault(be) + 1;
        }
        if (counts.Count == 0) return null;
        var top = counts.OrderByDescending(kv => kv.Value).First();
        if (top.Value < 2) return null;
        var leHits = FindXuidBytes(data, top.Key, bigEndian: false);
        var beHits = FindXuidBytes(data, top.Key, bigEndian: true);
        if (leHits.Count == 0 && beHits.Count == 0) return null;
        return ((leHits.Count > 0 ? leHits[0] : beHits[0]), top.Key);
    }

    public static int CanonicalXuidOffset(byte[] data)
    {
        if (data.Length < 4 || !data.AsSpan(0, 4).SequenceEqual(Preamble4)) return -1;
        int p = 0;
        int binaryBody = -1;
        for (int sec = 0; sec < 3; sec++)
        {
            if (p + 8 > data.Length) return -1;
            uint size = BitConverter.ToUInt32(data, p + 4);
            int body = p + 8;
            if (sec == 2) { binaryBody = body; break; }
            p = body + (int)size;
            if (p < 0) return -1;
        }
        if (binaryBody < 0) return -1;
        int off = binaryBody + 4;
        return off + 8 <= data.Length ? off : -1;
    }

    public static byte[] PatchXuid(byte[] data, ulong sourceXuid, ulong targetXuid, bool allowMultiple = false)
    {
        int off = CanonicalXuidOffset(data);
        if (off < 0)
            throw new InvalidOperationException("Could not locate the profile's canonical XUID field (unexpected layout).");
        var copy = (byte[])data.Clone();
        BitConverter.GetBytes(targetXuid).CopyTo(copy, off);
        return copy;
    }

    public sealed record Field(string Name, string Type, string Value, int Offset, bool Editable = false, int ValueOffset = -1, int Width = 0);

    public static byte[] PatchScalar(byte[] data, int valueOffset, int width, long newValue)
    {
        if (valueOffset < 0 || valueOffset + width > data.Length)
            throw new InvalidOperationException("Field offset is out of range.");
        var copy = (byte[])data.Clone();
        switch (width)
        {
            case 1: copy[valueOffset] = (byte)newValue; break;
            case 4: BitConverter.GetBytes((int)newValue).CopyTo(copy, valueOffset); break;
            case 8: BitConverter.GetBytes(newValue).CopyTo(copy, valueOffset); break;
            default: throw new InvalidOperationException($"Unsupported width {width}.");
        }
        return copy;
    }

    public static byte[] PatchInt32(byte[] data, int valueOffset, int newValue)
        => PatchScalar(data, valueOffset, 4, newValue);

    public static List<Field> ScanFields(byte[] data, int max = 1000)
    {
        var fields = new List<Field>();
        if (data.Length < 16 || !data.AsSpan(0, 4).SequenceEqual(Preamble4)) return fields;

        uint sectionSize = BitConverter.ToUInt32(data, 4);
        int poEnd = Math.Min(data.Length - 8, 8 + (int)sectionSize);

        int p = 8;
        while (p + 8 < poEnd && fields.Count < max)
        {
            uint nameLen = BitConverter.ToUInt32(data, p);
            if (nameLen >= 3 && nameLen <= 64 && p + 4 + nameLen + 8 <= data.Length)
            {
                var nameSpan = data.AsSpan(p + 4, (int)nameLen);
                if (IsAsciiNameBytes(nameSpan))
                {
                    uint dataLen = BitConverter.ToUInt32(data, p + 4 + (int)nameLen);
                    if (dataLen == 1 || dataLen == 4 || dataLen == 8)
                    {
                        int valOff = p + 4 + (int)nameLen + 4;
                        string name = Encoding.ASCII.GetString(nameSpan);
                        (string type, string val) = dataLen switch
                        {
                            1 => ("byte", data[valOff].ToString()),
                            4 => ("int32", BitConverter.ToInt32(data, valOff).ToString()),
                            _ => ("int64", BitConverter.ToInt64(data, valOff).ToString()),
                        };
                        fields.Add(new Field(name, type, val, valOff, true, valOff, (int)dataLen));
                        p = valOff + (int)dataLen;
                        if (p + 4 <= data.Length && BitConverter.ToUInt32(data, p) == 0x00002000) p += 4;
                        continue;
                    }
                }
            }
            p++;
        }
        return fields;
    }

    private static bool IsAsciiNameBytes(ReadOnlySpan<byte> s)
    {
        if (s.Length < 3) return false;
        foreach (var b in s)
            if (b < 0x30 || b > 0x7A) return false;
        return (s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= 'a' && s[0] <= 'z');
    }

    private static bool IsPrintableName(string s)
    {
        if (s.Length < 3) return false;
        foreach (var c in s)
            if (c < 'A' || c > 'z' || (c > 'Z' && c < 'a' && c != '_'))
                return false;
        return char.IsLetter(s[0]);
    }

    public static bool TryParseXuidText(string text, out ulong xuid)
    {
        xuid = 0;
        var value = (text ?? "").Trim();
        if (string.IsNullOrEmpty(value)) return false;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out xuid);
        if (ulong.TryParse(value, out xuid)) return true;
        return ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out xuid);
    }

    private static string ReadHint(byte[] data, int off)
    {
        if (off + 12 > data.Length) return "";

        uint a = BitConverter.ToUInt32(data, off);
        if (off + 12 <= data.Length)
        {
            uint len = BitConverter.ToUInt32(data, off + 4);
            if (len == 4) return BitConverter.ToInt32(data, off + 8).ToString();
            if (len == 9)
                return "";
        }
        return $"0x{a:X8}";
    }
}

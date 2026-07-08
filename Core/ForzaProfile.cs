namespace ForzaCryptoTool;

internal static class ForzaProfile
{
    public const uint HashSeed = 0x1505;
    public static readonly string[] KnownSections = { "profile", "savestate", "binary" };

    private static readonly byte[] Fh6Magic = "FH6S"u8.ToArray();

    public enum Container { Fh5SectionFormat, Fh6Magic, Unknown }

    public static Container DetectContainer(byte[] data)
    {
        if (data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual(Fh6Magic))
            return Container.Fh6Magic;
        return TryParse(data) is not null ? Container.Fh5SectionFormat : Container.Unknown;
    }

    public sealed record Section(string? Name, uint NameHash, int HeaderOffset, int DataOffset, int Size)
    {
        public string Label => Name ?? $"0x{NameHash:X8}";
    }

    public sealed record Layout(IReadOnlyList<Section> Sections, int? XuidOffset)
    {
        public Section? this[string name] =>
            Sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static uint HashName(string name)
    {
        uint val = HashSeed;
        for (int pass = 0; pass < 2; pass++)
            foreach (var ch in name)
                val = (uint)(val ^ (ch + (val >> 2) + 32u * val));
        return val;
    }

    private static readonly Dictionary<uint, string> KnownByHash =
        KnownSections.ToDictionary(HashName, n => n);

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.ToUInt32(data.Slice(offset, 4));

    public static Layout? TryParse(byte[] data)
    {
        if (data.Length < 8)
            return null;

        var sections = new List<Section>();
        int offset = 0;
        bool sawKnown = false;

        while (offset + 8 <= data.Length)
        {
            uint nameHash = ReadU32(data, offset);
            int size = (int)ReadU32(data, offset + 4);
            int dataOffset = offset + 8;
            if (size < 0 || dataOffset + size > data.Length)
                break;

            KnownByHash.TryGetValue(nameHash, out var name);
            if (name is not null)
                sawKnown = true;
            sections.Add(new Section(name, nameHash, offset, dataOffset, size));
            offset = dataOffset + size;

            if (sections.Count >= KnownSections.Length && sawKnown)
                break;
        }

        if (!sawKnown || sections.Count == 0)
            return null;

        return new Layout(sections, FindXuidOffset(data, sections));
    }

    private static int? FindXuidOffset(byte[] data, List<Section> sections)
    {
        var binary = sections.FirstOrDefault(s => s.Name == "binary");
        if (binary is null || binary.Size < 12)
            return null;
        int xuidOffset = binary.DataOffset + 4;
        return xuidOffset + 8 <= data.Length ? xuidOffset : null;
    }

    public static ulong? ReadXuid(byte[] data)
    {
        var layout = TryParse(data);
        if (layout?.XuidOffset is not int off)
            return null;
        return BitConverter.ToUInt64(data, off);
    }

    public static byte[] PatchXuid(byte[] data, ulong newXuid)
    {
        var layout = TryParse(data) ?? throw new InvalidOperationException("Not a recognizable Forza profile container.");
        if (layout.XuidOffset is not int offset)
            throw new InvalidOperationException("Could not locate the XUID in this profile.");

        var existing = data.AsSpan(offset, 8).ToArray();
        int matches = 0;
        for (int i = 0; i + 8 <= data.Length; i++)
        {
            if (data.AsSpan(i, 8).SequenceEqual(existing))
                matches++;
            if (matches > 1)
                break;
        }
        if (matches != 1)
            throw new InvalidOperationException("XUID location could not be uniquely identified; refusing to patch.");

        var copy = (byte[])data.Clone();
        BitConverter.GetBytes(newXuid).CopyTo(copy, offset);
        return copy;
    }

    public static bool TryParseXuid(string text, out ulong xuid)
    {
        xuid = 0;
        var value = text.Trim();
        if (string.IsNullOrEmpty(value))
            return false;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out xuid);
        if (ulong.TryParse(value, out xuid))
            return true;
        return ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out xuid);
    }
}

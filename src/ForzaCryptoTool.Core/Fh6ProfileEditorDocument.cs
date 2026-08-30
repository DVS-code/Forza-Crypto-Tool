using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ForzaCryptoTool;

internal sealed class Fh6ProfileEditorDocument
{
    private const uint ProfileMarker = 0x4A8BF2B6;
    private const int MaxProfileBytes = 64 * 1024 * 1024;
    private static readonly string[] SectionNames = ["profile", "savestate", "binary", "database"];

    private readonly byte[] _originalInput;
    private readonly byte[] _originalInflated;
    private readonly bool _wasCompressed;
    private readonly List<ProfileSection> _sections;

    private Fh6ProfileEditorDocument(
        byte[] originalInput,
        byte[] inflated,
        bool wasCompressed,
        List<ProfileSection> sections,
        ProfilePropertyTree properties,
        BxmlState bxml,
        BinaryCareerState binary)
    {
        _originalInput = originalInput;
        _originalInflated = inflated;
        _wasCompressed = wasCompressed;
        _sections = sections;
        Properties = properties;
        Bxml = bxml;
        Binary = binary;
    }

    public ProfilePropertyTree Properties { get; }
    public BxmlState Bxml { get; }
    public BinaryCareerState Binary { get; }
    public byte[] DatabaseBytes => _sections[3].Payload;
    public bool WasCompressed => _wasCompressed;
    public int OriginalSize => _originalInput.Length;
    public int InflatedSize => _originalInflated.Length;
    public IReadOnlyList<ProfileSection> Sections => _sections;

    public static Fh6ProfileEditorDocument Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxProfileBytes)
            throw new InvalidDataException($"ProfileData exceeds the {MaxProfileBytes / (1024 * 1024)} MB safety limit.");
        return Parse(File.ReadAllBytes(path));
    }

    public static Fh6ProfileEditorDocument Parse(byte[] input)
    {
        if (input.Length < 8)
            throw new InvalidDataException("ProfileData is too short.");

        bool compressed = BinaryPrimitives.ReadUInt32LittleEndian(input) != ProfileMarker;
        byte[] inflated;
        if (compressed)
        {
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(input);
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(4));
            if (uncompressedSize > MaxProfileBytes)
                throw new InvalidDataException($"Inflated ProfileData exceeds the {MaxProfileBytes / (1024 * 1024)} MB safety limit.");
            if (compressedSize != input.Length - 8)
                throw new InvalidDataException($"Compressed length mismatch: header {compressedSize:N0}, file {input.Length - 8:N0}.");
            using var source = new MemoryStream(input, 8, input.Length - 8, writable: false);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream((int)Math.Min(uncompressedSize, int.MaxValue));
            byte[] buffer = new byte[81920];
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > uncompressedSize)
                    throw new InvalidDataException("Inflated ProfileData exceeds its declared size.");
                output.Write(buffer, 0, read);
            }
            inflated = output.ToArray();
            if (inflated.Length != uncompressedSize)
                throw new InvalidDataException($"Inflated length mismatch: header {uncompressedSize:N0}, actual {inflated.Length:N0}.");
        }
        else
        {
            inflated = (byte[])input.Clone();
        }

        var sections = ParseSections(inflated);
        if (sections.Count != 4)
            throw new InvalidDataException($"Expected 4 FH6 ProfileData sections, found {sections.Count}.");
        for (int i = 0; i < SectionNames.Length; i++)
        {
            uint expected = ForzaNameHash(SectionNames[i]);
            if (sections[i].Marker != expected)
                throw new InvalidDataException($"{SectionNames[i]} section marker mismatch: expected 0x{expected:X8}, found 0x{sections[i].Marker:X8}.");
        }

        var properties = ProfilePropertyTree.Parse(sections[0].Payload);

        var paths = properties.Walk().Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        if (!paths.Contains("/Main/BpmMatchedEnviornment") || !paths.Contains("/Main/ProximityRadarVolume"))
            throw new InvalidDataException("This profile does not match the supported Forza Horizon 6 property schema.");
        if (paths.Contains("/Main/HDRExposure"))
            throw new InvalidDataException("This appears to be an FH5 profile. Only Forza Horizon 6 is supported.");

        var bxml = BxmlState.Parse(sections[1].Payload);
        var binary = BinaryCareerState.Parse(sections[2].Payload);
        if (!sections[3].Payload.AsSpan().StartsWith("SQLite format 3\0"u8))
            throw new InvalidDataException("The FH6 database section is not a SQLite database.");

        return new Fh6ProfileEditorDocument((byte[])input.Clone(), inflated, compressed, sections, properties, bxml, binary);
    }

    public byte[] Serialize(byte[]? databaseBytes = null)
    {
        var payloads = new[]
        {
            Properties.Serialize(),
            Bxml.Serialize(),
            Binary.Serialize(),
            databaseBytes ?? _sections[3].Payload,
        };
        if (!payloads[3].AsSpan().StartsWith("SQLite format 3\0"u8))
            throw new InvalidDataException("Edited database is not a SQLite database.");

        using var inflated = new MemoryStream();
        for (int i = 0; i < payloads.Length; i++)
        {
            WriteUInt32(inflated, _sections[i].Marker);
            WriteUInt32(inflated, checked((uint)payloads[i].Length));
            inflated.Write(payloads[i]);
        }
        byte[] plain = inflated.ToArray();
        if (plain.Length > MaxProfileBytes)
            throw new InvalidDataException($"Edited ProfileData exceeds the {MaxProfileBytes / (1024 * 1024)} MB safety limit.");
        if (!_wasCompressed)
            return plain;

        if (plain.AsSpan().SequenceEqual(_originalInflated))
            return (byte[])_originalInput.Clone();

        using var compressed = new MemoryStream();
        compressed.Position = 8;
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(plain);
        byte[] result = compressed.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)(result.Length - 8)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)plain.Length));
        return result;
    }

    public void ReplaceDatabase(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("SQLite format 3\0"u8))
            throw new InvalidDataException("Replacement database is not SQLite.");
        _sections[3] = _sections[3] with { Payload = (byte[])bytes.Clone() };
    }

    public IEnumerable<ScannedString> ScanStrings(ProfileStringSource source, int minimumLength = 5)
    {
        byte[] bytes = source switch
        {
            ProfileStringSource.Binary => Binary.Serialize(),
            ProfileStringSource.Database => DatabaseBytes,
            ProfileStringSource.Bxml => Bxml.Serialize(),
            _ => _originalInflated,
        };
        int start = -1;
        for (int i = 0; i <= bytes.Length; i++)
        {
            bool printable = i < bytes.Length && ((bytes[i] >= 32 && bytes[i] <= 126) || bytes[i] == 9);
            if (printable && start < 0) start = i;
            else if (!printable && start >= 0)
            {
                if (i - start >= minimumLength)
                    yield return new ScannedString(start, Encoding.UTF8.GetString(bytes, start, i - start));
                start = -1;
            }
        }
    }

    private static List<ProfileSection> ParseSections(byte[] data)
    {
        var result = new List<ProfileSection>();
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 8)
                throw new InvalidDataException($"Truncated section header at 0x{offset:X}.");
            uint marker = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            long end = (long)offset + 8 + size;
            if (end > data.Length)
                throw new InvalidDataException($"Section 0x{marker:X8} at 0x{offset:X} overruns ProfileData.");
            result.Add(new ProfileSection(marker, data.AsSpan(offset + 8, (int)size).ToArray(), offset));
            offset = (int)end;
        }
        return result;
    }

    internal static uint ForzaNameHash(string value, uint seed = 0x1505)
    {
        uint result = seed;
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        for (int pass = 0; pass < 2; pass++)
            foreach (byte b in encoded)
                result ^= b + (result >> 2) + 32 * result;
        return result;
    }

    internal static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    internal static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}

internal enum ProfileStringSource { All, Bxml, Binary, Database }
internal sealed record ScannedString(int Offset, string Value);
internal sealed record ProfileSection(uint Marker, byte[] Payload, int Offset);

internal sealed class ProfilePropertyTree
{
    private const int MaxTreeDepth = 256;
    private static readonly Dictionary<uint, PropertyKind> Kinds = new()
    {
        [0] = PropertyKind.Bool,
        [1] = PropertyKind.UInt8,
        [2] = PropertyKind.UInt16,
        [3] = PropertyKind.UInt32,
        [4] = PropertyKind.UInt64,
        [5] = PropertyKind.Int8,
        [6] = PropertyKind.Int16,
        [7] = PropertyKind.Int32,
        [8] = PropertyKind.Int64,
        [9] = PropertyKind.Float32,
        [10] = PropertyKind.Float64,
        [11] = PropertyKind.StringNarrow,
        [12] = PropertyKind.StringWide,
        [13] = PropertyKind.Matrix,
        [14] = PropertyKind.Vector,
        [15] = PropertyKind.PropertyBag,
        [16] = PropertyKind.DatabasePropertyBag,
        [17] = PropertyKind.ProtectedUInt32,
    };

    private readonly byte[] _trailer;
    private readonly List<List<uint>> _hashSets;

    private ProfilePropertyTree(List<ProfileProperty> roots, byte[] trailer, List<List<uint>> hashSets)
    {
        Roots = roots;
        _trailer = trailer;
        _hashSets = hashSets;
    }

    public List<ProfileProperty> Roots { get; }
    public IReadOnlyList<IReadOnlyList<uint>> HashSets => _hashSets;

    public void SetHashValue(int setIndex, int valueIndex, uint value)
    {
        if (setIndex < 0 || setIndex >= _hashSets.Count) throw new ArgumentOutOfRangeException(nameof(setIndex));
        var set = _hashSets[setIndex];
        if (valueIndex < 0 || valueIndex >= set.Count) throw new ArgumentOutOfRangeException(nameof(valueIndex));
        set[valueIndex] = value;
        set.Sort();
    }

    public static ProfilePropertyTree Parse(byte[] data)
    {
        var reader = new SpanReader(data);
        uint count = reader.ReadUInt32();
        if (count > 10_000) throw new InvalidDataException($"Unreasonable root property count: {count}.");
        var roots = new List<ProfileProperty>((int)count);
        for (int i = 0; i < count; i++) roots.Add(ReadProperty(reader, 0));
        byte[] trailer = reader.ReadRemaining();
        return new ProfilePropertyTree(roots, trailer, ParseHashSets(trailer));
    }

    public IEnumerable<(string Path, ProfileProperty Property)> Walk()
    {
        foreach (var root in Roots)
            foreach (var item in root.Walk(""))
                yield return item;
    }

    public ProfileProperty Find(string path)
    {
        string normalized = "/" + path.Trim('/');
        return Walk().FirstOrDefault(item => item.Path == normalized).Property
            ?? throw new KeyNotFoundException(path);
    }

    public byte[] Serialize()
    {
        using var output = new MemoryStream();
        Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)Roots.Count));
        foreach (var root in Roots) WriteProperty(output, root);
        if (_hashSets.Count == 2)
        {
            foreach (var set in _hashSets)
            {
                set.Sort();
                Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)set.Count));
                foreach (uint value in set) Fh6ProfileEditorDocument.WriteUInt32(output, value);
            }
        }
        else output.Write(_trailer);
        return output.ToArray();
    }

    private static ProfileProperty ReadProperty(SpanReader reader, int depth)
    {
        if (depth > MaxTreeDepth) throw new InvalidDataException("Property tree exceeds the supported nesting depth.");
        int offset = reader.Offset;
        byte caseSensitive = reader.ReadByte();
        uint hashBits = reader.ReadUInt32();
        if (caseSensitive > 1 || hashBits != 32)
            throw new InvalidDataException($"Invalid property hash metadata at 0x{offset:X}.");
        string name = reader.ReadUtf8(reader.ReadBoundedLength("property name", 16_384));
        uint typeId = reader.ReadUInt32();
        if (!Kinds.TryGetValue(typeId, out var kind))
            throw new InvalidDataException($"Unknown FH6 property variant {typeId} for {name} at 0x{offset:X}.");
        int valueOffset = reader.Offset;
        object? value;
        var children = new List<ProfileProperty>();
        switch (kind)
        {
            case PropertyKind.Bool: value = reader.ReadByte() != 0; break;
            case PropertyKind.UInt8: value = reader.ReadByte(); break;
            case PropertyKind.UInt16: value = reader.ReadUInt16(); break;
            case PropertyKind.UInt32:
            case PropertyKind.ProtectedUInt32: value = reader.ReadUInt32(); break;
            case PropertyKind.UInt64: value = reader.ReadUInt64(); break;
            case PropertyKind.Int8: value = unchecked((sbyte)reader.ReadByte()); break;
            case PropertyKind.Int16: value = reader.ReadInt16(); break;
            case PropertyKind.Int32: value = reader.ReadInt32(); break;
            case PropertyKind.Int64: value = reader.ReadInt64(); break;
            case PropertyKind.Float32: value = reader.ReadSingle(); break;
            case PropertyKind.Float64: value = reader.ReadDouble(); break;
            case PropertyKind.StringNarrow: value = reader.ReadUtf8(reader.ReadBoundedLength("string", int.MaxValue)); break;
            case PropertyKind.StringWide: value = reader.ReadUtf16(reader.ReadBoundedLength("wide string", int.MaxValue)); break;
            case PropertyKind.Matrix: value = reader.ReadBytes(64); break;
            case PropertyKind.Vector: value = reader.ReadBytes(16); break;
            case PropertyKind.PropertyBag:
            case PropertyKind.DatabasePropertyBag:
                uint count = reader.ReadUInt32();
                if (count > 100_000) throw new InvalidDataException($"Unreasonable child count in {name}.");
                for (int i = 0; i < count; i++) children.Add(ReadProperty(reader, depth + 1));
                value = null;
                break;
            default: throw new InvalidDataException($"Unsupported property kind {kind}.");
        }
        return new ProfileProperty(name, typeId, kind, value, children, caseSensitive, hashBits, offset, valueOffset);
    }

    private static void WriteProperty(Stream output, ProfileProperty node)
    {
        output.WriteByte(node.CaseSensitive);
        Fh6ProfileEditorDocument.WriteUInt32(output, node.HashBits);
        byte[] name = Encoding.UTF8.GetBytes(node.Name);
        Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)name.Length));
        output.Write(name);
        Fh6ProfileEditorDocument.WriteUInt32(output, node.TypeId);
        switch (node.Kind)
        {
            case PropertyKind.Bool: output.WriteByte((bool)node.Value! ? (byte)1 : (byte)0); break;
            case PropertyKind.UInt8: output.WriteByte(Convert.ToByte(node.Value, CultureInfo.InvariantCulture)); break;
            case PropertyKind.UInt16: WritePrimitive(output, BitConverter.GetBytes(Convert.ToUInt16(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.UInt32:
            case PropertyKind.ProtectedUInt32: Fh6ProfileEditorDocument.WriteUInt32(output, Convert.ToUInt32(node.Value, CultureInfo.InvariantCulture)); break;
            case PropertyKind.UInt64: Fh6ProfileEditorDocument.WriteUInt64(output, Convert.ToUInt64(node.Value, CultureInfo.InvariantCulture)); break;
            case PropertyKind.Int8: output.WriteByte(unchecked((byte)Convert.ToSByte(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.Int16: WritePrimitive(output, BitConverter.GetBytes(Convert.ToInt16(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.Int32: WritePrimitive(output, BitConverter.GetBytes(Convert.ToInt32(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.Int64: WritePrimitive(output, BitConverter.GetBytes(Convert.ToInt64(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.Float32: WritePrimitive(output, BitConverter.GetBytes(Convert.ToSingle(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.Float64: WritePrimitive(output, BitConverter.GetBytes(Convert.ToDouble(node.Value, CultureInfo.InvariantCulture))); break;
            case PropertyKind.StringNarrow:
                WriteString(output, Encoding.UTF8.GetBytes(Convert.ToString(node.Value, CultureInfo.InvariantCulture) ?? ""), false); break;
            case PropertyKind.StringWide:
                WriteString(output, Encoding.Unicode.GetBytes(Convert.ToString(node.Value, CultureInfo.InvariantCulture) ?? ""), true); break;
            case PropertyKind.Matrix:
            case PropertyKind.Vector: output.Write((byte[])node.Value!); break;
            case PropertyKind.PropertyBag:
            case PropertyKind.DatabasePropertyBag:
                Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)node.Children.Count));
                foreach (var child in node.Children) WriteProperty(output, child);
                break;
        }
    }

    private static void WritePrimitive(Stream output, byte[] bytes) => output.Write(bytes);
    private static void WriteString(Stream output, byte[] bytes, bool wide)
    {
        Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)(wide ? bytes.Length / 2 : bytes.Length)));
        output.Write(bytes);
    }

    private static List<List<uint>> ParseHashSets(byte[] trailer)
    {
        try
        {
            var reader = new SpanReader(trailer);
            var sets = new List<List<uint>>(2);
            for (int setIndex = 0; setIndex < 2; setIndex++)
            {
                uint count = reader.ReadUInt32();
                if (count > 100_000) return [];
                var set = new List<uint>((int)count);
                for (int i = 0; i < count; i++) set.Add(reader.ReadUInt32());
                if (!set.SequenceEqual(set.Order())) return [];
                sets.Add(set);
            }
            return reader.Remaining == 0 ? sets : [];
        }
        catch (InvalidDataException) { return []; }
    }
}

internal enum PropertyKind
{
    Bool, UInt8, UInt16, UInt32, UInt64, Int8, Int16, Int32, Int64,
    Float32, Float64, StringNarrow, StringWide, Matrix, Vector,
    PropertyBag, DatabasePropertyBag, ProtectedUInt32,
}

internal sealed class ProfileProperty(
    string name, uint typeId, PropertyKind kind, object? value, List<ProfileProperty> children,
    byte caseSensitive, uint hashBits, int offset, int valueOffset)
{
    public string Name { get; } = name;
    public uint TypeId { get; } = typeId;
    public PropertyKind Kind { get; } = kind;
    public object? Value { get; set; } = value;
    public List<ProfileProperty> Children { get; } = children;
    public byte CaseSensitive { get; } = caseSensitive;
    public uint HashBits { get; } = hashBits;
    public int Offset { get; } = offset;
    public int ValueOffset { get; } = valueOffset;
    public bool Editable => Kind is not (PropertyKind.Matrix or PropertyKind.Vector or PropertyKind.PropertyBag or PropertyKind.DatabasePropertyBag);
    public string TypeName => Kind == PropertyKind.ProtectedUInt32 ? "ProtectedUInt32" : Kind.ToString();
    public string DisplayValue => Kind switch
    {
        PropertyKind.Matrix or PropertyKind.Vector => Convert.ToHexString((byte[])Value!),
        PropertyKind.Float32 => ((float)Value!).ToString("R", CultureInfo.InvariantCulture),
        PropertyKind.Float64 => ((double)Value!).ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "",
    };

    public IEnumerable<(string Path, ProfileProperty Property)> Walk(string parent)
    {
        string path = parent + "/" + Name;
        yield return (path, this);
        foreach (var child in Children)
            foreach (var item in child.Walk(path)) yield return item;
    }

    public void SetFromText(string text)
    {
        try
        {
            Value = Kind switch
            {
                PropertyKind.Bool => bool.Parse(text),
                PropertyKind.UInt8 => ParseUnsigned(text, byte.MaxValue),
                PropertyKind.UInt16 => ParseUnsigned(text, ushort.MaxValue),
                PropertyKind.UInt32 or PropertyKind.ProtectedUInt32 => ParseUnsigned(text, uint.MaxValue),
                PropertyKind.UInt64 => ParseUnsigned64(text),
                PropertyKind.Int8 => sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                PropertyKind.Int16 => short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                PropertyKind.Int32 => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                PropertyKind.Int64 => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                PropertyKind.Float32 => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                PropertyKind.Float64 => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                PropertyKind.StringNarrow or PropertyKind.StringWide => text,
                _ => throw new InvalidOperationException($"{TypeName} is read-only."),
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new InvalidDataException($"{text} is not a valid {TypeName} value.", ex);
        }
    }

    private static ulong ParseUnsigned64(string text) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? ulong.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static object ParseUnsigned(string text, ulong maximum)
    {
        ulong parsed = ParseUnsigned64(text);
        if (parsed > maximum) throw new OverflowException();
        if (maximum == byte.MaxValue) return (byte)parsed;
        if (maximum == ushort.MaxValue) return (ushort)parsed;
        return (uint)parsed;
    }
}

internal sealed class BxmlState
{
    private const int MaxTreeDepth = 256;
    private BxmlState(byte version, List<string> strings, byte documentFlag, BxmlNode root)
    { Version = version; Strings = strings; DocumentFlag = documentFlag; Root = root; }

    public byte Version { get; }
    public List<string> Strings { get; }
    public byte DocumentFlag { get; set; }
    public BxmlNode Root { get; }

    public static BxmlState Parse(byte[] data)
    {
        if (data.Length < 13 || !data.AsSpan(0, 4).SequenceEqual("BXML"u8))
            throw new InvalidDataException("Save-state section is not BXML.");
        byte version = data[4];
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(5));
        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(9));
        if (count > 1_000_000 || 13L + blockSize > data.Length)
            throw new InvalidDataException("Invalid BXML string-table bounds.");
        var stringsReader = new SpanReader(data.AsSpan(13, (int)blockSize).ToArray());
        var strings = new List<string>((int)count);
        for (int i = 0; i < count; i++) strings.Add(stringsReader.ReadUtf8(stringsReader.ReadUInt16()));
        if (stringsReader.Remaining != 0) throw new InvalidDataException("BXML string table has trailing bytes.");
        var tokenReader = new SpanReader(data.AsSpan(13 + (int)blockSize).ToArray());
        byte documentFlag = tokenReader.ReadByte();
        int width = count > 0xFFFF ? 4 : count > 0xFF ? 2 : 1;
        BxmlNode root = ReadNode(tokenReader, width, count, 0);
        if (tokenReader.Remaining != 0) throw new InvalidDataException("BXML token stream has trailing bytes.");
        return new BxmlState(version, strings, documentFlag, root);
    }

    public IEnumerable<(int Depth, BxmlNode Node)> Walk() => Root.Walk(0);

    public byte[] Serialize()
    {
        using var stringData = new MemoryStream();
        Span<byte> length = stackalloc byte[2];
        foreach (string value in Strings)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("A BXML string exceeds 65535 bytes.");
            BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)bytes.Length);
            stringData.Write(length); stringData.Write(bytes);
        }
        using var output = new MemoryStream();
        output.Write("BXML"u8); output.WriteByte(Version);
        Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)Strings.Count));
        Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)stringData.Length));
        stringData.Position = 0; stringData.CopyTo(output);
        output.WriteByte(DocumentFlag);
        int width = Strings.Count > 0xFFFF ? 4 : Strings.Count > 0xFF ? 2 : 1;
        WriteNode(output, Root, width);
        return output.ToArray();
    }

    public int Intern(string value)
    {
        int index = Strings.IndexOf(value);
        if (index >= 0) return index;
        Strings.Add(value);
        return Strings.Count - 1;
    }

    private static BxmlNode ReadNode(SpanReader reader, int width, uint stringCount, int depth)
    {
        if (depth > MaxTreeDepth) throw new InvalidDataException("BXML tree exceeds the supported nesting depth.");
        int offset = reader.Offset;
        byte flags = reader.ReadByte();
        if ((flags & ~7) != 0) throw new InvalidDataException($"Invalid BXML flags 0x{flags:X2} at 0x{offset:X}.");
        uint name = reader.ReadIndex(width);
        if (name >= stringCount) throw new InvalidDataException("BXML node name index is out of range.");
        var attributes = new List<BxmlAttribute>();
        if ((flags & 2) != 0)
        {
            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                uint key = reader.ReadIndex(width), value = reader.ReadIndex(width);
                if (key >= stringCount || value >= stringCount) throw new InvalidDataException("BXML attribute index is out of range.");
                attributes.Add(new BxmlAttribute((int)key, (int)value));
            }
        }
        var children = new List<BxmlNode>();
        if ((flags & 4) != 0)
        {
            int count = reader.ReadUInt16();
            for (int i = 0; i < count; i++) children.Add(ReadNode(reader, width, stringCount, depth + 1));
        }
        return new BxmlNode(flags, (int)name, attributes, children, offset);
    }

    private static void WriteNode(Stream output, BxmlNode node, int width)
    {
        byte flags = node.Flags;
        flags = node.Attributes.Count > 0 ? (byte)(flags | 2) : (byte)(flags & ~2);
        flags = node.Children.Count > 0 ? (byte)(flags | 4) : (byte)(flags & ~4);
        output.WriteByte(flags); WriteIndex(output, node.NameIndex, width);
        if (node.Attributes.Count > 0)
        {
            if (node.Attributes.Count > byte.MaxValue) throw new InvalidDataException("BXML node has too many attributes.");
            output.WriteByte((byte)node.Attributes.Count);
            foreach (var attribute in node.Attributes)
            { WriteIndex(output, attribute.KeyIndex, width); WriteIndex(output, attribute.ValueIndex, width); }
        }
        if (node.Children.Count > 0)
        {
            if (node.Children.Count > ushort.MaxValue) throw new InvalidDataException("BXML node has too many children.");
            Span<byte> count = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)node.Children.Count); output.Write(count);
            foreach (var child in node.Children) WriteNode(output, child, width);
        }
    }

    private static void WriteIndex(Stream output, int value, int width)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (width == 1) output.WriteByte(checked((byte)value));
        else if (width == 2) { BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value)); output.Write(bytes[..2]); }
        else { BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value)); output.Write(bytes); }
    }
}

internal sealed class BxmlNode(byte flags, int nameIndex, List<BxmlAttribute> attributes, List<BxmlNode> children, int offset)
{
    public byte Flags { get; set; } = flags;
    public int NameIndex { get; set; } = nameIndex;
    public List<BxmlAttribute> Attributes { get; } = attributes;
    public List<BxmlNode> Children { get; } = children;
    public int Offset { get; } = offset;
    public IEnumerable<(int Depth, BxmlNode Node)> Walk(int depth)
    {
        yield return (depth, this);
        foreach (var child in Children)
            foreach (var item in child.Walk(depth + 1)) yield return item;
    }
}
internal sealed class BxmlAttribute(int keyIndex, int valueIndex)
{
    public int KeyIndex { get; set; } = keyIndex;
    public int ValueIndex { get; set; } = valueIndex;
}

internal sealed class BinaryCareerState
{
    private BinaryCareerState(ulong xuid, List<BinaryCareerRecord> records) { Xuid = xuid; Records = records; }
    public ulong Xuid { get; set; }
    public List<BinaryCareerRecord> Records { get; }

    public static BinaryCareerState Parse(byte[] data)
    {
        var reader = new SpanReader(data);
        uint count = reader.ReadUInt32();
        if (count > 100_000) throw new InvalidDataException($"Unreasonable binary record count: {count}.");
        ulong xuid = reader.ReadUInt64();
        var records = new List<BinaryCareerRecord>((int)count);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            int offset = reader.Offset;
            string name = reader.ReadUtf8(reader.ReadBoundedLength("binary record name", 16_384));
            string serializer = reader.ReadUtf8(reader.ReadBoundedLength("binary serializer", 16_384));
            uint tag = reader.ReadUInt32();
            uint expected = Fh6ProfileEditorDocument.ForzaNameHash(name + serializer);
            if (tag != expected) throw new InvalidDataException($"Binary record tag mismatch for {name}.");
            int size = reader.ReadBoundedLength("binary payload", reader.Remaining);
            int payloadOffset = reader.Offset;
            byte[] payload = reader.ReadBytes(size);
            uint storedOrdinal = reader.ReadUInt32();
            if (storedOrdinal != ordinal) throw new InvalidDataException($"Binary record ordinal mismatch at 0x{reader.Offset - 4:X}.");
            records.Add(new BinaryCareerRecord(name, serializer, tag, payload, ordinal, offset, payloadOffset));
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Binary career state has trailing bytes.");
        return new BinaryCareerState(xuid, records);
    }

    public byte[] Serialize()
    {
        using var output = new MemoryStream();
        Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)Records.Count));
        Fh6ProfileEditorDocument.WriteUInt64(output, Xuid);
        for (int ordinal = 0; ordinal < Records.Count; ordinal++)
        {
            var record = Records[ordinal];
            WriteNarrow(output, record.Name); WriteNarrow(output, record.Serializer);
            record.Tag = Fh6ProfileEditorDocument.ForzaNameHash(record.Name + record.Serializer);
            Fh6ProfileEditorDocument.WriteUInt32(output, record.Tag);
            Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)record.Payload.Length));
            output.Write(record.Payload);
            Fh6ProfileEditorDocument.WriteUInt32(output, checked((uint)ordinal));
        }
        return output.ToArray();
    }

    private static void WriteNarrow(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Fh6ProfileEditorDocument.WriteUInt32(stream, checked((uint)bytes.Length)); stream.Write(bytes);
    }
}

internal sealed class BinaryCareerRecord(string name, string serializer, uint tag, byte[] payload, int ordinal, int offset, int payloadOffset)
{
    public string Name { get; } = name;
    public string Serializer { get; } = serializer;
    public uint Tag { get; set; } = tag;
    public byte[] Payload { get; set; } = payload;
    public int Ordinal { get; } = ordinal;
    public int Offset { get; } = offset;
    public int PayloadOffset { get; } = payloadOffset;
}

internal sealed class SpanReader
{
    private readonly byte[] _data;
    public SpanReader(byte[] data) => _data = data;
    public int Offset { get; private set; }
    public int Remaining => _data.Length - Offset;
    public byte ReadByte() => ReadBytes(1)[0];
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(2));
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(ReadSpan(2));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(4));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(4));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadSpan(8));
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(8));
    public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
    public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());
    public uint ReadIndex(int width) => width switch { 1 => ReadByte(), 2 => ReadUInt16(), 4 => ReadUInt32(), _ => throw new ArgumentOutOfRangeException(nameof(width)) };
    public string ReadUtf8(int length) => Encoding.UTF8.GetString(ReadSpan(length));
    public string ReadUtf16(int characterCount) => Encoding.Unicode.GetString(ReadSpan(checked(characterCount * 2)));
    public int ReadBoundedLength(string label, int maximum)
    {
        uint length = ReadUInt32();
        if (length > maximum || length > int.MaxValue) throw new InvalidDataException($"Unreasonable {label} length at 0x{Offset - 4:X}.");
        return (int)length;
    }
    public byte[] ReadBytes(int count) => ReadSpan(count).ToArray();
    public byte[] ReadRemaining() => ReadBytes(Remaining);
    private ReadOnlySpan<byte> ReadSpan(int count)
    {
        if (count < 0 || count > Remaining) throw new InvalidDataException($"Unexpected end of ProfileData at 0x{Offset:X}.");
        var result = _data.AsSpan(Offset, count); Offset += count; return result;
    }
}

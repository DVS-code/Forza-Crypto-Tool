using System.Buffers.Binary;
using System.Text;

namespace ForzaCryptoTool;

internal static class LegacyZip
{
    private const uint SigLocalHeader = 0x04034B50;
    private const uint SigCentralHeader = 0x02014B50;
    private const uint SigEndOfCentralDir = 0x06054B50;
    private const ushort FlagDataDescriptor = 0x0008;

    internal sealed record Result(byte[] Archive, int Converted, int Unrecognised, int BadBlockMacs, LegacyFileInfo? First);

    private delegate (ushort Method, byte[] Payload)? Converter(Entry entry, ReadOnlySpan<byte> payload);

    private sealed class Entry
    {
        public required int CentralOffset;
        public required int CentralLength;
        public required string Name;
        public required ushort Method;
        public required uint Crc32;
        public required uint CompressedSize;
        public required uint UncompressedSize;
        public required uint LocalOffset;
        public int NameLength;
        public int ExtraLength;
        public byte[]? NewPayload;
        public ushort NewMethod;
        public uint NewLocalOffset;
        public uint NewDataOffset;
    }

    public static LegacyFileInfo? IdentifyFirstEntry(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var header = new byte[36];

        while (stream.Position + 30 <= stream.Length)
        {
            if (reader.ReadUInt32() != SigLocalHeader) return null;
            stream.Position += 4;
            ushort method = reader.ReadUInt16();
            stream.Position += 8;
            uint compressedSize = reader.ReadUInt32();
            stream.Position += 4;
            ushort nameLength = reader.ReadUInt16();
            ushort extraLength = reader.ReadUInt16();
            stream.Position += nameLength + extraLength;

            if (method == M22Archive.MethodM22 && compressedSize > 0)
            {
                int read = stream.Read(header, 0, (int)Math.Min(header.Length, compressedSize));
                return LegacyCrypto.Identify(header.AsSpan(0, read), compressedSize);
            }

            long next = stream.Position + compressedSize;
            if (next > stream.Length) return null;
            stream.Position = next;
        }
        return null;
    }

    public static Result Decrypt(byte[] archive)
    {
        int converted = 0, unrecognised = 0, badMacs = 0;
        LegacyFileInfo? first = null;

        var output = Transform(archive, ensureDataOffsetField: false, (entry, payload) =>
        {
            if (entry.Method != M22Archive.MethodM22 || payload.Length == 0) return null;

            var result = LegacyCrypto.TryDecrypt(payload);
            if (result is null) { unrecognised++; return null; }

            first ??= result.Info;
            badMacs += result.BadBlockMacs;
            converted++;
            return (M22Archive.MethodDeflate, result.Plaintext);
        });

        return new Result(output, converted, unrecognised, badMacs, first);
    }

    public static Result Encrypt(byte[] archive, LegacyContext context, Func<string, byte[]?>? ivForEntry = null)
    {
        int converted = 0;
        LegacyFileInfo? first = null;

        var output = Transform(archive, ensureDataOffsetField: true, (entry, payload) =>
        {
            if (entry.Method != M22Archive.MethodDeflate || payload.Length == 0) return null;

            var iv = ivForEntry?.Invoke(entry.Name) ?? LegacyCrypto.RandomIv();
            var encrypted = LegacyCrypto.Encrypt(payload, context, iv);
            first ??= LegacyCrypto.Identify(encrypted, encrypted.Length);
            converted++;
            return (M22Archive.MethodM22, encrypted);
        });

        return new Result(output, converted, 0, 0, first);
    }

    public static Dictionary<string, byte[]> ReadEntryIvs(byte[] archive)
    {
        var ivs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in ReadCentralDirectory(archive, out _, out _))
        {
            if (entry.Method != M22Archive.MethodM22 || entry.CompressedSize < 16) continue;
            int data = DataOffset(archive, entry);
            ivs[entry.Name] = archive.AsSpan(data, 16).ToArray();
        }
        return ivs;
    }

    private static byte[] Transform(byte[] archive, bool ensureDataOffsetField, Converter convert)
    {
        var entries = ReadCentralDirectory(archive, out _, out int eocdOffset);
        using var output = new MemoryStream(archive.Length + entries.Count * 64);
        Span<byte> localHeader = stackalloc byte[30];

        foreach (var entry in entries.OrderBy(e => e.LocalOffset))
        {
            int data = DataOffset(archive, entry);
            var payload = archive.AsSpan(data, checked((int)entry.CompressedSize));

            var converted = convert(entry, payload);
            entry.NewMethod = converted?.Method ?? entry.Method;
            entry.NewPayload = converted?.Payload;
            int newSize = entry.NewPayload?.Length ?? payload.Length;

            archive.AsSpan((int)entry.LocalOffset, 30).CopyTo(localHeader);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(localHeader[6..]);
            BinaryPrimitives.WriteUInt16LittleEndian(localHeader[6..], (ushort)(flags & ~FlagDataDescriptor));
            BinaryPrimitives.WriteUInt16LittleEndian(localHeader[8..], entry.NewMethod);
            BinaryPrimitives.WriteUInt32LittleEndian(localHeader[14..], entry.Crc32);
            BinaryPrimitives.WriteUInt32LittleEndian(localHeader[18..], (uint)newSize);
            BinaryPrimitives.WriteUInt32LittleEndian(localHeader[22..], entry.UncompressedSize);

            entry.NewLocalOffset = checked((uint)output.Position);
            output.Write(localHeader);
            output.Write(archive, (int)entry.LocalOffset + 30, entry.NameLength + entry.ExtraLength);
            entry.NewDataOffset = checked((uint)output.Position);
            if (entry.NewPayload is not null) output.Write(entry.NewPayload);
            else output.Write(payload);
        }

        uint newCentralStart = checked((uint)output.Position);
        foreach (var entry in entries)
            WriteCentralRecord(output, archive, entry, ensureDataOffsetField);
        uint newCentralSize = checked((uint)output.Position) - newCentralStart;

        var eocd = archive.AsSpan(eocdOffset).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), newCentralSize);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), newCentralStart);
        output.Write(eocd);

        return output.ToArray();
    }

    private static void WriteCentralRecord(MemoryStream output, byte[] archive, Entry entry, bool ensureDataOffsetField)
    {
        var record = archive.AsSpan(entry.CentralOffset, entry.CentralLength).ToArray();
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(28));
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(30));
        int extraStart = 46 + nameLength;

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(8));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), (ushort)(flags & ~FlagDataDescriptor));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), entry.NewMethod);
        if (entry.NewPayload is not null)
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(20), (uint)entry.NewPayload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(42), entry.NewLocalOffset);

        bool hasField = false;
        for (int offset = extraStart; offset + 4 <= extraStart + extraLength;)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset));
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 2));
            if (id == M22Archive.ForzaExtraHeaderId && size == 4 && offset + 8 <= extraStart + extraLength)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(offset + 4), entry.NewDataOffset);
                hasField = true;
            }
            offset += 4 + size;
        }

        if (hasField || !ensureDataOffsetField)
        {
            output.Write(record);
            return;
        }

        Span<byte> field = stackalloc byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(field, M22Archive.ForzaExtraHeaderId);
        BinaryPrimitives.WriteUInt16LittleEndian(field[2..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(field[4..], entry.NewDataOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(30), checked((ushort)(extraLength + field.Length)));
        output.Write(record, 0, extraStart + extraLength);
        output.Write(field);
        output.Write(record, extraStart + extraLength, record.Length - extraStart - extraLength);
    }

    private static List<Entry> ReadCentralDirectory(byte[] archive, out int centralStart, out int eocdOffset)
    {
        eocdOffset = -1;
        for (int i = archive.Length - 22; i >= Math.Max(0, archive.Length - 22 - ushort.MaxValue); i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(i)) != SigEndOfCentralDir) continue;
            eocdOffset = i;
            break;
        }
        if (eocdOffset < 0)
            throw new InvalidDataException("Not a ZIP archive (no end-of-central-directory record).");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocdOffset + 10));
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(eocdOffset + 16));
        if (count == ushort.MaxValue || start == uint.MaxValue)
            throw new NotSupportedException("ZIP64 archives are not supported.");
        if (start > eocdOffset)
            throw new InvalidDataException("Central directory offset is outside the archive.");
        centralStart = (int)start;

        var entries = new List<Entry>(count);
        int offset = centralStart;
        for (int i = 0; i < count; i++)
        {
            var record = archive.AsSpan(offset);
            if (record.Length < 46 || BinaryPrimitives.ReadUInt32LittleEndian(record) != SigCentralHeader)
                throw new InvalidDataException("Central directory is truncated or corrupt.");

            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[28..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(record[30..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[32..]);
            int length = 46 + nameLength + extraLength + commentLength;

            entries.Add(new Entry
            {
                CentralOffset = offset,
                CentralLength = length,
                Name = Encoding.UTF8.GetString(record.Slice(46, nameLength)),
                Method = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]),
                Crc32 = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]),
                CompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]),
                UncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[24..]),
                LocalOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[42..]),
            });
            offset += length;
        }
        return entries;
    }

    private static int DataOffset(byte[] archive, Entry entry)
    {
        var header = archive.AsSpan(checked((int)entry.LocalOffset));
        if (header.Length < 30 || BinaryPrimitives.ReadUInt32LittleEndian(header) != SigLocalHeader)
            throw new InvalidDataException($"No local file header for '{entry.Name}'.");
        entry.NameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        entry.ExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        int data = (int)entry.LocalOffset + 30 + entry.NameLength + entry.ExtraLength;
        if ((long)data + entry.CompressedSize > archive.Length)
            throw new InvalidDataException($"Entry '{entry.Name}' runs past the end of the archive.");
        return data;
    }
}

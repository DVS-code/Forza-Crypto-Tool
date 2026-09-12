using System.Buffers.Binary;
using System.Text;

namespace ForzaCryptoTool;

internal enum Decryptability
{
    NotEncrypted,

    SingleChunk,

    MultiChunk,

    Unknown,
}

internal sealed record M22EntryInfo(
    string Name,
    ushort Method,
    uint CompressedSize,
    uint UncompressedSize,
    uint Crc32,
    long LocalHeaderOffset,
    int ChunkCount,
    Decryptability Decryptability,
    uint? AlignHint)
{
    public bool IsEncrypted => Method == M22Archive.MethodM22;
}

internal static class M22Archive
{
    public const ushort MethodStore = 0;
    public const ushort MethodDeflate = 8;
    public const ushort MethodM22 = 22;

    public const int EntryHeaderBytes = 0x24;

    public const int PageDataBytes = 0x200;

    public const int PageStrideBytes = 0x210;

    public const ushort ForzaExtraHeaderId = 0x1123;

    private const uint SigLocalHeader = 0x04034B50;
    private const uint SigCentralHeader = 0x02014B50;
    private const uint SigEndOfCentralDir = 0x06054B50;
    private const int EocdMinBytes = 22;

    public static int ChunkCount(uint compressedSize)
    {
        long body = (long)compressedSize - EntryHeaderBytes;
        if (body <= 0) return 0;
        long full = Math.DivRem(body, PageStrideBytes, out long remainder);

        if (remainder != 0 && remainder % 16 != 0) return 0;
        return (int)(full + (remainder != 0 ? 1 : 0));
    }

    public static Decryptability Classify(ushort method, uint compressedSize)
    {
        if (method is MethodStore or MethodDeflate) return Decryptability.NotEncrypted;
        if (method != MethodM22) return Decryptability.Unknown;
        return ChunkCount(compressedSize) switch
        {
            0 => Decryptability.Unknown,
            1 => Decryptability.SingleChunk,
            _ => Decryptability.MultiChunk,
        };
    }

    public static string? DescribeBlocker(M22EntryInfo entry, bool ivsAvailable)
    {
        switch (entry.Decryptability)
        {
            case Decryptability.NotEncrypted:
            case Decryptability.SingleChunk:
                return null;
            case Decryptability.MultiChunk when ivsAvailable:
                return null;
            case Decryptability.MultiChunk:
                return $"{entry.ChunkCount} chunks. Only the first chunk's IV is stored in the file; "
                     + "the rest are cipher-internal state that cannot be derived from the file's bytes. "
                     + "This entry needs its IVs in the loaded IV table before it can be decrypted.";
            default:
                return $"Compression method {entry.Method} with an unrecognized payload size "
                     + $"({entry.CompressedSize:N0} bytes). Shown as raw bytes only.";
        }
    }

    public static IReadOnlyList<M22EntryInfo> ReadCentralDirectory(Stream zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        if (!zip.CanSeek) throw new ArgumentException("A seekable stream is required.", nameof(zip));

        var (centralOffset, entryCount) = LocateCentralDirectory(zip);
        var entries = new List<M22EntryInfo>(entryCount);

        zip.Position = centralOffset;
        using var reader = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);

        for (int i = 0; i < entryCount; i++)
        {
            uint signature = reader.ReadUInt32();
            if (signature != SigCentralHeader) break;

            reader.BaseStream.Position += 4;
            reader.ReadUInt16();
            ushort method = reader.ReadUInt16();
            reader.BaseStream.Position += 4;
            uint crc = reader.ReadUInt32();
            uint compressedSize = reader.ReadUInt32();
            uint uncompressedSize = reader.ReadUInt32();
            ushort nameLength = reader.ReadUInt16();
            ushort extraLength = reader.ReadUInt16();
            ushort commentLength = reader.ReadUInt16();
            reader.BaseStream.Position += 8;
            uint localHeaderOffset = reader.ReadUInt32();

            string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
            byte[] extra = reader.ReadBytes(extraLength);
            reader.BaseStream.Position += commentLength;

            entries.Add(new M22EntryInfo(
                Name: name,
                Method: method,
                CompressedSize: compressedSize,
                UncompressedSize: uncompressedSize,
                Crc32: crc,
                LocalHeaderOffset: localHeaderOffset,
                ChunkCount: method == MethodM22 ? ChunkCount(compressedSize) : 1,
                Decryptability: Classify(method, compressedSize),
                AlignHint: ReadForzaAlignHint(extra)));
        }

        return entries;
    }

    public static long ResolveDataOffset(Stream zip, M22EntryInfo entry)
    {
        zip.Position = entry.LocalHeaderOffset;
        Span<byte> header = stackalloc byte[30];
        ReadExactly(zip, header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != SigLocalHeader)
            throw new InvalidDataException($"No local file header for '{entry.Name}'.");
        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        return entry.LocalHeaderOffset + 30 + nameLength + extraLength;
    }

    public static byte[] ReadEntryBytes(Stream zip, M22EntryInfo entry)
    {
        long offset = ResolveDataOffset(zip, entry);
        zip.Position = offset;
        var buffer = new byte[entry.CompressedSize];
        ReadExactly(zip, buffer);
        return buffer;
    }

    private static uint? ReadForzaAlignHint(ReadOnlySpan<byte> extra)
    {
        int offset = 0;
        while (offset + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[offset..]);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(offset + 2)..]);
            int payload = offset + 4;
            if (payload + size > extra.Length) break;
            if (id == ForzaExtraHeaderId && size >= 4)
                return BinaryPrimitives.ReadUInt32LittleEndian(extra[payload..]);
            offset = payload + size;
        }
        return null;
    }

    private static (long Offset, int Count) LocateCentralDirectory(Stream zip)
    {
        int windowSize = (int)Math.Min(zip.Length, 64 * 1024);
        var window = new byte[windowSize];
        zip.Position = zip.Length - windowSize;
        ReadExactly(zip, window);

        for (int i = windowSize - EocdMinBytes; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(i)) != SigEndOfCentralDir)
                continue;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(window.AsSpan(i + 10));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(i + 16));
            if (offset < 0 || offset >= zip.Length)
                throw new InvalidDataException("Central directory offset is outside the archive.");
            return (offset, count);
        }

        throw new InvalidDataException("Not a ZIP archive (no end-of-central-directory record).");
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer[read..]);
            if (n == 0) throw new EndOfStreamException("Unexpected end of archive.");
            read += n;
        }
    }
}

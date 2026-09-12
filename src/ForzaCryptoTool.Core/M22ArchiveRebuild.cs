using System.Buffers.Binary;

namespace ForzaCryptoTool;

internal static partial class M22ArchiveRebuild
{
    private const uint SigLocalHeader = 0x04034B50;
    private const uint SigCentralHeader = 0x02014B50;
    private const uint SigEndOfCentralDir = 0x06054B50;

    internal sealed record EntryReplacement(byte[] RawBytes, ushort Method, uint UncompressedSize, uint Crc32);

    public static byte[] Rebuild(
        Stream source,
        IReadOnlyDictionary<string, EntryReplacement>? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        replacements ??= new Dictionary<string, EntryReplacement>(0);

        var entries = M22Archive.ReadCentralDirectory(source);
        using var output = new MemoryStream(checked((int)source.Length));

        var order = entries
            .Select((entry, index) => (entry, index))
            .OrderBy(pair => pair.entry.LocalHeaderOffset)
            .ToArray();

        var newOffsets = new long[entries.Count];
        long expectedNext = -1;

        foreach (var (entry, index) in order)
        {
            if (expectedNext >= 0 && entry.LocalHeaderOffset > expectedNext)
            {
                source.Position = expectedNext;
                CopyBytes(source, output, entry.LocalHeaderOffset - expectedNext);
            }

            newOffsets[index] = output.Position;
            replacements.TryGetValue(entry.Name, out var replacement);
            expectedNext = CopyLocalRecord(source, output, entry, replacement);
        }

        long centralDirectoryStart = FindCentralDirectoryStart(source);
        if (expectedNext >= 0 && centralDirectoryStart > expectedNext)
        {
            source.Position = expectedNext;
            CopyBytes(source, output, centralDirectoryStart - expectedNext);
        }

        long centralStart = output.Position;
        for (int i = 0; i < entries.Count; i++)
        {
            replacements.TryGetValue(entries[i].Name, out var replacement);
            CopyCentralRecord(source, output, entries[i], newOffsets[i], replacement);
        }
        long centralSize = output.Position - centralStart;

        WriteEndOfCentralDirectory(source, output, entries.Count, centralStart, centralSize);
        return output.ToArray();
    }

    private static long CopyLocalRecord(
        Stream source, Stream output, M22EntryInfo entry, EntryReplacement? replacement)
    {
        source.Position = entry.LocalHeaderOffset;
        var header = new byte[30];
        ReadExactly(source, header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != SigLocalHeader)
            throw new InvalidDataException($"No local file header for '{entry.Name}'.");

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        var nameAndExtra = new byte[nameLength + extraLength];
        ReadExactly(source, nameAndExtra);

        long dataOffset = source.Position;

        if (replacement is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), replacement.Method);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14), replacement.Crc32);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)replacement.RawBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), replacement.UncompressedSize);
        }

        output.Write(header);
        output.Write(nameAndExtra);

        if (replacement is not null)
        {
            output.Write(replacement.RawBytes);
        }
        else
        {
            source.Position = dataOffset;
            CopyBytes(source, output, entry.CompressedSize);
        }

        return dataOffset + entry.CompressedSize;
    }

    private static void CopyCentralRecord(
        Stream source, Stream output, M22EntryInfo entry, long newLocalOffset, EntryReplacement? replacement)
    {
        long recordOffset = FindCentralRecord(source, entry);
        source.Position = recordOffset;

        var header = new byte[46];
        ReadExactly(source, header);

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
        ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
        var tail = new byte[nameLength + extraLength + commentLength];
        ReadExactly(source, tail);

        if (replacement is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), replacement.Method);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), replacement.Crc32);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)replacement.RawBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), replacement.UncompressedSize);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(42), checked((uint)newLocalOffset));

        output.Write(header);
        output.Write(tail);
    }

    private static void WriteEndOfCentralDirectory(
        Stream source, Stream output, int entryCount, long centralStart, long centralSize)
    {
        long eocdOffset = FindEndOfCentralDirectory(source);
        source.Position = eocdOffset;
        var eocd = new byte[22];
        ReadExactly(source, eocd);
        ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(eocd.AsSpan(20));
        var comment = new byte[commentLength];
        if (commentLength > 0) ReadExactly(source, comment);

        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), checked((ushort)entryCount));
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), checked((ushort)entryCount));
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), checked((uint)centralSize));
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), checked((uint)centralStart));

        output.Write(eocd);
        if (commentLength > 0) output.Write(comment);
    }

    private static long FindCentralRecord(Stream source, M22EntryInfo entry)
    {
        long offset = FindCentralDirectoryStart(source);
        while (true)
        {
            source.Position = offset;
            var header = new byte[46];
            ReadExactly(source, header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != SigCentralHeader)
                throw new InvalidDataException($"Central-directory record for '{entry.Name}' not found.");

            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(42));

            if (localOffset == entry.LocalHeaderOffset)
            {
                var name = new byte[nameLength];
                ReadExactly(source, name);
                if (System.Text.Encoding.UTF8.GetString(name) == entry.Name)
                    return offset;
            }
            offset += 46 + nameLength + extraLength + commentLength;
        }
    }

    private static long FindCentralDirectoryStart(Stream source)
    {
        long eocd = FindEndOfCentralDirectory(source);
        source.Position = eocd + 16;
        var raw = new byte[4];
        ReadExactly(source, raw);
        return BinaryPrimitives.ReadUInt32LittleEndian(raw);
    }

    private static long FindEndOfCentralDirectory(Stream source)
    {
        int windowSize = (int)Math.Min(source.Length, 64 * 1024);
        var window = new byte[windowSize];
        source.Position = source.Length - windowSize;
        ReadExactly(source, window);
        for (int i = windowSize - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(i)) == SigEndOfCentralDir)
                return source.Length - windowSize + i;
        }
        throw new InvalidDataException("Not a ZIP archive (no end-of-central-directory record).");
    }

    private static void CopyBytes(Stream source, Stream destination, long count)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            int want = (int)Math.Min(buffer.Length, count);
            int read = source.Read(buffer, 0, want);
            if (read == 0) throw new EndOfStreamException("Unexpected end of archive while copying entry data.");
            destination.Write(buffer, 0, read);
            count -= read;
        }
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

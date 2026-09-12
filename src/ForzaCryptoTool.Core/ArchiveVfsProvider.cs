using System.IO.Compression;

namespace ForzaCryptoTool;

internal sealed class ArchiveVfsProvider(
    ArchiveDirectoryCache cache,
    IM22EntryDecryptor decryptor) : IVfsProvider
{
    private readonly ArchiveDirectoryCache _cache = cache;
    private readonly IM22EntryDecryptor _decryptor = decryptor;

    public bool CanList(string vfsPath)
    {
        var (container, _) = AssetVfs.SplitPath(vfsPath);
        if (!File.Exists(container)) return false;
        string extension = Path.GetExtension(container);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".minizip", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<VfsNode> List(string vfsPath)
    {
        var (container, _) = AssetVfs.SplitPath(vfsPath);
        IReadOnlyList<M22EntryInfo> entries;
        try { entries = _cache.Get(container); }
        catch (InvalidDataException) { return []; }

        var nodes = new List<VfsNode>(entries.Count);
        foreach (var entry in entries)
        {
            bool ivsAvailable = entry.Decryptability == Decryptability.MultiChunk
                                && _decryptor.HasIvsFor(container, entry);
            string? blocker = M22Archive.DescribeBlocker(entry, ivsAvailable);

            nodes.Add(new VfsNode(
                VfsPath: AssetVfs.Combine(container, entry.Name),
                Name: entry.Name,
                Kind: VfsNodeKind.ArchiveEntry,
                Size: entry.UncompressedSize,
                PhysicalSize: entry.CompressedSize,
                Decryptability: entry.Decryptability,
                CanOpen: blocker is null,
                Blocker: blocker));
        }
        return nodes;
    }

    public ArchiveSummary Summarize(string containerPath)
    {
        IReadOnlyList<M22EntryInfo> entries;
        try { entries = _cache.Get(containerPath); }
        catch (InvalidDataException) { return new ArchiveSummary(0, 0, 0); }

        int openable = entries.Count(e =>
            M22Archive.DescribeBlocker(
                e,
                e.Decryptability == Decryptability.MultiChunk && _decryptor.HasIvsFor(containerPath, e)) is null);
        return new ArchiveSummary(entries.Count, openable, entries.Count - openable);
    }

    public async Task<VfsContent> ReadAsync(string vfsPath, CancellationToken ct)
    {
        var (container, entryName) = AssetVfs.SplitPath(vfsPath);
        if (entryName is null) throw new ArgumentException("Not an archive entry path.", nameof(vfsPath));

        var entry = _cache.Get(container).FirstOrDefault(e => e.Name == entryName)
            ?? throw new FileNotFoundException($"'{entryName}' is not in {Path.GetFileName(container)}.");

        switch (entry.Decryptability)
        {
            case Decryptability.NotEncrypted:
                return new VfsContent(ReadPlainEntry(container, entry),
                    entry.Method == M22Archive.MethodStore
                        ? "Stored uncompressed — read locally, no decryption needed."
                        : "Deflate compressed — decompressed locally, no decryption needed.");

            case Decryptability.SingleChunk:
            case Decryptability.MultiChunk:
            {
                bool ivsAvailable = entry.Decryptability == Decryptability.SingleChunk
                                    || _decryptor.HasIvsFor(container, entry);
                string? blocker = M22Archive.DescribeBlocker(entry, ivsAvailable);
                if (blocker is not null) throw new VfsEntryLockedException(blocker);

                byte[] payload;
                using (var stream = File.OpenRead(container))
                    payload = M22Archive.ReadEntryBytes(stream, entry);

                byte[] plaintext = await _decryptor.DecryptEntryAsync(container, entry, payload, ct);
                return new VfsContent(plaintext,
                    $"Method 22, {entry.ChunkCount} chunk{(entry.ChunkCount == 1 ? "" : "s")} — "
                    + $"decrypted on demand ({payload.Length:N0} bytes sent).");
            }

            default:
                throw new VfsEntryLockedException(
                    M22Archive.DescribeBlocker(entry, false) ?? "This entry cannot be opened.");
        }
    }

    private static byte[] ReadPlainEntry(string container, M22EntryInfo entry)
    {
        using var archive = ZipFile.OpenRead(container);
        var zipEntry = archive.GetEntry(entry.Name)
            ?? throw new FileNotFoundException($"'{entry.Name}' vanished from the archive.");
        using var stream = zipEntry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

internal interface IM22EntryDecryptor
{
    bool HasIvsFor(string containerPath, M22EntryInfo entry);

    Task<byte[]> DecryptEntryAsync(string containerPath, M22EntryInfo entry, byte[] payload, CancellationToken ct);
}

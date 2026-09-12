namespace ForzaCryptoTool;

internal sealed class FileSystemVfsProvider : IVfsProvider
{
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    public bool CanList(string vfsPath)
    {
        if (vfsPath.Contains(AssetVfs.EntrySeparator, StringComparison.Ordinal)) return false;
        if (Directory.Exists(vfsPath)) return true;
        return File.Exists(vfsPath) && !LooksLikeArchive(vfsPath);
    }

    public IReadOnlyList<VfsNode> List(string vfsPath)
    {
        if (!Directory.Exists(vfsPath)) return [];

        var nodes = new List<VfsNode>();

        foreach (var dir in SafeEnumerate(vfsPath, directories: true))
        {
            nodes.Add(new VfsNode(
                VfsPath: dir,
                Name: Path.GetFileName(dir),
                Kind: VfsNodeKind.Directory,
                Size: 0,
                PhysicalSize: 0,
                Decryptability: Decryptability.NotEncrypted,
                CanOpen: false,
                Blocker: null));
        }

        foreach (var file in SafeEnumerate(vfsPath, directories: false))
        {
            long length;
            try { length = new FileInfo(file).Length; }
            catch { continue; }

            bool isArchive = LooksLikeArchive(file);
            nodes.Add(new VfsNode(
                VfsPath: file,
                Name: Path.GetFileName(file),
                Kind: isArchive ? VfsNodeKind.ArchiveRoot : VfsNodeKind.File,
                Size: length,
                PhysicalSize: length,
                Decryptability: Decryptability.NotEncrypted,
                CanOpen: !isArchive,
                Blocker: null));
        }

        return nodes;
    }

    public async Task<VfsContent> ReadAsync(string vfsPath, CancellationToken ct)
    {
        byte[] bytes = await File.ReadAllBytesAsync(vfsPath, ct);
        return new VfsContent(bytes, "Read from disk.");
    }

    private static bool LooksLikeArchive(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".minizip", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == 4 && magic.SequenceEqual(ZipMagic);
        }
        catch { return false; }
    }

    private static IEnumerable<string> SafeEnumerate(string path, bool directories)
    {
        try
        {
            var items = directories
                ? Directory.EnumerateDirectories(path)
                : Directory.EnumerateFiles(path);
            return items.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
    }
}

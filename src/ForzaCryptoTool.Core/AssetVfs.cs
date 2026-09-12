using System.Collections.Concurrent;

namespace ForzaCryptoTool;

internal enum VfsNodeKind
{
    Directory,
    File,
    ArchiveRoot,
    ArchiveEntry,
}

internal sealed record VfsNode(
    string VfsPath,
    string Name,
    VfsNodeKind Kind,
    long Size,
    long PhysicalSize,
    Decryptability Decryptability,
    bool CanOpen,
    string? Blocker)
{
    public bool IsContainer => Kind is VfsNodeKind.Directory or VfsNodeKind.ArchiveRoot;

    public string SizeLabel => Size switch
    {
        >= 1024L * 1024 * 1024 => $"{Size / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024 * 1024 => $"{Size / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{Size / 1024.0:0.0} KB",
        _ => $"{Size} B",
    };
}

internal sealed record VfsContent(byte[] Bytes, string SourceDescription);

internal sealed record ArchiveSummary(int Total, int Openable, int Locked)
{
    public string Label => Locked == 0
        ? $"{Total} entries"
        : $"{Total} entries · {Openable} openable · {Locked} locked";
}

internal interface IVfsProvider
{
    bool CanList(string vfsPath);
    IReadOnlyList<VfsNode> List(string vfsPath);
    Task<VfsContent> ReadAsync(string vfsPath, CancellationToken ct);
}

internal sealed class VfsEntryLockedException(string message) : Exception(message);

internal sealed class AssetVfs(IReadOnlyList<IVfsProvider> providers)
{
    public const string EntrySeparator = "!/";

    private readonly IReadOnlyList<IVfsProvider> _providers = providers;

    public static (string Container, string? Entry) SplitPath(string vfsPath)
    {
        int index = vfsPath.IndexOf(EntrySeparator, StringComparison.Ordinal);
        return index < 0
            ? (vfsPath, null)
            : (vfsPath[..index], vfsPath[(index + EntrySeparator.Length)..]);
    }

    public static string Combine(string container, string entry) => container + EntrySeparator + entry;

    public IReadOnlyList<VfsNode> List(string vfsPath)
    {
        foreach (var provider in _providers)
        {
            if (provider.CanList(vfsPath)) return provider.List(vfsPath);
        }
        return [];
    }

    public Task<VfsContent> ReadAsync(string vfsPath, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (provider.CanList(vfsPath)) return provider.ReadAsync(vfsPath, ct);
        }
        throw new FileNotFoundException($"Nothing can read '{vfsPath}'.");
    }
}

internal sealed class ArchiveDirectoryCache
{
    private readonly record struct Key(string Path, long Ticks, long Length);

    private readonly ConcurrentDictionary<Key, IReadOnlyList<M22EntryInfo>> _entries = new();

    public IReadOnlyList<M22EntryInfo> Get(string path)
    {
        var info = new FileInfo(path);
        var key = new Key(path, info.LastWriteTimeUtc.Ticks, info.Length);
        if (_entries.TryGetValue(key, out var cached)) return cached;

        using var stream = File.OpenRead(path);
        var entries = M22Archive.ReadCentralDirectory(stream);

        if (_entries.Count > 256) _entries.Clear();
        _entries[key] = entries;
        return entries;
    }

    public void Clear() => _entries.Clear();
}

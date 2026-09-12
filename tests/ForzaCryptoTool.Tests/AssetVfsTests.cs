using System.IO.Compression;

namespace ForzaCryptoTool.Tests;

public sealed class AssetVfsTests
{
    private const string InstallRoot = @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon6";
    private static string MediaRoot => Path.Combine(InstallRoot, "media");
    private static bool InstallPresent => Directory.Exists(MediaRoot);

    private sealed class NeverCalledDecryptor : IM22EntryDecryptor
    {
        public bool HasIvsFor(string containerPath, M22EntryInfo entry) => false;

        public Task<byte[]> DecryptEntryAsync(
            string containerPath, M22EntryInfo entry, byte[] payload, CancellationToken ct)
            => throw new InvalidOperationException("The decryptor must not be called on this path.");
    }

    [Fact]
    public void SplitPath_separates_container_from_entry()
    {
        var (container, entry) = AssetVfs.SplitPath(@"C:\a\Camera.zip!/Autovista.xml");
        Assert.Equal(@"C:\a\Camera.zip", container);
        Assert.Equal("Autovista.xml", entry);

        var (plain, none) = AssetVfs.SplitPath(@"C:\a\file.xml");
        Assert.Equal(@"C:\a\file.xml", plain);
        Assert.Null(none);
    }

    [Fact]
    public void Combine_round_trips_through_SplitPath()
    {
        string combined = AssetVfs.Combine(@"C:\a\b.zip", "dir/file.xml");
        var (container, entry) = AssetVfs.SplitPath(combined);
        Assert.Equal(@"C:\a\b.zip", container);
        Assert.Equal("dir/file.xml", entry);
    }

    [SkippableFact]
    public void Listing_an_archive_never_invokes_the_decryptor()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");

        string? archive = FindArchive(e => e.Method == M22Archive.MethodM22);
        Skip.If(archive is null, "No method-22 archive found.");

        var provider = new ArchiveVfsProvider(new ArchiveDirectoryCache(), new NeverCalledDecryptor());

        var nodes = provider.List(archive!);
        Assert.NotEmpty(nodes);
        Assert.All(nodes, n => Assert.Equal(VfsNodeKind.ArchiveEntry, n.Kind));
    }

    [SkippableFact]
    public void Multi_chunk_entries_without_ivs_are_locked_and_explain_why()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        string? archive = FindArchive(e => e.Method == M22Archive.MethodM22
                                           && e.Decryptability == Decryptability.MultiChunk);
        Skip.If(archive is null, "No multi-chunk m22 archive found.");

        var provider = new ArchiveVfsProvider(new ArchiveDirectoryCache(), new NeverCalledDecryptor());
        var locked = provider.List(archive!).First(n => n.Decryptability == Decryptability.MultiChunk);

        Assert.False(locked.CanOpen);
        Assert.NotNull(locked.Blocker);
        Assert.Contains("chunks", locked.Blocker);

        var vfs = new AssetVfs([new FileSystemVfsProvider(), provider]);
        Assert.ThrowsAsync<VfsEntryLockedException>(() => vfs.ReadAsync(locked.VfsPath)).Wait();
    }

    [SkippableFact]
    public async Task Plain_entries_open_with_no_decryptor_involvement()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        string? archive = FindArchive(e => e.Method == M22Archive.MethodDeflate, allPlain: true);
        Skip.If(archive is null, "No fully-plain archive found.");

        var provider = new ArchiveVfsProvider(new ArchiveDirectoryCache(), new NeverCalledDecryptor());
        var nodes = provider.List(archive!);
        var entry = nodes.First(n => n.Size > 0);

        Assert.True(entry.CanOpen);
        Assert.Null(entry.Blocker);

        var content = await provider.ReadAsync(entry.VfsPath, CancellationToken.None);
        Assert.Equal(entry.Size, content.Bytes.Length);

        using var zip = ZipFile.OpenRead(archive!);
        using var stream = zip.GetEntry(entry.Name)!.Open();
        using var expected = new MemoryStream();
        stream.CopyTo(expected);
        Assert.Equal(expected.ToArray(), content.Bytes);
    }

    [SkippableFact]
    public void Listing_an_archive_through_the_vfs_reaches_the_archive_provider()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        string? archive = FindArchive(e => e.Method == M22Archive.MethodM22);
        Skip.If(archive is null, "No method-22 archive found.");

        var vfs = new AssetVfs([
            new FileSystemVfsProvider(),
            new ArchiveVfsProvider(new ArchiveDirectoryCache(), new NeverCalledDecryptor()),
        ]);

        var entries = vfs.List(archive!);
        Assert.NotEmpty(entries);
        Assert.All(entries, n => Assert.Equal(VfsNodeKind.ArchiveEntry, n.Kind));
    }

    [SkippableFact]
    public void The_filesystem_provider_does_not_claim_archives()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        string? archive = FindArchive(_ => true);
        Skip.If(archive is null, "No archive found.");

        var provider = new FileSystemVfsProvider();
        Assert.False(provider.CanList(archive!));
        Assert.True(provider.CanList(Path.GetDirectoryName(archive!)!));
    }

    [SkippableFact]
    public void Archive_summary_counts_openable_and_locked()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        string? archive = FindArchive(e => e.Method == M22Archive.MethodM22);
        Skip.If(archive is null, "No method-22 archive found.");

        var provider = new ArchiveVfsProvider(new ArchiveDirectoryCache(), new NeverCalledDecryptor());
        var summary = provider.Summarize(archive!);

        Assert.True(summary.Total > 0);
        Assert.Equal(summary.Total, summary.Openable + summary.Locked);
        Assert.Contains("entries", summary.Label);
    }

    [SkippableFact]
    public void Directory_listing_marks_zips_as_archives_without_scanning_them()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        var provider = new FileSystemVfsProvider();
        var nodes = provider.List(MediaRoot);

        Assert.NotEmpty(nodes);
        var zips = nodes.Where(n => n.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.All(zips, z => Assert.Equal(VfsNodeKind.ArchiveRoot, z.Kind));
        Assert.Contains(nodes, n => n.Kind == VfsNodeKind.Directory);
    }

    [Fact]
    public void Fingerprint_is_stable_and_needs_no_key()
    {
        var payload = new byte[M22Archive.EntryHeaderBytes + M22Archive.PageStrideBytes];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7);

        string a = IvTableStore.Fingerprint(payload);
        string b = IvTableStore.Fingerprint(payload);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);

        payload[M22Archive.EntryHeaderBytes] ^= 0xFF;
        Assert.NotEqual(a, IvTableStore.Fingerprint(payload));
    }

    [Fact]
    public void Missing_iv_table_loads_as_empty_rather_than_failing()
    {
        var store = IvTableStore.Load(Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid() + ".json"));
        Assert.Equal(0, store.Count);
        Assert.Null(store.LoadedFrom);
    }

    private static string? FindArchive(Func<M22EntryInfo, bool> predicate, bool allPlain = false)
    {
        foreach (var path in Directory.EnumerateFiles(MediaRoot, "*.zip", SearchOption.AllDirectories))
        {
            long length;
            try { length = new FileInfo(path).Length; } catch { continue; }
            if (length > 8L * 1024 * 1024) continue;

            IReadOnlyList<M22EntryInfo> entries;
            try
            {
                using var stream = File.OpenRead(path);
                entries = M22Archive.ReadCentralDirectory(stream);
            }
            catch { continue; }

            if (entries.Count == 0) continue;
            if (allPlain && entries.Any(e => e.Method == M22Archive.MethodM22)) continue;
            if (entries.Any(predicate)) return path;
        }
        return null;
    }
}

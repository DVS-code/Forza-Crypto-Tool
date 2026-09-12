using System.IO.Compression;

namespace ForzaCryptoTool.Tests;

public sealed class M22ArchiveTests
{
    private const string InstallRoot = @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon6";
    private static string MediaRoot => Path.Combine(InstallRoot, "media");

    private static bool InstallPresent => Directory.Exists(MediaRoot);

    private static IEnumerable<string> Archives(int limit, long maxBytes = 100L * 1024 * 1024)
    {
        if (!InstallPresent) yield break;
        int taken = 0;
        foreach (var path in Directory.EnumerateFiles(MediaRoot, "*.zip", SearchOption.AllDirectories))
        {
            if (taken >= limit) yield break;
            long length;
            try { length = new FileInfo(path).Length; }
            catch { continue; }
            if (length > maxBytes) continue;
            taken++;
            yield return path;
        }
    }

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(0x24u, 0)]
    [InlineData(0x24u + 0x210u, 1)]
    [InlineData(0x24u + 0x200u, 1)]
    [InlineData(0x24u + 0x210u * 2, 2)]
    [InlineData(0x24u + 0x210u * 41 + 0x200u, 42)]
    public void ChunkCount_matches_the_page_layout(uint compressedSize, int expected)
        => Assert.Equal(expected, M22Archive.ChunkCount(compressedSize));

    [Fact]
    public void ChunkCount_rejects_sizes_that_are_not_whole_aes_blocks()
    {
        Assert.Equal(0, M22Archive.ChunkCount(0x24u + 0x210u + 5));
    }

    [Fact]
    public void Classify_treats_store_and_deflate_as_plaintext()
    {
        Assert.Equal(Decryptability.NotEncrypted, M22Archive.Classify(M22Archive.MethodStore, 1234));
        Assert.Equal(Decryptability.NotEncrypted, M22Archive.Classify(M22Archive.MethodDeflate, 1234));
    }

    [Fact]
    public void Classify_separates_single_from_multi_chunk()
    {
        Assert.Equal(Decryptability.SingleChunk,
            M22Archive.Classify(M22Archive.MethodM22, 0x24 + 0x210));
        Assert.Equal(Decryptability.MultiChunk,
            M22Archive.Classify(M22Archive.MethodM22, 0x24 + 0x210 * 3));
    }

    [Fact]
    public void Single_chunk_entries_report_no_blocker_and_multi_chunk_explain_themselves()
    {
        var single = new M22EntryInfo("a.xml", 22, 0x24 + 0x210, 100, 0, 0, 1, Decryptability.SingleChunk, null);
        Assert.Null(M22Archive.DescribeBlocker(single, ivsAvailable: false));

        var multi = new M22EntryInfo("b.xml", 22, 0x24 + 0x210 * 4, 100, 0, 0, 4, Decryptability.MultiChunk, null);
        Assert.Null(M22Archive.DescribeBlocker(multi, ivsAvailable: true));

        string? blocked = M22Archive.DescribeBlocker(multi, ivsAvailable: false);
        Assert.NotNull(blocked);
        Assert.Contains("4 chunks", blocked);
    }

    [SkippableFact]
    public void Central_directory_parse_agrees_with_BCL_on_real_archives()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        int checkedArchives = 0;

        foreach (var path in Archives(limit: 40))
        {
            List<(string Name, long Size, uint Crc)> expected;
            try
            {
                using var zip = ZipFile.OpenRead(path);
                expected = zip.Entries
                    .Select(e => (e.FullName, e.CompressedLength, e.Crc32))
                    .ToList();
            }
            catch { continue; }

            using var stream = File.OpenRead(path);
            var actual = M22Archive.ReadCentralDirectory(stream);

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Name, actual[i].Name);
                Assert.Equal(expected[i].Size, actual[i].CompressedSize);
                Assert.Equal(expected[i].Crc, actual[i].Crc32);
            }
            checkedArchives++;
        }

        Assert.True(checkedArchives > 0, "No archives were compared.");
    }

    [SkippableFact]
    public void Every_m22_entry_in_the_install_has_a_legal_page_layout()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        int m22Entries = 0;
        var offenders = new List<string>();

        foreach (var path in Archives(limit: 500))
        {
            using var stream = File.OpenRead(path);
            IReadOnlyList<M22EntryInfo> entries;
            try { entries = M22Archive.ReadCentralDirectory(stream); }
            catch { continue; }

            foreach (var entry in entries.Where(e => e.Method == M22Archive.MethodM22))
            {
                m22Entries++;
                if (entry.ChunkCount == 0)
                    offenders.Add($"{Path.GetFileName(path)}!{entry.Name} csize={entry.CompressedSize}");
            }
        }

        Assert.True(m22Entries > 0, "No method-22 entries were found to classify.");
        Assert.Empty(offenders);
    }

    [SkippableFact]
    public void Rebuild_with_no_replacements_is_byte_identical()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");
        int rebuilt = 0;
        var failures = new List<string>();

        foreach (var path in Archives(limit: int.MaxValue, maxBytes: 32L * 1024 * 1024))
        {
            byte[] original;
            byte[] roundTripped;
            try
            {
                original = File.ReadAllBytes(path);
                using var stream = new MemoryStream(original, writable: false);
                roundTripped = M22ArchiveRebuild.Rebuild(stream);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}");
                continue;
            }

            if (!original.AsSpan().SequenceEqual(roundTripped))
            {
                failures.Add($"{Path.GetFileName(path)}: {original.Length:N0} -> {roundTripped.Length:N0} bytes, differs");
            }
            rebuilt++;
        }

        Assert.True(rebuilt > 0, "No archives were rebuilt.");
        Assert.Empty(failures);
    }

    [SkippableFact]
    public void Rebuild_substitutes_one_entry_and_leaves_the_others_untouched()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");

        string? target = null;
        foreach (var path in Archives(limit: 400, maxBytes: 8L * 1024 * 1024))
        {
            using var stream = File.OpenRead(path);
            IReadOnlyList<M22EntryInfo> entries;
            try { entries = M22Archive.ReadCentralDirectory(stream); }
            catch { continue; }
            if (entries.Count >= 2 && entries.All(e => e.Method == M22Archive.MethodDeflate))
            {
                target = path;
                break;
            }
        }
        Skip.If(target is null, "No suitable plain archive found.");

        byte[] original = File.ReadAllBytes(target!);
        IReadOnlyList<M22EntryInfo> list;
        using (var probe = new MemoryStream(original, writable: false))
            list = M22Archive.ReadCentralDirectory(probe);

        var victim = list[0];
        byte[] payload = "<replaced/>"u8.ToArray();
        var stored = new M22ArchiveRebuild.EntryReplacement(
            RawBytes: payload,
            Method: M22Archive.MethodStore,
            UncompressedSize: (uint)payload.Length,
            Crc32: Crc32(payload));

        byte[] rebuilt;
        using (var stream = new MemoryStream(original, writable: false))
        {
            rebuilt = M22ArchiveRebuild.Rebuild(stream,
                new Dictionary<string, M22ArchiveRebuild.EntryReplacement> { [victim.Name] = stored });
        }

        using var check = new ZipArchive(new MemoryStream(rebuilt), ZipArchiveMode.Read);
        Assert.Equal(list.Count, check.Entries.Count);

        var replaced = check.GetEntry(victim.Name);
        Assert.NotNull(replaced);
        using (var reader = new StreamReader(replaced!.Open()))
            Assert.Equal("<replaced/>", reader.ReadToEnd());

        using var originalZip = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read);
        foreach (var entry in list.Skip(1))
        {
            var before = ReadAll(originalZip.GetEntry(entry.Name)!);
            var after = ReadAll(check.GetEntry(entry.Name)!);
            Assert.Equal(before, after);
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        Span<uint> table = stackalloc uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[(int)i] = c;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = table[(int)((crc ^ b) & 0xFF)] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}

using System.IO.Compression;
using System.Text;

namespace ForzaCryptoTool.Tests;

public sealed class AssetEditServiceTests
{
    private const string InstallRoot = @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon6";
    private static string MediaRoot => Path.Combine(InstallRoot, "media");
    private static bool InstallPresent => Directory.Exists(MediaRoot);

    [Fact]
    public void A_utf8_bom_survives_a_round_trip()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. "<a/>\r\n"u8.ToArray()];
        byte[] saved = AssetEditService.EncodeTextLike(original, "<a/>\r\n");

        Assert.Equal(0xEF, saved[0]);
        Assert.Equal(0xBB, saved[1]);
        Assert.Equal(0xBF, saved[2]);
        Assert.Equal(original, saved);
    }

    [Fact]
    public void A_file_without_a_bom_does_not_gain_one()
    {
        byte[] original = "<a/>\r\n"u8.ToArray();
        byte[] saved = AssetEditService.EncodeTextLike(original, "<a/>\r\n");

        Assert.NotEqual(0xEF, saved[0]);
        Assert.Equal(original, saved);
    }

    [Fact]
    public void Crlf_files_stay_crlf()
    {
        byte[] original = "<a>\r\n  <b/>\r\n</a>\r\n"u8.ToArray();

        byte[] saved = AssetEditService.EncodeTextLike(original, "<a>\n  <c/>\n</a>\n");
        string text = Encoding.UTF8.GetString(saved);

        Assert.Contains("\r\n", text);
        Assert.DoesNotContain(text.Replace("\r\n", ""), "\n");
        Assert.Contains("<c/>", text);
    }

    [Fact]
    public void Lf_files_stay_lf()
    {
        byte[] original = "<a>\n  <b/>\n</a>\n"u8.ToArray();
        byte[] saved = AssetEditService.EncodeTextLike(original, "<a>\n  <c/>\n</a>\n");

        Assert.DoesNotContain("\r", Encoding.UTF8.GetString(saved));
    }

    [Fact]
    public void An_unchanged_text_file_re_encodes_to_the_identical_bytes()
    {
        foreach (byte[] original in new[]
                 {
                     "<a/>\r\n"u8.ToArray(),
                     "<a/>\n"u8.ToArray(),
                     [0xEF, 0xBB, 0xBF, .. "<a>\r\n</a>\r\n"u8.ToArray()],
                 })
        {
            bool bom = original.Length >= 3 && original[0] == 0xEF;
            string text = Encoding.UTF8.GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
            Assert.Equal(original, AssetEditService.EncodeTextLike(original, text));
        }
    }

    [Fact]
    public void Locked_entries_and_containers_cannot_be_saved()
    {
        var locked = new VfsNode("a.zip!/b.xml", "b.xml", VfsNodeKind.ArchiveEntry, 10, 10,
            Decryptability.MultiChunk, CanOpen: false, Blocker: "locked");
        var archive = new VfsNode("a.zip", "a.zip", VfsNodeKind.ArchiveRoot, 10, 10,
            Decryptability.NotEncrypted, CanOpen: false, Blocker: null);
        var openable = new VfsNode("a.zip!/c.xml", "c.xml", VfsNodeKind.ArchiveEntry, 10, 10,
            Decryptability.SingleChunk, CanOpen: true, Blocker: null);

        Assert.False(AssetEditService.CanSave(locked));
        Assert.False(AssetEditService.CanSave(archive));
        Assert.True(AssetEditService.CanSave(openable));
    }

    [SkippableFact]
    public async Task Editing_one_entry_leaves_every_other_entry_byte_identical()
    {
        Skip.IfNot(InstallPresent, "Game install not present.");

        string? source = FindPlainArchive();
        Skip.If(source is null, "No plain archive found.");

        string work = Path.Combine(Path.GetTempPath(), "fct-edit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string copy = Path.Combine(work, Path.GetFileName(source!));
            File.Copy(source!, copy);

            IReadOnlyList<M22EntryInfo> entries;
            using (var probe = File.OpenRead(copy))
                entries = M22Archive.ReadCentralDirectory(probe);

            var target = entries[0];
            byte[] newContent = "<edited-by-test/>"u8.ToArray();

            var before = new Dictionary<string, byte[]>();
            using (var zip = ZipFile.OpenRead(copy))
            {
                foreach (var e in zip.Entries.Where(e => e.FullName != target.Name))
                    before[e.FullName] = ReadAll(e);
            }

            string destination = Path.Combine(work, "out.zip");
            var service = new AssetEditService(new CryptoService(new BackendClient()));
            var outcome = await service.SaveArchiveEntryAsync(copy, target.Name, newContent, destination);

            Assert.True(outcome.Success, outcome.Message);

            using var result = ZipFile.OpenRead(destination);
            Assert.Equal(entries.Count, result.Entries.Count);

            Assert.Equal(newContent, ReadAll(result.GetEntry(target.Name)!));

            foreach (var (name, content) in before)
                Assert.Equal(content, ReadAll(result.GetEntry(name)!));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public void Saving_a_loose_file_writes_it_and_leaves_a_backup()
    {
        string work = Path.Combine(Path.GetTempPath(), "fct-loose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string path = Path.Combine(work, "settings.ini");
            File.WriteAllBytes(path, "original\r\n"u8.ToArray());

            var service = new AssetEditService(new CryptoService(new BackendClient()));
            byte[] updated = "changed\r\n"u8.ToArray();
            var outcome = service.SaveLooseFile(path, updated);

            Assert.True(outcome.Success, outcome.Message);
            Assert.Equal(updated, File.ReadAllBytes(path));

            var backups = Directory.GetFiles(work, "*.bak");
            Assert.Single(backups);
            Assert.Equal("original\r\n"u8.ToArray(), File.ReadAllBytes(backups[0]));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string? FindPlainArchive()
    {
        foreach (var path in Directory.EnumerateFiles(MediaRoot, "*.zip", SearchOption.AllDirectories))
        {
            long length;
            try { length = new FileInfo(path).Length; } catch { continue; }
            if (length > 4L * 1024 * 1024) continue;

            try
            {
                using var stream = File.OpenRead(path);
                var entries = M22Archive.ReadCentralDirectory(stream);
                if (entries.Count >= 2 && entries.All(e => e.Method != M22Archive.MethodM22))
                    return path;
            }
            catch { }
        }
        return null;
    }
}

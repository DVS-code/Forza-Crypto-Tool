using System.IO.Compression;
using System.Text;

namespace ForzaCryptoTool;

internal sealed class AssetEditService(CryptoService crypto)
{
    private readonly CryptoService _crypto = crypto;

    public event Action<string>? Progress;

    private void Report(string message)
    {
        Logger.Info(message);
        Progress?.Invoke(message);
    }

    internal sealed record SaveOutcome(bool Success, string? OutputPath, string Message)
    {
        public static SaveOutcome Ok(string path, string message) => new(true, path, message);
        public static SaveOutcome Fail(string message) => new(false, null, message);
    }

    public static bool CanSave(VfsNode node) => node.Kind switch
    {
        VfsNodeKind.File => true,
        VfsNodeKind.ArchiveEntry => node.CanOpen,
        _ => false,
    };

    public static string SuggestOutputName(VfsNode node)
    {
        var (container, entry) = AssetVfs.SplitPath(node.VfsPath);
        return entry is null
            ? Path.GetFileName(container)
            : Path.GetFileName(container);
    }

    public SaveOutcome SaveLooseFile(string path, byte[] content)
    {
        try
        {
            FileSafety.ReplaceWithBackup(path, content);
            byte[] written = File.ReadAllBytes(path);
            if (!written.AsSpan().SequenceEqual(content))
                return SaveOutcome.Fail("The file on disk does not match what was written.");

            return SaveOutcome.Ok(path, $"Saved {content.Length:N0} bytes to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Logger.Exception($"Saving '{path}' failed", ex);
            return SaveOutcome.Fail(ex.Message);
        }
    }

    public async Task<SaveOutcome> SaveArchiveEntryAsync(
        string containerPath,
        string entryName,
        byte[] newContent,
        string destinationPath,
        CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<M22EntryInfo> entries;
            using (var probe = File.OpenRead(containerPath))
                entries = M22Archive.ReadCentralDirectory(probe);

            var entry = entries.FirstOrDefault(e => e.Name == entryName)
                ?? throw new FileNotFoundException($"'{entryName}' is not in this archive.");

            bool encrypted = entry.Method == M22Archive.MethodM22;

            Report(encrypted
                ? "Rebuilding archive with the edited entry…"
                : "Rebuilding archive…");

            byte[] rebuilt = RebuildWithEntry(containerPath, entry, newContent);

            if (!encrypted)
            {
                if (!VerifyEntry(rebuilt, entryName, newContent, out string problem))
                    return SaveOutcome.Fail($"The rebuilt archive did not verify: {problem}");

                FileSafety.ReplaceWithBackup(destinationPath, rebuilt);
                return SaveOutcome.Ok(destinationPath,
                    $"Saved {Path.GetFileName(destinationPath)} ({rebuilt.Length:N0} bytes).");
            }

            string workDir = Path.Combine(Path.GetTempPath(), BuildConfig.AppName,
                "edit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            try
            {
                string editedPath = Path.Combine(workDir,
                    Path.GetFileNameWithoutExtension(containerPath) + "_edited.zip");
                await File.WriteAllBytesAsync(editedPath, rebuilt, ct);

                Report("Re-encrypting through the backend…");
                var result = await _crypto.EncryptAsync(editedPath, destinationPath, containerPath, ct);
                if (!result.Success) return SaveOutcome.Fail(result.Message);

                if (!VerifyArchiveReadable(destinationPath, entries.Count, out string problem))
                    return SaveOutcome.Fail($"The re-encrypted archive did not verify: {problem}");

                return SaveOutcome.Ok(destinationPath, result.Message);
            }
            finally
            {
                TryDeleteDirectory(workDir);
            }
        }
        catch (Exception ex)
        {
            Logger.Exception($"Saving '{entryName}' into '{containerPath}' failed", ex);
            return SaveOutcome.Fail(ex.Message);
        }
    }

    private static byte[] RebuildWithEntry(string containerPath, M22EntryInfo entry, byte[] content)
    {
        byte[] deflated = Deflate(content);
        var replacement = new M22ArchiveRebuild.EntryReplacement(
            RawBytes: deflated,
            Method: M22Archive.MethodDeflate,
            UncompressedSize: (uint)content.Length,
            Crc32: Crc32(content));

        using var source = File.OpenRead(containerPath);
        return M22ArchiveRebuild.Rebuild(source,
            new Dictionary<string, M22ArchiveRebuild.EntryReplacement> { [entry.Name] = replacement });
    }

    private static bool VerifyEntry(byte[] archiveBytes, string entryName, byte[] expected, out string problem)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
            var entry = archive.GetEntry(entryName);
            if (entry is null) { problem = $"'{entryName}' is missing from the rebuilt archive."; return false; }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            if (!buffer.ToArray().AsSpan().SequenceEqual(expected))
            {
                problem = "the entry's contents do not match what was saved.";
                return false;
            }
            problem = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static bool VerifyArchiveReadable(string path, int expectedEntries, out string problem)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var entries = M22Archive.ReadCentralDirectory(stream);
            if (entries.Count != expectedEntries)
            {
                problem = $"expected {expectedEntries} entries, found {entries.Count}.";
                return false;
            }
            problem = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            string safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), BuildConfig.AppName))
                              + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            if (candidate.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                Directory.Delete(candidate, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static byte[] EncodeTextLike(byte[] original, string editedText)
    {
        bool hadBom = original.Length >= 3
                      && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;

        bool crlf = FirstLineEndingIsCrLf(original);
        string normalized = editedText.Replace("\r\n", "\n");
        if (crlf) normalized = normalized.Replace("\n", "\r\n");

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        byte[] body = encoding.GetBytes(normalized);
        if (!hadBom) return body;

        var withBom = new byte[body.Length + 3];
        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
        body.CopyTo(withBom, 3);
        return withBom;
    }

    private static bool FirstLineEndingIsCrLf(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == (byte)'\n') return i > 0 && data[i - 1] == (byte)'\r';
        }
        return true;
    }
}

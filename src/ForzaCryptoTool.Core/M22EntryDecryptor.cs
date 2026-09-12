using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ForzaCryptoTool;

internal sealed class M22EntryDecryptor(CryptoService crypto, IvTableStore ivTable) : IM22EntryDecryptor
{
    private readonly CryptoService _crypto = crypto;
    private readonly IvTableStore _ivTable = ivTable;

    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

    public bool HasIvsFor(string containerPath, M22EntryInfo entry)
    {
        if (entry.Decryptability == Decryptability.SingleChunk) return true;
        if (entry.Decryptability != Decryptability.MultiChunk) return false;
        if (_ivTable.Count == 0) return false;

        try
        {
            using var stream = File.OpenRead(containerPath);
            byte[] payload = M22Archive.ReadEntryBytes(stream, entry);
            return _ivTable.Contains(IvTableStore.Fingerprint(payload));
        }
        catch { return false; }
    }

    public async Task<byte[]> DecryptEntryAsync(
        string containerPath, M22EntryInfo entry, byte[] payload, CancellationToken ct)
    {
        string cacheKey = containerPath + AssetVfs.EntrySeparator + entry.Name;
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        string workDir = Path.Combine(Path.GetTempPath(), BuildConfig.AppName,
            "entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            string requestPath = Path.Combine(workDir, "entry.zip");
            string resultPath = Path.Combine(workDir, "entry_decrypted.zip");

            await File.WriteAllBytesAsync(requestPath, BuildSingleEntryArchive(entry, payload), ct);

            var result = await _crypto.DecryptAsync(requestPath, resultPath, ct);
            if (!result.Success)
                throw new InvalidOperationException(result.Message);

            byte[] plaintext = ExtractSingleEntry(resultPath, entry.Name);
            _cache[cacheKey] = plaintext;
            return plaintext;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    internal static byte[] BuildSingleEntryArchive(M22EntryInfo entry, byte[] payload)
    {
        byte[] name = Encoding.UTF8.GetBytes(entry.Name);
        using var output = new MemoryStream(payload.Length + name.Length * 2 + 128);

        var local = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(0), 0x04034B50);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), entry.Method);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), entry.Crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), entry.UncompressedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)name.Length);
        output.Write(local);
        output.Write(name);
        output.Write(payload);

        long centralStart = output.Position;

        var central = new byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(0), 0x02014B50);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(10), entry.Method);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(16), entry.Crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), entry.UncompressedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)name.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(42), 0);
        output.Write(central);
        output.Write(name);

        long centralSize = output.Position - centralStart;

        var eocd = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(0), 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), (uint)centralSize);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), (uint)centralStart);
        output.Write(eocd);

        return output.ToArray();
    }

    private static byte[] ExtractSingleEntry(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var entry = archive.GetEntry(entryName)
                    ?? archive.Entries.FirstOrDefault(e => e.Name == Path.GetFileName(entryName))
                    ?? (archive.Entries.Count == 1 ? archive.Entries[0] : null)
                    ?? throw new InvalidDataException(
                        $"The decrypted archive did not contain '{entryName}'.");

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
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
}

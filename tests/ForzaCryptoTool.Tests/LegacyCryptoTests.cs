using System.IO.Compression;
using System.Security.Cryptography;

namespace ForzaCryptoTool.Tests;

public sealed class LegacyCryptoTests
{
    private static readonly byte[] FixedIv = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    public static IEnumerable<object[]> AllContexts()
        => LegacyKeyStore.Contexts.Select(c => new object[] { LegacyKeyStore.ShortName(c.Game), c.KeyType.ToString() });

    private static LegacyContext Context(string game, string keyType)
    {
        Assert.True(LegacyKeyStore.TryParseGame(game, out var parsedGame));
        Assert.True(LegacyKeyStore.TryParseKeyType(keyType, out var parsedKey));
        return LegacyKeyStore.Find(parsedGame, parsedKey)
            ?? throw new InvalidOperationException($"No {game} {keyType} context.");
    }

    private static byte[] Pattern(int length, int seed = 3)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = (byte)(i * 7 + seed);
        return data;
    }

    [Fact]
    public void Key_store_carries_every_CryptoTool_context()
    {
        var contexts = LegacyKeyStore.Contexts;
        Assert.Equal(35, contexts.Count);
        Assert.Equal(8, contexts.Select(c => c.Game).Distinct().Count());

        Assert.All(contexts, c => Assert.Equal(
            c.Game is LegacyGame.FM7 or LegacyGame.FM6Apex ? LegacyContainer.Motorsport : LegacyContainer.Horizon,
            c.Container));

        Assert.All(contexts, c => Assert.Equal(c.KeyType == LegacyKeyType.GameDB, c.Scramble is not null));
        Assert.All(contexts, c => Assert.Equal(
            c.KeyType is LegacyKeyType.GameDB or LegacyKeyType.SFS ? 0x20000 : 0x200, c.BlockSize));
    }

    [Fact]
    public void No_two_contexts_share_a_mac_key()
    {
        var keys = LegacyKeyStore.Contexts
            .Select(c => Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>(c.MacKeys)))
            .ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Decryption_run_backwards_equals_CryptoTools_encryption_tables()
    {
        var random = new Random(1234);
        var plain = new byte[16];
        var viaInverse = new byte[16];
        var viaTables = new byte[16];
        var back = new byte[16];
        int checkedContexts = 0;

        foreach (var context in LegacyKeyStore.Contexts.Where(c => c.EncryptionKeys is not null))
        {
            for (int i = 0; i < 500; i++)
            {
                random.NextBytes(plain);
                context.Tables.Encryptor.ProcessBlock(context.DecryptionKeys, plain, viaInverse);
                context.Tables.Encryption.ProcessBlock(context.EncryptionKeys, plain, viaTables);
                Assert.Equal(viaTables, viaInverse);

                context.Tables.Decryption.ProcessBlock(context.DecryptionKeys, viaInverse, back);
                Assert.Equal(plain, back);
            }
            checkedContexts++;
        }

        Assert.Equal(15, checkedContexts);
    }

    [Theory]
    [MemberData(nameof(AllContexts))]
    public void Inverse_undoes_decryption_for_every_key(string game, string keyType)
    {
        var context = Context(game, keyType);
        var random = new Random(game.GetHashCode() ^ keyType.GetHashCode());
        var cipherText = new byte[16];
        var plain = new byte[16];
        var back = new byte[16];

        for (int i = 0; i < 200; i++)
        {
            random.NextBytes(cipherText);
            context.Tables.Decryption.ProcessBlock(context.DecryptionKeys, cipherText, plain);
            context.Tables.Encryptor.ProcessBlock(context.DecryptionKeys, plain, back);
            Assert.Equal(cipherText, back);
        }
    }

    [Theory]
    [InlineData("FH5", "Profile", "u3o6STnTUN/swHIacVffy+b03peqmNeNJ04KNVSHNpo=")]
    [InlineData("FH5_v1.619.349.0", "Profile", "M+FrAMp7uPZd4qpakuknWQAeIqQBlgi0nsB91H7vl5E=")]
    [InlineData("FH5", "Photo", "w7kbmRU2Y0maiPhL5xF786n0DMvwIvONtdzN7fnZe5k=")]
    [InlineData("FH5", "Dynamic", "t5vQLZhGrYDqpoYGqGNDDsJmmpO1qXvgvRuCt48km0g=")]
    [InlineData("FH4", "Profile", "LWCMU2UVXqjkRCy/47FbKq725YfRbnOXBCv0gyXGVcY=")]
    [InlineData("FH4", "Photo", "icXcNcYdNgfJSMfI1Dw3dkm+Xjh8/npu8EbgpdWR6rs=")]
    [InlineData("FH4", "Dynamic", "mQXBUlFufH26+LKK80ZJzXymY885gEeQbnH4u1XmH9A=")]
    [InlineData("FH3", "Profile", "Pk09MDh47tTaxWZwOEzvXA7fmVX7/iEUds0QGLVTiBg=")]
    [InlineData("FH3", "Photo", "UTB4HYkEhQGwwVgOntEvizEoy85Ri+YzJUTRxp+UHpg=")]
    [InlineData("FH3Dev", "Profile", "LW+drWpj2dTgIS9K+YhTAvAoavL07vtiBaSdIQ2IQ5g=")]
    [InlineData("FM7", "Profile", "vj6QxSonBlRUTaEfEMvlm6inSJA1L0gxugaBkRh89ng=")]
    [InlineData("FM7", "Reward", "Q65HVBXNgZBRYXJo5xOAKji9RDxrx7wV3XxvCmYKP8I=")]
    [InlineData("FM7", "Photo", "L6AEZEtie6U3FS+oboQWeREiyBW9rAHsM/R4NgsQg6E=")]
    [InlineData("FM6Apex", "Profile", "SfS/k0E687UCF10TfeYYheljYiaCXzBpBiRxIEDBHcs=")]
    [InlineData("FM6Apex", "Photo", "52MRrtye9D/GAbgsa5IlRYA1gWCxKcjif7fFxO5UMrI=")]
    public void Encrypt_reproduces_CryptoTools_ciphertext(string game, string keyType, string expectedSha256)
    {
        var context = Context(game, keyType);

        int length = context.Container == LegacyContainer.Motorsport ? 1024 : 1000;
        var encrypted = LegacyCrypto.Encrypt(Pattern(length), context, FixedIv);
        Assert.Equal(expectedSha256, Convert.ToBase64String(SHA256.HashData(encrypted)));
    }

    [Theory]
    [MemberData(nameof(AllContexts))]
    public void Every_key_round_trips_and_identifies_itself(string game, string keyType)
    {
        var context = Context(game, keyType);
        int block = context.BlockSize;

        foreach (int length in new[] { 0, 1, 15, 16, 17, block - 1, block, block + 1, block * 2 + 333 })
        {
            var plain = Pattern(length, seed: length);
            var encrypted = LegacyCrypto.Encrypt(plain, context, FixedIv);

            long blocks = (length + block - 1) / block;
            Assert.Equal(context.HeaderSize + blocks * (block + 16), encrypted.Length);

            var result = LegacyCrypto.TryDecrypt(encrypted);
            Assert.NotNull(result);
            Assert.Same(context, result.Info.Context);
            Assert.Equal(FixedIv, result.Info.Iv);
            Assert.Equal(0, result.BadBlockMacs);

            if (context.Container == LegacyContainer.Horizon)
            {
                Assert.Equal(plain, result.Plaintext);
            }
            else
            {
                Assert.Equal(blocks * block, result.Plaintext.Length);
                Assert.Equal(plain, result.Plaintext[..length]);
            }
        }
    }

    [Fact]
    public void Scramble_is_its_own_inverse_and_leaves_padding_alone()
    {
        var context = Context("FH4", "GameDB");
        var data = Pattern(4096);
        var scrambled = (byte[])data.Clone();
        context.Scramble!.Apply(0x20000, scrambled);
        Assert.NotEqual(data, scrambled);
        context.Scramble.Apply(0x20000, scrambled);
        Assert.Equal(data, scrambled);

        var encrypted = LegacyCrypto.Encrypt(Pattern(1000), context, FixedIv);
        var raw = DecryptWithoutScramble(encrypted, context);
        Assert.All(raw[1000..], b => Assert.Equal(0, b));
        Assert.NotEqual(Pattern(1000), raw[..1000]);
    }

    private static byte[] DecryptWithoutScramble(byte[] encrypted, LegacyContext context)
    {
        var output = new byte[context.BlockSize];
        var previous = encrypted[..16];
        var clear = new byte[16];
        for (int i = 0; i < context.BlockSize; i += 16)
        {
            var block = encrypted.AsSpan(context.HeaderSize + i, 16);
            context.Tables.Decryption.ProcessBlock(context.DecryptionKeys, block, clear);
            for (int j = 0; j < 16; j++) output[i + j] = (byte)(clear[j] ^ previous[j]);
            previous = block.ToArray();
        }
        return output;
    }

    [Fact]
    public void Tampering_is_caught()
    {
        var context = Context("FH4", "File");
        var encrypted = LegacyCrypto.Encrypt(Pattern(2000), context, FixedIv);

        var damagedData = (byte[])encrypted.Clone();
        damagedData[context.HeaderSize + 700] ^= 1;
        var result = LegacyCrypto.TryDecrypt(damagedData);
        Assert.NotNull(result);
        Assert.True(result.BadBlockMacs > 0);

        foreach (int offset in new[] { 0, 17, 25 })
        {
            var damagedHeader = (byte[])encrypted.Clone();
            damagedHeader[offset] ^= 1;
            Assert.Null(LegacyCrypto.TryDecrypt(damagedHeader));
        }

        Assert.Null(LegacyCrypto.TryDecrypt(encrypted.AsSpan(0, encrypted.Length - 0x210)));
    }

    [Fact]
    public void Unrelated_data_is_never_identified()
    {
        var random = new Random(99);
        var noise = new byte[0x24 + 0x210 * 4];
        for (int i = 0; i < 50; i++)
        {
            random.NextBytes(noise);
            Assert.Null(LegacyCrypto.Identify(noise, noise.Length));
        }
        Assert.Null(LegacyCrypto.Identify(new byte[10], 10));
        Assert.Null(LegacyCrypto.Identify("SQLite format 3\0"u8.ToArray().Concat(new byte[100]).ToArray(), 116));
    }

    private static byte[] BuildZip(params (string Name, byte[] Data, CompressionLevel Level)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data, level) in files)
            {
                using var entry = zip.CreateEntry(name, level).Open();
                entry.Write(data);
            }
        }
        return stream.ToArray();
    }

    private static Dictionary<string, byte[]> ReadZip(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    [Fact]
    public void Archive_round_trips_through_method_22()
    {
        var context = Context("FH4", "File");
        var files = new (string, byte[], CompressionLevel)[]
        {
            ("physics/a.xml", Pattern(5000), CompressionLevel.Optimal),
            ("stored.bin", Pattern(300, 9), CompressionLevel.NoCompression),
            ("empty.txt", Array.Empty<byte>(), CompressionLevel.Optimal),
            ("b.ini", "TimeScale 1\r\n"u8.ToArray(), CompressionLevel.Optimal),
        };
        var plainZip = BuildZip(files);

        var encrypted = LegacyZip.Encrypt(plainZip, context, _ => FixedIv);
        Assert.Equal(2, encrypted.Converted);
        Assert.Same(context, encrypted.First!.Context);

        var ivs = LegacyZip.ReadEntryIvs(encrypted.Archive);
        Assert.Equal(new[] { "b.ini", "physics/a.xml" }, ivs.Keys.Order().ToArray());
        Assert.All(ivs.Values, iv => Assert.Equal(FixedIv, iv));

        var decrypted = LegacyZip.Decrypt(encrypted.Archive);
        Assert.Equal(2, decrypted.Converted);
        Assert.Equal(0, decrypted.Unrecognised);
        Assert.Equal(0, decrypted.BadBlockMacs);

        var contents = ReadZip(decrypted.Archive);
        Assert.Equal(files.Length, contents.Count);
        foreach (var (name, data, _) in files)
            Assert.Equal(data, contents[name]);

        var again = LegacyZip.Encrypt(decrypted.Archive, context, _ => FixedIv);
        Assert.Equal(encrypted.Archive, again.Archive);
        Assert.Equal(decrypted.Archive, LegacyZip.Decrypt(again.Archive).Archive);
    }

    [Fact]
    public async Task Service_decrypts_and_encrypts_without_a_backend()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fct-legacy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var plain = Pattern(3000);
            string plainPath = Path.Combine(folder, "PhysicsSettings_decrypted.ini");
            string encryptedPath = Path.Combine(folder, "PhysicsSettings.ini");
            string backPath = Path.Combine(folder, "back.ini");
            await File.WriteAllBytesAsync(plainPath, plain);

            using var backend = new BackendClient();
            var service = new CryptoService(backend);

            var encrypted = await service.EncryptAsync(plainPath, encryptedPath,
                legacy: new CryptoService.LegacyTarget(LegacyGame.FH4));
            Assert.True(encrypted.Success, encrypted.Message);

            var detection = FileDetection.Detect(encryptedPath);
            Assert.Equal(DetectedKind.LegacyEncrypted, detection.Kind);
            Assert.Equal(LegacyGame.FH4, detection.Legacy!.Context.Game);
            Assert.Equal(LegacyKeyType.File, detection.Legacy.Context.KeyType);
            Assert.True(CryptoService.RunsLocally(detection));
            Assert.True(CryptoService.CanDecrypt(detection.Kind));

            var decrypted = await service.DecryptAsync(encryptedPath, backPath);
            Assert.True(decrypted.Success, decrypted.Message);
            Assert.Equal(plain, await File.ReadAllBytesAsync(backPath));

            string againPath = Path.Combine(folder, "again.ini");
            var again = await service.EncryptAsync(backPath, againPath, originalPath: encryptedPath);
            Assert.True(again.Success, again.Message);
            Assert.Equal(await File.ReadAllBytesAsync(encryptedPath), await File.ReadAllBytesAsync(againPath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Key_type_is_inferred_from_the_plaintext()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fct-legacy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            string db = Path.Combine(folder, "db"), save = Path.Combine(folder, "save"), other = Path.Combine(folder, "other");
            File.WriteAllBytes(db, "SQLite format 3\0"u8.ToArray().Concat(new byte[64]).ToArray());
            File.WriteAllBytes(save, new byte[] { 0xB6, 0xF2, 0x8B, 0x4A, 1, 2, 3, 4 });
            File.WriteAllBytes(other, "TimeScale 1"u8.ToArray());

            Assert.Equal(LegacyKeyType.GameDB, CryptoService.InferLegacyKeyType(db, LegacyGame.FH4));
            Assert.Equal(LegacyKeyType.Profile, CryptoService.InferLegacyKeyType(save, LegacyGame.FH3));
            Assert.Equal(LegacyKeyType.File, CryptoService.InferLegacyKeyType(other, LegacyGame.FH5));

            Assert.Equal(LegacyKeyType.ConfigFile, CryptoService.InferLegacyKeyType(other, LegacyGame.FM7));

            var plan = CryptoService.PlanLegacyEncrypt(other, null,
                new CryptoService.LegacyTarget(LegacyGame.FH3, LegacyKeyType.Dynamic), out var error);
            Assert.Null(plan);
            Assert.NotNull(error);

            Assert.Null(CryptoService.PlanLegacyEncrypt(other, null, null, out error));
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string? SampleRoot()
    {
        var folder = Environment.GetEnvironmentVariable("FCT_LEGACY_SAMPLES");
        return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;
    }

    [SkippableFact]
    public void Real_files_re_encrypt_to_the_exact_original_bytes()
    {
        string? root = SampleRoot();
        Skip.If(root is null, "No legacy sample folder present.");

        int files = 0, archives = 0;
        foreach (var path in Directory.EnumerateFiles(root!, "*", SearchOption.AllDirectories))
        {
            var detection = FileDetection.Detect(path);
            var original = File.ReadAllBytes(path);

            if (detection.Kind == DetectedKind.LegacyEncrypted)
            {
                var decrypted = LegacyCrypto.Decrypt(original, detection.Legacy!);
                Assert.Equal(0, decrypted.BadBlockMacs);
                var encrypted = LegacyCrypto.Encrypt(decrypted.Plaintext, detection.Legacy!.Context, detection.Legacy.Iv);
                Assert.True(original.AsSpan().SequenceEqual(encrypted), $"{path} did not re-encrypt identically.");
                files++;
            }
            else if (detection.Kind == DetectedKind.LegacyZip)
            {
                var decrypted = LegacyZip.Decrypt(original);
                Assert.Equal(0, decrypted.Unrecognised);
                Assert.Equal(0, decrypted.BadBlockMacs);
                Assert.NotEmpty(ReadZip(decrypted.Archive));

                var ivs = LegacyZip.ReadEntryIvs(original);
                var encrypted = LegacyZip.Encrypt(decrypted.Archive, detection.Legacy!.Context, name => ivs[name]);
                Assert.True(original.AsSpan().SequenceEqual(encrypted.Archive), $"{path} did not re-encrypt identically.");
                archives++;
            }
        }

        Skip.If(files + archives == 0, "Sample folder holds no legacy-encrypted files.");
    }
}

using System.Buffers.Binary;

namespace ForzaCryptoTool.Tests;

public sealed class Fh5SaveSwapTests
{
    private const ulong DonorXuid = 2535463007977723;
    private const ulong TargetXuid = 2535470423520115;

    private static byte[] BuildPlaintext(ulong xuid, int filler = 3000)
    {
        using var stream = new MemoryStream();
        void Section(string name, byte[] body)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header, Fh5SaveSwap.SectionHash(name));
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)body.Length);
            stream.Write(header);
            stream.Write(body);
        }

        Section("profile", Enumerable.Range(0, filler).Select(i => (byte)(i * 13)).ToArray());
        Section("savestate", Enumerable.Range(0, 777).Select(i => (byte)(i * 7 + 1)).ToArray());
        var binary = new byte[64];
        BinaryPrimitives.WriteUInt64LittleEndian(binary.AsSpan(4), xuid);
        Section("binary", binary);
        return stream.ToArray();
    }

    private static byte[] Encrypt(byte[] plain, LegacyGame game = LegacyGame.FH5)
        => LegacyCrypto.Encrypt(plain, LegacyKeyStore.Find(game, LegacyKeyType.Profile)!, LegacyCrypto.RandomIv());

    [Fact]
    public void Section_hash_matches_the_game()
    {
        Assert.Equal(0x4A8BF2B6u, Fh5SaveSwap.SectionHash("profile"));
    }

    [Fact]
    public void Xuid_is_found_four_bytes_into_the_binary_section()
    {
        var plain = BuildPlaintext(DonorXuid);
        int offset = Fh5SaveSwap.FindXuidOffset(plain);
        Assert.Equal(8 + 3000 + 8 + 777 + 8 + 4, offset);
        Assert.Equal(DonorXuid, BinaryPrimitives.ReadUInt64LittleEndian(plain.AsSpan(offset)));

        Assert.Equal(-1, Fh5SaveSwap.FindXuidOffset(plain.AsSpan(0, 100)));
        Assert.Equal(-1, Fh5SaveSwap.FindXuidOffset(new byte[200]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Swap_changes_only_the_xuid(bool donorFromV1619)
    {
        var plain = BuildPlaintext(DonorXuid);
        var donor = Encrypt(plain, donorFromV1619 ? LegacyGame.FH5_v1_619_349_0 : LegacyGame.FH5);

        var patched = Fh5SaveSwap.Patch(donor, TargetXuid);
        Assert.Equal(DonorXuid, patched.DonorXuid);
        Assert.Equal(donor.Length, patched.Encrypted.Length);

        var result = LegacyCrypto.TryDecrypt(patched.Encrypted)!;
        Assert.Equal(LegacyGame.FH5, result.Info.Context.Game);
        Assert.Equal(new byte[16], result.Info.Iv);
        Assert.Equal(0, result.BadBlockMacs);

        var expected = BuildPlaintext(TargetXuid);
        Assert.Equal(expected, result.Plaintext);
    }

    [Fact]
    public void Ambiguous_xuid_is_refused()
    {
        var plain = BuildPlaintext(DonorXuid);
        BinaryPrimitives.WriteUInt64LittleEndian(plain.AsSpan(100), DonorXuid);
        var error = Assert.Throws<InvalidDataException>(() => Fh5SaveSwap.Patch(Encrypt(plain), TargetXuid));
        Assert.Contains("uniquely", error.Message);
    }

    [Fact]
    public void Only_fh5_saves_are_accepted()
    {
        var plain = BuildPlaintext(DonorXuid);
        Assert.Throws<InvalidDataException>(() => Fh5SaveSwap.Patch(Encrypt(plain, LegacyGame.FH4), TargetXuid));
        Assert.Throws<InvalidDataException>(() => Fh5SaveSwap.Patch(new byte[4096], TargetXuid));
    }

    [Fact]
    public async Task Service_swaps_backs_up_and_restores_without_a_backend()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fct-fh5-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            string donorPath = Path.Combine(folder, "donor.ProfileData");
            string activePath = Path.Combine(folder, "User_901FF35EEB373.ProfileData");
            await File.WriteAllBytesAsync(donorPath, Encrypt(BuildPlaintext(DonorXuid)));
            var original = Encrypt(BuildPlaintext(TargetXuid, filler: 500));
            await File.WriteAllBytesAsync(activePath, original);

            using var backend = new BackendClient();
            var service = new SaveSwapService(backend);

            var result = await service.SwapAsync(donorPath, activePath, TargetXuid.ToString());
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.BackupPath);
            Assert.Equal(original, await File.ReadAllBytesAsync(result.BackupPath!));

            var swapped = LegacyCrypto.TryDecrypt(await File.ReadAllBytesAsync(activePath))!;
            Assert.Equal(BuildPlaintext(TargetXuid), swapped.Plaintext);

            Assert.True(service.HasBackup(activePath));
            Assert.True(service.RestoreOriginal(activePath).Success);
            Assert.Equal(original, await File.ReadAllBytesAsync(activePath));

            var refused = await service.SwapAsync(donorPath, activePath, "not-a-xuid");
            Assert.False(refused.Success);
            Assert.Equal(original, await File.ReadAllBytesAsync(activePath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

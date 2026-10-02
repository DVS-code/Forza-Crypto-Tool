using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ForzaCryptoTool;

internal sealed record LegacyFileInfo(LegacyContext Context, byte[] Iv, uint PaddingBytes, uint DataBytes)
{
    public long PlainBytes => (long)DataBytes - PaddingBytes;
    public string IvHex => Convert.ToHexString(Iv);
}

internal sealed record LegacyDecryptResult(LegacyFileInfo Info, byte[] Plaintext, int BadBlockMacs);

internal static class LegacyCrypto
{
    private const int CipherBlock = 16;

    public static LegacyFileInfo? Identify(ReadOnlySpan<byte> header, long totalBytes)
    {
        if (totalBytes > uint.MaxValue) return null;

        Span<byte> signed = stackalloc byte[24];
        Span<byte> mac = stackalloc byte[TfitMac.Size];

        foreach (var context in LegacyKeyStore.Contexts)
        {
            int headerSize = context.HeaderSize;
            if (header.Length < headerSize || totalBytes < headerSize) continue;

            long blocks = (totalBytes - headerSize) / (context.BlockSize + CipherBlock);
            uint dataBytes = (uint)(blocks * context.BlockSize);
            uint padding = 0;

            BinaryPrimitives.WriteUInt32LittleEndian(signed, dataBytes);
            header[..16].CopyTo(signed[4..]);
            int signedLength = 20;
            if (context.Container == LegacyContainer.Horizon)
            {
                padding = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
                BinaryPrimitives.WriteUInt32LittleEndian(signed[20..], padding);
                signedLength = 24;
            }

            TfitMac.Compute(context.Tables.Mac, context.MacKeys, signed[..signedLength], mac);
            if (!mac.SequenceEqual(header.Slice(headerSize - TfitMac.Size, TfitMac.Size))) continue;
            if (padding > dataBytes) continue;

            return new LegacyFileInfo(context, header[..16].ToArray(), padding, dataBytes);
        }
        return null;
    }

    public static LegacyDecryptResult? TryDecrypt(ReadOnlySpan<byte> encrypted)
    {
        var info = Identify(encrypted, encrypted.Length);
        return info is null ? null : Decrypt(encrypted, info);
    }

    public static LegacyDecryptResult Decrypt(ReadOnlySpan<byte> encrypted, LegacyFileInfo info)
    {
        var context = info.Context;
        var cipher = context.Tables.Decryption;
        int blockSize = context.BlockSize;

        var plaintext = new byte[info.PlainBytes];
        var block = new byte[blockSize];
        Span<byte> previous = stackalloc byte[CipherBlock];
        Span<byte> storedMac = stackalloc byte[TfitMac.Size];
        Span<byte> mac = stackalloc byte[TfitMac.Size];
        info.Iv.CopyTo(previous);

        var input = encrypted[context.HeaderSize..];
        int badMacs = 0;

        for (long offset = 0; offset < info.DataBytes; offset += blockSize)
        {
            for (int i = 0; i < blockSize; i += CipherBlock)
            {
                DecryptCbc(cipher, context.DecryptionKeys, input[..CipherBlock], previous, block.AsSpan(i));
                input = input[CipherBlock..];
            }

            DecryptCbc(cipher, context.DecryptionKeys, input[..CipherBlock], previous, storedMac);
            input = input[CipherBlock..];

            TfitMac.Compute(context.Tables.Mac, context.MacKeys, block, mac);
            if (!mac.SequenceEqual(storedMac)) badMacs++;

            context.Scramble?.Apply(offset, block);

            int keep = (int)Math.Min(blockSize, plaintext.Length - offset);
            if (keep > 0) block.AsSpan(0, keep).CopyTo(plaintext.AsSpan((int)offset));
        }

        return new LegacyDecryptResult(info, plaintext, badMacs);
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, LegacyContext context, ReadOnlySpan<byte> iv)
    {
        if (iv.Length != CipherBlock) throw new ArgumentException("The IV must be 16 bytes.", nameof(iv));

        int blockSize = context.BlockSize;
        long dataBytes = ((long)plaintext.Length + blockSize - 1) / blockSize * blockSize;
        if (dataBytes > uint.MaxValue) throw new NotSupportedException("File is too large for this format.");
        uint padding = (uint)(dataBytes - plaintext.Length);

        long blocks = dataBytes / blockSize;
        var output = new byte[context.HeaderSize + blocks * (blockSize + CipherBlock)];

        Span<byte> signed = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signed, (uint)dataBytes);
        iv.CopyTo(signed[4..]);
        iv.CopyTo(output);
        int signedLength = 20;
        if (context.Container == LegacyContainer.Horizon)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(signed[20..], padding);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16), padding);
            signedLength = 24;
        }
        TfitMac.Compute(context.Tables.Mac, context.MacKeys, signed[..signedLength],
            output.AsSpan(context.HeaderSize - TfitMac.Size, TfitMac.Size));

        var cipher = context.Tables.Encryptor;
        var block = new byte[blockSize];
        Span<byte> previous = stackalloc byte[CipherBlock];
        Span<byte> mac = stackalloc byte[TfitMac.Size];
        iv.CopyTo(previous);
        var destination = output.AsSpan(context.HeaderSize);

        for (long offset = 0; offset < dataBytes; offset += blockSize)
        {
            int take = (int)Math.Min(blockSize, plaintext.Length - offset);
            plaintext.Slice((int)offset, take).CopyTo(block);
            block.AsSpan(take).Clear();

            context.Scramble?.Apply(offset, block.AsSpan(0, take));
            TfitMac.Compute(context.Tables.Mac, context.MacKeys, block, mac);

            for (int i = 0; i < blockSize; i += CipherBlock)
            {
                EncryptCbc(cipher, context.DecryptionKeys, block.AsSpan(i, CipherBlock), previous, destination);
                destination = destination[CipherBlock..];
            }
            EncryptCbc(cipher, context.DecryptionKeys, mac, previous, destination);
            destination = destination[CipherBlock..];
        }

        return output;
    }

    public static byte[] RandomIv() => RandomNumberGenerator.GetBytes(CipherBlock);

    private static void DecryptCbc(
        TfitTables cipher, ReadOnlySpan<uint> keys, ReadOnlySpan<byte> source, Span<byte> previous, Span<byte> destination)
    {
        Span<byte> clear = stackalloc byte[CipherBlock];
        cipher.ProcessBlock(keys, source, clear);
        for (int i = 0; i < CipherBlock; i++) destination[i] = (byte)(clear[i] ^ previous[i]);
        source.CopyTo(previous);
    }

    private static void EncryptCbc(
        TfitInverse cipher, ReadOnlySpan<uint> keys, ReadOnlySpan<byte> source, Span<byte> previous, Span<byte> destination)
    {
        Span<byte> mixed = stackalloc byte[CipherBlock];
        for (int i = 0; i < CipherBlock; i++) mixed[i] = (byte)(source[i] ^ previous[i]);
        cipher.ProcessBlock(keys, mixed, destination);
        destination[..CipherBlock].CopyTo(previous);
    }
}

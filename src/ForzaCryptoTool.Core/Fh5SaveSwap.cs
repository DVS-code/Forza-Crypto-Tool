using System.Buffers.Binary;

namespace ForzaCryptoTool;

internal static class Fh5SaveSwap
{
    public static bool IsFh5Profile(LegacyFileInfo? info)
        => info is not null
        && info.Context.KeyType == LegacyKeyType.Profile
        && info.Context.Game is LegacyGame.FH5 or LegacyGame.FH5_v1_619_349_0;

    public static int FindXuidOffset(ReadOnlySpan<byte> plain)
    {
        int offset = 0;
        foreach (var name in new[] { "profile", "savestate", "binary" })
        {
            if (offset + 8 > plain.Length) return -1;
            uint hash = BinaryPrimitives.ReadUInt32LittleEndian(plain[offset..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(plain[(offset + 4)..]);
            offset += 8;
            if (hash != SectionHash(name) || size > plain.Length - offset) return -1;

            if (name == "binary")
                return size >= 12 && offset + 12 <= plain.Length ? offset + 4 : -1;
            offset += (int)size;
        }
        return -1;
    }

    public static uint SectionHash(string name)
    {
        uint value = 5381;
        for (int pass = 0; pass < 2; pass++)
            foreach (char c in name)
                value = unchecked(value ^ ((uint)c + (value >> 2) + 32 * value));
        return value;
    }

    public sealed record Patched(byte[] Encrypted, ulong DonorXuid, int XuidOffset);

    public static Patched Patch(byte[] donorEncrypted, ulong targetXuid)
    {
        var donor = LegacyCrypto.TryDecrypt(donorEncrypted);
        if (donor is null || !IsFh5Profile(donor.Info))
            throw new InvalidDataException(
                "The donor isn't a supported FH5 save. FH5 saves up to v1.619.349.0 can be swapped.");
        if (donor.BadBlockMacs > 0)
            throw new InvalidDataException("The donor save is damaged (its block MACs don't verify).");

        var plain = donor.Plaintext;
        int offset = FindXuidOffset(plain);
        if (offset < 0)
            throw new InvalidDataException("Could not find the account XUID in the donor save.");

        var current = plain.AsSpan(offset, 8);
        if (CountOccurrences(plain, current) != 1)
            throw new InvalidDataException("The XUID location in the donor save could not be uniquely identified.");

        ulong donorXuid = BinaryPrimitives.ReadUInt64LittleEndian(current);
        BinaryPrimitives.WriteUInt64LittleEndian(plain.AsSpan(offset), targetXuid);

        var context = LegacyKeyStore.Find(LegacyGame.FH5, LegacyKeyType.Profile)!;
        var encrypted = LegacyCrypto.Encrypt(plain, context, new byte[16]);

        var check = LegacyCrypto.TryDecrypt(encrypted);
        if (check is null || check.BadBlockMacs > 0 || !check.Plaintext.AsSpan().SequenceEqual(plain))
            throw new InvalidDataException("The swapped save did not decrypt back correctly. Nothing was written.");
        if (encrypted.Length != donorEncrypted.Length)
            throw new InvalidDataException("The swapped save is not the same size as the donor. Nothing was written.");

        return new Patched(encrypted, donorXuid, offset);
    }

    private static int CountOccurrences(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        int count = 0;
        for (int index = data.IndexOf(needle); index >= 0;)
        {
            count++;
            int next = data[(index + 1)..].IndexOf(needle);
            index = next < 0 ? -1 : index + 1 + next;
        }
        return count;
    }
}

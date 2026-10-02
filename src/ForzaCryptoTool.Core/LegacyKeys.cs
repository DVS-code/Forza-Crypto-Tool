using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ForzaCryptoTool;

internal enum LegacyGame { FH5_v1_619_349_0, FH5_v1_614_70_0, FH5, FH4, FM7, FH3Dev, FH3, FM6Apex }

internal enum LegacyKeyType { Profile, Reward, GameDB, ConfigFile, File, SFS, Photo, Dynamic }

internal enum LegacyContainer
{
    Horizon,

    Motorsport,
}

internal sealed class LegacyTableSet
{
    private readonly Lazy<TfitInverse> _encryptor;

    public LegacyTableSet(string name, TfitTables encryption, TfitTables decryption, TfitTables mac)
    {
        Name = name;
        Encryption = encryption;
        Decryption = decryption;
        Mac = mac;
        _encryptor = new Lazy<TfitInverse>(() => new TfitInverse(decryption));
    }

    public string Name { get; }

    public TfitTables Encryption { get; }
    public TfitTables Decryption { get; }
    public TfitTables Mac { get; }

    public TfitInverse Encryptor => _encryptor.Value;
}

internal sealed class LegacyContext
{
    public required LegacyGame Game { get; init; }
    public required LegacyKeyType KeyType { get; init; }
    public required LegacyContainer Container { get; init; }
    public required int BlockSize { get; init; }
    public required LegacyTableSet Tables { get; init; }
    public required uint[] DecryptionKeys { get; init; }
    public required uint[] MacKeys { get; init; }

    public uint[]? EncryptionKeys { get; init; }

    public LegacyDbScramble? Scramble { get; init; }

    public int HeaderSize => Container == LegacyContainer.Horizon ? 36 : 32;
    public string Label => $"{LegacyKeyStore.ShortName(Game)} {KeyType}";
}

internal static class LegacyKeyStore
{
    private const string ResourceName = "ForzaCryptoTool.LegacyTfit.bin";
    private const int FlagHasEncryptionKeys = 1;
    private const int FlagDbScramble = 2;

    private static readonly Lazy<IReadOnlyList<LegacyContext>> Loaded = new(Load);

    public static IReadOnlyList<LegacyContext> Contexts => Loaded.Value;

    public static LegacyContext? Find(LegacyGame game, LegacyKeyType keyType)
        => Contexts.FirstOrDefault(c => c.Game == game && c.KeyType == keyType);

    public static IEnumerable<LegacyKeyType> KeyTypes(LegacyGame game)
        => Contexts.Where(c => c.Game == game).Select(c => c.KeyType);

    public static LegacyKeyType DefaultKeyType(LegacyGame game)
        => Find(game, LegacyKeyType.File) is not null ? LegacyKeyType.File
         : Find(game, LegacyKeyType.ConfigFile) is not null ? LegacyKeyType.ConfigFile
         : KeyTypes(game).First();

    public static string ShortName(LegacyGame game) => game switch
    {
        LegacyGame.FH5_v1_619_349_0 => "FH5_v1.619.349.0",
        LegacyGame.FH5_v1_614_70_0 => "FH5_v1.614.70.0",
        _ => game.ToString(),
    };

    public static string FullName(LegacyGame game) => game switch
    {
        LegacyGame.FH5_v1_619_349_0 => "Forza Horizon 5 v1.619.349.0",
        LegacyGame.FH5_v1_614_70_0 => "Forza Horizon 5 v1.614.70.0",
        LegacyGame.FH5 => "Forza Horizon 5",
        LegacyGame.FH4 => "Forza Horizon 4",
        LegacyGame.FM7 => "Forza Motorsport 7",
        LegacyGame.FH3Dev => "Forza Horizon 3 v1.0.37.2 \"OpusDev\"",
        LegacyGame.FH3 => "Forza Horizon 3",
        LegacyGame.FM6Apex => "Forza Motorsport 6: Apex",
        _ => game.ToString(),
    };

    public static bool TryParseGame(string text, out LegacyGame game)
    {
        foreach (var candidate in Enum.GetValues<LegacyGame>())
        {
            if (string.Equals(ShortName(candidate), text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                game = candidate;
                return true;
            }
        }
        game = default;
        return false;
    }

    public static bool TryParseKeyType(string text, out LegacyKeyType keyType)
        => Enum.TryParse(text, ignoreCase: true, out keyType) && Enum.IsDefined(keyType);

    private static IReadOnlyList<LegacyContext> Load()
    {
        using var stream = typeof(LegacyKeyStore).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Legacy key resource is missing from this build.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);

        var span = data.AsSpan();
        if (!span[..4].SequenceEqual("FCTL"u8) || BinaryPrimitives.ReadUInt32LittleEndian(span[4..]) != 1)
            throw new InvalidDataException("Legacy key resource has an unknown format.");
        int offset = 8;

        var tableSets = new LegacyTableSet[span[offset++]];
        for (int i = 0; i < tableSets.Length; i++)
        {
            int nameLength = span[offset++];
            string name = System.Text.Encoding.ASCII.GetString(span.Slice(offset, nameLength));
            offset += nameLength;
            var encryption = ReadTables(span, ref offset, TfitTables.Direction.Encrypt);
            var decryption = ReadTables(span, ref offset, TfitTables.Direction.Decrypt);

            var mac = ReadTables(span, ref offset, TfitTables.Direction.Encrypt);
            tableSets[i] = new LegacyTableSet(name, encryption, decryption, mac);
        }

        var crcMaps = new byte[span[offset++]][];
        for (int i = 0; i < crcMaps.Length; i++)
        {
            crcMaps[i] = span.Slice(offset, 256).ToArray();
            offset += 256;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
        offset += 2;
        var contexts = new List<LegacyContext>(count);
        for (int i = 0; i < count; i++)
        {
            var game = (LegacyGame)span[offset];
            var keyType = (LegacyKeyType)span[offset + 1];
            var container = (LegacyContainer)span[offset + 2];
            int blockSize = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 3)..]);
            var tables = tableSets[span[offset + 7]];
            int flags = span[offset + 8];
            uint seed = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 9)..]);
            var crcMap = crcMaps.Length > 0 ? crcMaps[span[offset + 13]] : null;
            offset += 14;

            var decryptionKeys = ReadKeys(span, ref offset, tables.Decryption.Rounds);
            var macKeys = ReadKeys(span, ref offset, tables.Mac.Rounds);
            var encryptionKeys = (flags & FlagHasEncryptionKeys) != 0
                ? ReadKeys(span, ref offset, tables.Encryption.Rounds)
                : null;

            contexts.Add(new LegacyContext
            {
                Game = game,
                KeyType = keyType,
                Container = container,
                BlockSize = blockSize,
                Tables = tables,
                DecryptionKeys = decryptionKeys,
                MacKeys = macKeys,
                EncryptionKeys = encryptionKeys,
                Scramble = (flags & FlagDbScramble) != 0 ? new LegacyDbScramble(seed, crcMap!) : null,
            });
        }

        if (offset != span.Length)
            throw new InvalidDataException("Legacy key resource has trailing data.");
        return contexts;
    }

    private static TfitTables ReadTables(ReadOnlySpan<byte> span, ref int offset, TfitTables.Direction direction)
    {
        int unique = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
        offset += 2;
        var pool = new uint[unique][];
        for (int i = 0; i < unique; i++)
        {
            pool[i] = ReadWords(span.Slice(offset, 1024));
            offset += 1024;
        }

        int slots = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
        offset += 2;
        var tables = new uint[slots][];
        for (int i = 0; i < slots; i++)
            tables[i] = pool[span[offset + i]];
        offset += slots;
        return new TfitTables(tables, direction);
    }

    private static uint[] ReadKeys(ReadOnlySpan<byte> span, ref int offset, int rounds)
    {
        var keys = ReadWords(span.Slice(offset, rounds * 16));
        offset += rounds * 16;
        return keys;
    }

    private static uint[] ReadWords(ReadOnlySpan<byte> bytes)
    {
        var words = MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
        if (!BitConverter.IsLittleEndian)
            BinaryPrimitives.ReverseEndianness(words, words);
        return words;
    }
}

internal sealed class LegacyDbScramble
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private readonly uint _seed;
    private readonly byte[] _crcMap;

    public LegacyDbScramble(uint seed, byte[] crcMap)
    {
        _seed = seed;
        _crcMap = crcMap;
    }

    public void Apply(long offset, Span<byte> data)
    {
        unchecked
        {
            uint step = _seed + (_seed + 1) * (uint)(offset / 4);
            for (int i = 0; i < data.Length; i += 4)
            {
                uint hash = Hash(step);
                for (int j = i; j < i + 4 && j < data.Length; j++)
                {
                    data[j] ^= (byte)hash;
                    hash >>= 8;
                }
                step += _seed + 1;
            }
        }
    }

    private uint Hash(uint value)
    {
        uint result = 0xFFFFFFFF;
        for (int i = 0; i < 4; i++)
        {
            byte low = (byte)(result ^ _crcMap[(byte)(value >> (8 * i))]);
            result = result >> 8 ^ Crc32Table[low];
        }
        return ~result;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ c >> 1 : c >> 1;
            table[n] = c;
        }
        return table;
    }
}

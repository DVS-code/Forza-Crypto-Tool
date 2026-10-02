using System.Buffers.Binary;
using System.Numerics;

namespace ForzaCryptoTool;

internal sealed class TfitTables
{
    public enum Direction { Encrypt, Decrypt }

    private static readonly byte[] PlainOrder = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
    private static readonly byte[] EncryptOrder = { 0, 5, 10, 15, 3, 4, 9, 14, 2, 7, 8, 13, 1, 6, 11, 12 };
    private static readonly byte[] DecryptOrder = { 0, 7, 10, 13, 1, 4, 11, 14, 2, 5, 8, 15, 3, 6, 9, 12 };

    private readonly uint[][] _tables;
    private readonly byte[] _innerOrder;

    public int Rounds { get; }

    public TfitTables(uint[][] tables, Direction direction)
    {
        if (tables.Length == 0 || tables.Length % 16 != 0)
            throw new ArgumentException("Expected 16 tables per round.", nameof(tables));
        _tables = tables;
        _innerOrder = direction == Direction.Encrypt ? EncryptOrder : DecryptOrder;
        Rounds = tables.Length / 16;
    }

    public ReadOnlySpan<byte> Order(int round)
        => round < 2 || round == Rounds - 1 ? PlainOrder : _innerOrder;

    public uint[] Table(int round, int stateByte) => _tables[round * 16 + stateByte];

    public void ProcessBlock(ReadOnlySpan<uint> keys, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Span<byte> state = stackalloc byte[16];
        source[..16].CopyTo(state);
        Span<uint> words = stackalloc uint[4];

        for (int round = 0; round < Rounds; round++)
        {
            var order = Order(round);
            int tableBase = round * 16;
            for (int word = 0; word < 4; word++)
            {
                int a = order[word * 4], b = order[word * 4 + 1], c = order[word * 4 + 2], d = order[word * 4 + 3];
                words[word] = _tables[tableBase + a][state[a]]
                            ^ _tables[tableBase + b][state[b]]
                            ^ _tables[tableBase + c][state[c]]
                            ^ _tables[tableBase + d][state[d]]
                            ^ keys[round * 4 + word];
            }
            for (int word = 0; word < 4; word++)
                BinaryPrimitives.WriteUInt32LittleEndian(state[(word * 4)..], words[word]);
        }

        state.CopyTo(destination);
    }
}

internal sealed class TfitInverse
{
    private readonly TfitTables _forward;
    private readonly uint[] _constant;
    private readonly uint[][] _project;
    private readonly byte[][] _lookup;

    public TfitInverse(TfitTables forward)
    {
        _forward = forward;
        int words = forward.Rounds * 4;
        _constant = new uint[words];
        _project = new uint[words * 4][];
        _lookup = new byte[words * 4][];

        for (int round = 0; round < forward.Rounds; round++)
        {
            var order = forward.Order(round);
            for (int word = 0; word < 4; word++)
                BuildWord(round, word, order.Slice(word * 4, 4));
        }
    }

    private void BuildWord(int round, int word, ReadOnlySpan<byte> stateBytes)
    {
        int index = round * 4 + word;
        var tables = new uint[4][];
        var basis = new uint[32];
        uint constant = 0;
        Span<uint> pivots = stackalloc uint[32];

        for (int slot = 0; slot < 4; slot++)
        {
            var table = tables[slot] = _forward.Table(round, stateBytes[slot]);
            constant ^= table[0];

            pivots.Clear();
            int found = 0;
            for (int x = 1; x < 256 && found < 8; x++)
            {
                uint difference = table[x] ^ table[0];
                uint reduced = difference;
                while (reduced != 0)
                {
                    int top = BitOperations.Log2(reduced);
                    if (pivots[top] == 0) { pivots[top] = reduced; break; }
                    reduced ^= pivots[top];
                }
                if (reduced != 0) basis[slot * 8 + found++] = difference;
            }
            if (found != 8)
                throw new InvalidDataException("Legacy cipher table is not an 8-dimensional affine map.");
        }

        var rows = new uint[32];
        var inverse = new uint[32];
        for (int column = 0; column < 32; column++)
            for (int bit = 0; bit < 32; bit++)
                if ((basis[column] >> bit & 1) != 0) rows[bit] |= 1u << column;
        for (int bit = 0; bit < 32; bit++) inverse[bit] = 1u << bit;

        for (int column = 0; column < 32; column++)
        {
            int pivot = column;
            while (pivot < 32 && (rows[pivot] >> column & 1) == 0) pivot++;
            if (pivot == 32)
                throw new InvalidDataException("Legacy cipher round is not invertible.");
            (rows[column], rows[pivot]) = (rows[pivot], rows[column]);
            (inverse[column], inverse[pivot]) = (inverse[pivot], inverse[column]);
            for (int other = 0; other < 32; other++)
            {
                if (other == column || (rows[other] >> column & 1) == 0) continue;
                rows[other] ^= rows[column];
                inverse[other] ^= inverse[column];
            }
        }

        for (int inputByte = 0; inputByte < 4; inputByte++)
        {
            var projection = _project[index * 4 + inputByte] = new uint[256];
            for (int coordinate = 0; coordinate < 32; coordinate++)
            {
                uint mask = inverse[coordinate] >> (inputByte * 8) & 0xFF;
                if (mask == 0) continue;
                for (int value = 1; value < 256; value++)
                    if ((BitOperations.PopCount(mask & (uint)value) & 1) != 0)
                        projection[value] |= 1u << coordinate;
            }
        }

        for (int slot = 0; slot < 4; slot++)
        {
            var lookup = _lookup[index * 4 + slot] = new byte[256];
            var seen = new bool[256];
            for (int x = 0; x < 256; x++)
            {
                uint coordinates = Project(index, tables[slot][x] ^ tables[slot][0]);
                if ((coordinates & ~(0xFFu << (slot * 8))) != 0)
                    throw new InvalidDataException("Legacy cipher table leaves its own subspace.");
                int own = (int)(coordinates >> (slot * 8) & 0xFF);
                if (seen[own])
                    throw new InvalidDataException("Legacy cipher table is not a bijection.");
                seen[own] = true;
                lookup[own] = (byte)x;
            }
        }

        _constant[index] = constant;
    }

    private uint Project(int index, uint value)
        => _project[index * 4][value & 0xFF]
         ^ _project[index * 4 + 1][value >> 8 & 0xFF]
         ^ _project[index * 4 + 2][value >> 16 & 0xFF]
         ^ _project[index * 4 + 3][value >> 24];

    public void ProcessBlock(ReadOnlySpan<uint> keys, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Span<byte> state = stackalloc byte[16];
        Span<byte> previous = stackalloc byte[16];
        source[..16].CopyTo(state);

        for (int round = _forward.Rounds - 1; round >= 0; round--)
        {
            var order = _forward.Order(round);
            for (int word = 0; word < 4; word++)
            {
                int index = round * 4 + word;
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(state[(word * 4)..]) ^ keys[index] ^ _constant[index];
                uint coordinates = Project(index, value);
                for (int slot = 0; slot < 4; slot++)
                    previous[order[word * 4 + slot]] = _lookup[index * 4 + slot][coordinates >> (slot * 8) & 0xFF];
            }
            previous.CopyTo(state);
        }

        state.CopyTo(destination);
    }
}

internal static class TfitMac
{
    public const int Size = 16;

    public static void Compute(TfitTables cipher, ReadOnlySpan<uint> keys, ReadOnlySpan<byte> data, Span<byte> mac)
    {
        if (data.IsEmpty) throw new ArgumentException("Nothing to authenticate.", nameof(data));

        Span<byte> subkey = stackalloc byte[16];
        Span<byte> block = stackalloc byte[16];
        Span<byte> chain = stackalloc byte[16];

        block.Clear();
        cipher.ProcessBlock(keys, block, subkey);
        ShiftSubkey(subkey);

        int partial = data.Length % 16;
        int lastStart = partial == 0 ? data.Length - 16 : data.Length - partial;

        chain.Clear();
        for (int offset = 0; offset < lastStart; offset += 16)
        {
            for (int i = 0; i < 16; i++) block[i] = (byte)(data[offset + i] ^ chain[i]);
            cipher.ProcessBlock(keys, block, chain);
        }

        if (partial == 0)
        {
            data.Slice(lastStart, 16).CopyTo(block);
        }
        else
        {
            block.Clear();
            data[lastStart..].CopyTo(block);
            block[partial] = 0x80;
            ShiftSubkey(subkey);
        }

        for (int i = 0; i < 16; i++) block[i] ^= (byte)(chain[i] ^ subkey[i]);
        cipher.ProcessBlock(keys, block, mac);
    }

    private static void ShiftSubkey(Span<byte> value)
    {
        bool carry = (value[0] & 0x80) != 0;
        for (int i = 0; i < 15; i++)
            value[i] = (byte)(value[i] << 1 | value[i + 1] >> 7);
        value[15] = (byte)(value[15] << 1 ^ (carry ? 0x87 : 0));
    }
}

using System;
using System.Numerics;


namespace Halina.Core;  
public interface IHashFunction<THash, TData>
{
    THash ComputeHash(TData data);
}


public struct TabulationHash: IHashFunction<ulong, ulong>
{
    private readonly ulong[][] _tables;

    public TabulationHash(int seed)
    {
        var random = new Random(seed);
        _tables = new ulong[8][];
        var buffer = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            _tables[i] = new ulong[256];
            for (int j = 0; j < 256; j++)
            {
                random.NextBytes(buffer);
                _tables[i][j] = BitConverter.ToUInt64(buffer, 0);
            }
        }
    }

    public ulong ComputeHash(ulong data)
    {
        ulong h = 0;
        for (int i = 0; i < 8; i++)
        {
            h ^= _tables[i][(byte)(data >> (i * 8))];
        }
        return h;
    }
}

struct RollingHash<THash,TFragment, TFragmentHashFunction> : IHashFunction<THash,TFragment[]>
    where TFragmentHashFunction : struct, IHashFunction<THash, TFragment>
    where THash : IXorType<THash>
{
    private readonly TFragmentHashFunction _fragmentHashFunction;
    public RollingHash(int fragmentCount)
    {
        _fragmentHashFunction = new TFragmentHashFunction();
    }

    public THash ComputeHash(TFragment[] data)
    {
        THash hash = default!;
        for (int i = 0; i < data.Length; i++)
        {
            var fragmentHash = _fragmentHashFunction.ComputeHash(data[i]);
            hash = hash.Xor(fragmentHash);
        }
        return hash;
    }

    public THash RollHash(THash previousHash, TFragment oldFragment, TFragment newFragment)
    {
        var oldFragmentHash = _fragmentHashFunction.ComputeHash(oldFragment);
        var newFragmentHash = _fragmentHashFunction.ComputeHash(newFragment);
        var newHash = previousHash.Xor(oldFragmentHash).Xor(newFragmentHash);
        return newHash;
    }
}

public struct KmerTabulationHash : IHashFunction<ulong, Kmer>
{
    private readonly ulong[] _table;

    public KmerTabulationHash(int seed)
    {
        var random = new Random(seed);
        _table = new ulong[256];
        var buffer = new byte[8];
        for (int j = 0; j < 256; j++)
        {
            random.NextBytes(buffer);
            _table[j] = BitConverter.ToUInt64(buffer, 0);
        }
    }

    public ulong ComputeHash(Kmer data)
    {
        ulong h = 0;
        var bytes = data.GetBytes();
        int len = data.Length;
        int limit = len - 4;

        if (limit < 0) return 0;

        int i = 0;
        // Safe loop limit: ensure i+1 is valid and 4*i+3 <= limit
        int safeLimit = Math.Min((limit - 3) >> 2, bytes.Length - 2);

        for (; i <= safeLimit; i++)
        {
            byte b1 = bytes[i];
            byte b2 = bytes[i + 1];
            int k = i * 4;

            h ^= BitOperations.RotateLeft(_table[b1], k);
            h ^= BitOperations.RotateLeft(_table[(byte)((b1 << 2) | (b2 >> 6))], k + 1);
            h ^= BitOperations.RotateLeft(_table[(byte)((b1 << 4) | (b2 >> 4))], k + 2);
            h ^= BitOperations.RotateLeft(_table[(byte)((b1 << 6) | (b2 >> 2))], k + 3);
        }

        for (int k = i * 4; k <= limit; k++)
        {
            int byteIdx = k >> 2;
            int bitShift = (k & 3) << 1;
            byte val = bitShift == 0 
                ? bytes[byteIdx] 
                : (byte)((bytes[byteIdx] << bitShift) | (bytes[byteIdx + 1] >> (8 - bitShift)));
            h ^= BitOperations.RotateLeft(_table[val], k);
        }
        return h;
    }

    public ulong RollHash(ulong currentHash, byte oldFirstByte, byte newLastByte, int kmerLength)
    {
        // Rolling Left:
        // 1. Remove the first term: Rot(T[oldFirstByte], 0)
        // 2. Rotate the entire hash left by 1 (equivalent to decrementing index of all remaining terms)
        //    Note: RotLeft(x, -1) is RotRight(x, 1)
        // 3. Add the new term at the end: Rot(T[newLastByte], kmerLength - 4)
        
        ulong termToRemove = _table[oldFirstByte]; // Rotated by 0 is just the value
        ulong rotatedHash = BitOperations.RotateRight(currentHash ^ termToRemove, 1);
        ulong termToAdd = BitOperations.RotateLeft(_table[newLastByte], kmerLength - 4);
        return rotatedHash ^ termToAdd;
    }

    public ulong RollHashReverse(ulong currentHash, byte oldLastByte, byte newFirstByte, int kmerLength)
    {
        // Rolling Right (Prepend):
        // 1. Remove the last term: Rot(T[oldLastByte], kmerLength - 4)
        // 2. Rotate the entire hash left by 1 (incrementing index of all remaining terms)
        // 3. Add the new term at the start: Rot(T[newFirstByte], 0)
        ulong termToRemove = BitOperations.RotateLeft(_table[oldLastByte], kmerLength - 4);
        ulong rotatedHash = BitOperations.RotateLeft(currentHash ^ termToRemove, 1);
        ulong termToAdd = _table[newFirstByte]; // Rotated by 0
        return rotatedHash ^ termToAdd;
    }

    public ulong SubstituteNucleotideHash(ulong currentHash, Kmer kmer, int index, Nucleotide newNuc)
    {
        int len = kmer.Length;
        int limit = len - 4;
        if (limit < 0) return currentHash;

        byte[] bytes = kmer.GetBytes();
        
        // Get old nucleotide
        int bitOffset = index * 2;
        int byteIndex = bitOffset / 8;
        int shiftNuc = 6 - (bitOffset % 8);
        Nucleotide oldNuc = (Nucleotide)((bytes[byteIndex] >> shiftNuc) & 3);

        if (oldNuc == newNuc) return currentHash;

        ulong h = currentHash;
        int startK = Math.Max(0, index - 3);
        int endK = Math.Min(limit, index);

        for (int k = startK; k <= endK; k++)
        {
            int byteIdx = k >> 2;
            int bitShift = (k & 3) << 1;
            byte oldVal = bitShift == 0 
                ? bytes[byteIdx] 
                : (byte)((bytes[byteIdx] << bitShift) | (bytes[byteIdx + 1] >> (8 - bitShift)));

            int nucOffsetInWindow = index - k;
            int shift = 6 - (nucOffsetInWindow * 2);
            
            byte diff = (byte)(((byte)oldNuc ^ (byte)newNuc) << shift);
            byte newVal = (byte)(oldVal ^ diff);

            h ^= BitOperations.RotateLeft(_table[oldVal], k);
            h ^= BitOperations.RotateLeft(_table[newVal], k);
        }
        return h;
    }
}

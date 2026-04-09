using System;
using System.Collections.Generic;

namespace Halina.Core;

public sealed class HashSetPredictor : ITable<KmerData>
{
    private static readonly Nucleotide[] Nucleotides = new[]
    {
        Nucleotide.A,
        Nucleotide.C,
        Nucleotide.G,
        Nucleotide.T
    };

    private readonly Tables<UlongData> _hashTable;
    private readonly KmerTabulationHash _hasher;
    private readonly HashSet<ulong> _remainingHashes = new();
    private readonly HashSet<KmerData> _encodedElements = new(KmerDataHashComparer.Instance);
    private bool _isDecodePhase;

    public HashSetPredictor(int kmerLength, int tableSize, int seed = 123)
    {
        if (kmerLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kmerLength), "K-mer length must be greater than zero.");
        }

        if (tableSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tableSize), "Table size must be greater than zero.");
        }

        _hashTable = IBLTFactory.GetStandardIBLT(3, tableSize);
        _hasher = new KmerTabulationHash(seed);
    }

    public void Encode(Buffer<KmerData> data)
    {
        if (_isDecodePhase)
        {
            foreach (var item in data)
            {
                Toggle(_remainingHashes, item.Hash);
                Toggle(_encodedElements, item);
            }

            return;
        }

        var hashBuffer = Buffer<UlongData>.Rent(Math.Max(1, data.Length));
        try
        {
            foreach (var item in data)
            {
                hashBuffer.Add(new UlongData(item.Hash));
            }

            if (hashBuffer.Length > 0)
            {
                _hashTable.Encode(hashBuffer);
            }
        }
        finally
        {
            hashBuffer.Return();
        }
    }

    public void ToDecode()
    {
        if (_isDecodePhase)
        {
            return;
        }

        _hashTable.ToDecode();
        _remainingHashes.Clear();
        _encodedElements.Clear();

        var decodedHashes = _hashTable.Decode();
        try
        {
            foreach (var decoded in decodedHashes)
            {
                Toggle(_remainingHashes, decoded.Value);
            }
        }
        finally
        {
            decodedHashes.Return();
        }

        _isDecodePhase = true;
    }

    public Buffer<KmerData> Decode()
    {
        if (!_isDecodePhase)
        {
            ToDecode();
        }

        var result = Buffer<KmerData>.Rent(Math.Max(1, _encodedElements.Count));
        if (_encodedElements.Count == 0)
        {
            return result;
        }

        var stack = new Stack<KmerData>(_encodedElements);
        var discovered = new HashSet<KmerData>(KmerDataHashComparer.Instance);
        _encodedElements.Clear();

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            TryExtend(current, forward: true, stack, discovered);
            TryExtend(current, forward: false, stack, discovered);
        }

        foreach (var item in discovered)
        {
            result.Add(item);
        }

        return result;
    }

    public TableState GetState()
    {
        if (!_isDecodePhase)
        {
            return _hashTable.GetState();
        }

        return _remainingHashes.Count > 0 || _encodedElements.Count > 0
            ? TableState.Decoding
            : TableState.Finished;
    }

    private void TryExtend(KmerData current, bool forward, Stack<KmerData> stack, HashSet<KmerData> discovered)
    {
        foreach (var nucleotide in Nucleotides)
        {
            var candidate = forward
                ? KmerDataGenerator.RollingUpdate(current, nucleotide, _hasher)
                : KmerDataGenerator.RollingUpdateReverse(current, nucleotide, _hasher);

            if (!_remainingHashes.Remove(candidate.Hash))
            {
                continue;
            }

            if (discovered.Add(candidate))
            {
                stack.Push(candidate);
            }

            break;
        }
    }

    private static bool Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Add(value))
        {
            set.Remove(value);
            return false;
        }

        return true;
    }

    private sealed class KmerDataHashComparer : IEqualityComparer<KmerData>
    {
        public static KmerDataHashComparer Instance { get; } = new();

        public bool Equals(KmerData x, KmerData y) => x.Hash == y.Hash;

        public int GetHashCode(KmerData obj) => obj.Hash.GetHashCode();
    }
}

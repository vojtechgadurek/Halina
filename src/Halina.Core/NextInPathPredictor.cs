using System;
using System.Collections.Generic;

namespace Halina.Core;

public sealed class NextInPathPredictor : ITable<KmerData>
{
    private static readonly Nucleotide[] Nucleotides = new[]
    {
        Nucleotide.A,
        Nucleotide.C,
        Nucleotide.G,
        Nucleotide.T
    };

    private readonly KmerTabulationHash _hasher;
    private readonly Random _random;
    private readonly int _selectionDivisor;
    private readonly HashSet<KmerData> _trackedElements = new(KmerDataHashComparer.Instance);
    private readonly List<KmerData> _replayElements = new();
    private bool _isDecodePhase;
    private bool _hasPendingGeneration;

    public NextInPathPredictor(int seed = 123, int selectionDivisor = 3)
    {
        if (selectionDivisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionDivisor), "Selection divisor must be greater than zero.");
        }

        _hasher = new KmerTabulationHash(seed);
        _random = new Random(seed);
        _selectionDivisor = selectionDivisor;
    }

    public void Encode(Buffer<KmerData> data)
    {
        if (!_isDecodePhase)
        {
            return;
        }

        foreach (var item in data)
        {
            Toggle(_trackedElements, item);
            _hasPendingGeneration = true;
        }
    }

    public void ToDecode()
    {
        if (_isDecodePhase)
        {
            return;
        }

        _trackedElements.Clear();
        _replayElements.Clear();
        _hasPendingGeneration = false;
        _isDecodePhase = true;
    }

    public Buffer<KmerData> Decode()
    {
        if (!_isDecodePhase)
        {
            ToDecode();
        }

        if (_replayElements.Count > 0)
        {
            return ReplayAndClear();
        }

        if (!_hasPendingGeneration || _trackedElements.Count == 0)
        {
            return Buffer<KmerData>.Rent(0);
        }

        _hasPendingGeneration = false;
        GenerateGuesses();

        var result = Buffer<KmerData>.Rent(Math.Max(1, _replayElements.Count));
        foreach (var item in _replayElements)
        {
            result.Add(item);
        }

        return result;
    }

    public TableState GetState()
    {
        if (!_isDecodePhase)
        {
            return TableState.Finished;
        }

        return _replayElements.Count > 0 || (_hasPendingGeneration && _trackedElements.Count > 0)
            ? TableState.Decoding
            : TableState.Finished;
    }

    private Buffer<KmerData> ReplayAndClear()
    {
        var result = Buffer<KmerData>.Rent(Math.Max(1, _replayElements.Count));
        foreach (var item in _replayElements)
        {
            Toggle(_trackedElements, item);
            result.Add(item);
        }

        _replayElements.Clear();
        return result;
    }

    private void GenerateGuesses()
    {
        _replayElements.Clear();
        var candidates = new List<KmerData>(_trackedElements);
        Shuffle(candidates);

        int selectionCount = Math.Max(1, (candidates.Count + _selectionDivisor - 1) / _selectionDivisor);
        var guessedSet = new HashSet<KmerData>(KmerDataHashComparer.Instance);

        for (int i = 0; i < selectionCount && i < candidates.Count; i++)
        {
            var guess = CreateRandomNeighbor(candidates[i]);
            if (!guessedSet.Add(guess))
            {
                continue;
            }

            Toggle(_trackedElements, guess);
            _replayElements.Add(guess);
        }
    }

    private KmerData CreateRandomNeighbor(KmerData source)
    {
        bool forward = _random.Next(2) == 0;
        var nucleotide = Nucleotides[_random.Next(Nucleotides.Length)];

        return forward
            ? KmerDataGenerator.RollingUpdate(source, nucleotide, _hasher)
            : KmerDataGenerator.RollingUpdateReverse(source, nucleotide, _hasher);
    }

    private void Shuffle(List<KmerData> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int swapIndex = _random.Next(i + 1);
            (items[i], items[swapIndex]) = (items[swapIndex], items[i]);
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

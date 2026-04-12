using System;
using System.Collections.Generic;

namespace Halina.Core;

public sealed class NextInPathOnlyOnce : ITable<KmerData>
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
    private readonly HashSet<KmerData> _pendingElements = new(KmerDataHashComparer.Instance);
    private readonly HashSet<KmerData> _processedSourceElements = new(KmerDataHashComparer.Instance);
    private readonly HashSet<KmerData> _emittedGuessElements = new(KmerDataHashComparer.Instance);
    private bool _isDecodePhase;

    public NextInPathOnlyOnce(int seed = 123, int selectionDivisor = 3)
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
            if (_processedSourceElements.Contains(item))
            {
                continue;
            }

            Toggle(_pendingElements, item);
        }
    }

    public void ToDecode()
    {
        if (_isDecodePhase)
        {
            return;
        }

        _pendingElements.Clear();
        _processedSourceElements.Clear();
        _emittedGuessElements.Clear();
        _isDecodePhase = true;
    }

    public Buffer<KmerData> Decode()
    {
        if (!_isDecodePhase)
        {
            ToDecode();
        }

        if (_pendingElements.Count == 0)
        {
            return Buffer<KmerData>.Rent(0);
        }

        return GenerateGuesses();
    }

    public TableState GetState()
    {
        if (!_isDecodePhase)
        {
            return TableState.Finished;
        }

        return _pendingElements.Count > 0 ? TableState.Decoding : TableState.Finished;
    }

    private Buffer<KmerData> GenerateGuesses()
    {
        var candidates = new List<KmerData>(_pendingElements);
        Shuffle(candidates);

        int selectionCount = Math.Max(1, (candidates.Count + _selectionDivisor - 1) / _selectionDivisor);
        var result = Buffer<KmerData>.Rent(Math.Max(1, selectionCount));
        var guessedThisStep = new HashSet<KmerData>(KmerDataHashComparer.Instance);

        for (int i = 0; i < selectionCount && i < candidates.Count; i++)
        {
            var source = candidates[i];
            _pendingElements.Remove(source);
            _processedSourceElements.Add(source);

            var guess = CreateRandomNeighbor(source);
            if (!guessedThisStep.Add(guess))
            {
                continue;
            }

            if (!_emittedGuessElements.Add(guess))
            {
                continue;
            }

            result.Add(guess);
        }

        return result;
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

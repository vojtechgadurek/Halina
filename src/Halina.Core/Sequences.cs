using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Halina.Core;

public class Sequence
{
    public Nucleotide[] Nucleotides { get; }
    public int BaseOffset { get; }
    public int SetId { get; }

    public Sequence(Nucleotide[] nucleotides, int baseOffset, int setId)
    {
        Nucleotides = nucleotides;
        BaseOffset = baseOffset;
        SetId = setId;
    }

    public IEnumerable<KmerData> GetKmers(int kmerLength, KmerTabulationHash hasher)
    {
        if (Nucleotides.Length < kmerLength) yield break;

        var initialSlice = new Nucleotide[kmerLength];
        Array.Copy(Nucleotides, 0, initialSlice, 0, kmerLength);
        var currentKmer = new Kmer(initialSlice);
        ulong currentHash = hasher.ComputeHash(currentKmer);

        var currentData = new KmerData
        {
            MetaData = new KmerMetaData { Index = BaseOffset, SetId = SetId },
            Hash = currentHash,
            Data = currentKmer
        };

        yield return currentData;

        for (int i = 1; i <= Nucleotides.Length - kmerLength; i++)
        {
            Nucleotide nextNuc = Nucleotides[i + kmerLength - 1];
            currentData = KmerDataGenerator.RollingUpdate(currentData, nextNuc, hasher);
            yield return currentData;
        }
    }
}

public record DoubleSequence(Sequence Seq1, Sequence Seq2);

public static class DatasetGenerator
{
    public static DoubleSequence Generate(int length, int offset1, int offset2, int setId1, int setId2, int seed)
    {
        var rng = new Random(seed);
        var nucleotides = new Nucleotide[length];
        for (int i = 0; i < length; i++)
        {
            nucleotides[i] = (Nucleotide)rng.Next(4);
        }

        var nucleotides2 = (Nucleotide[])nucleotides.Clone();
        int mid = length / 2;
        Nucleotide currentMid = nucleotides2[mid];
        Nucleotide newMid = (Nucleotide)(((int)currentMid + 1) % 4);
        nucleotides2[mid] = newMid;

        return new DoubleSequence(
            new Sequence(nucleotides, offset1, setId1),
            new Sequence(nucleotides2, offset2, setId2)
        );
    }
}

public class ContinuableSequence
{
    // Deque implemented over list
    private readonly List<Nucleotide?> _deque = new List<Nucleotide?>();
    
    public int StartOffset { get; private set; }
    public int EndOffset => StartOffset + _deque.Count;

    public ContinuableSequence(KmerData kmer)
    {
        StartOffset = kmer.MetaData.Index;
        for (int i = 0; i < kmer.Data.Length; i++)
        {
            _deque.Add(kmer.Data.GetNucleotide(i));
        }
    }

    public void Add(ContinuableSequence other)
    {
        // Prepend nulls if needed
        if (other.StartOffset < StartOffset)
        {
            int diff = StartOffset - other.StartOffset;
            _deque.InsertRange(0, Enumerable.Repeat((Nucleotide?)null, diff));
            StartOffset = other.StartOffset;
        }

        // Append nulls if needed
        if (other.EndOffset > EndOffset)
        {
            int diff = other.EndOffset - EndOffset;
            _deque.AddRange(Enumerable.Repeat((Nucleotide?)null, diff));
        }

        // Merge and validate
        for (int i = 0; i < other._deque.Count; i++)
        {
            int absIndex = other.StartOffset + i;
            int localIndex = absIndex - StartOffset;
            
            var otherVal = other._deque[i];
            if (otherVal.HasValue)
            {
                if (_deque[localIndex].HasValue && _deque[localIndex] != otherVal)
                    throw new InvalidOperationException($"Conflict at index {absIndex}");
                _deque[localIndex] = otherVal;
            }
        }
    }
}

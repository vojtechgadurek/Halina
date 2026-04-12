using System;
using System.Collections.Generic;
using System.Linq;
using Halina.Core;
using Xunit;

namespace Halina.Tests;

public class NextInPathOnlyOnceTests
{
    [Fact]
    public void NextInPathOnlyOnce_EmitsGuessesOnceThenStopsWhenEverythingWasConsumed()
    {
        const int seed = 17;
        const int kmerLength = 4;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed);
        var predictor = new NextInPathOnlyOnce(seed, selectionDivisor: 1);

        predictor.ToDecode();
        Encode(predictor, kmers.Take(4));

        Assert.Equal(TableState.Decoding, predictor.GetState());

        var firstDecode = Decode(predictor);
        var secondDecode = Decode(predictor);

        Assert.NotEmpty(firstDecode);
        Assert.Empty(secondDecode);
        Assert.Equal(TableState.Finished, predictor.GetState());
    }

    [Fact]
    public void NextInPathOnlyOnce_IsDeterministicForSameSeed()
    {
        const int seed = 29;
        const int kmerLength = 4;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed);

        var predictorA = new NextInPathOnlyOnce(seed, selectionDivisor: 3);
        var predictorB = new NextInPathOnlyOnce(seed, selectionDivisor: 3);

        predictorA.ToDecode();
        predictorB.ToDecode();

        Encode(predictorA, kmers.Take(5));
        Encode(predictorB, kmers.Take(5));

        var decodedA = Decode(predictorA);
        var decodedB = Decode(predictorB);

        Assert.Equal(ToSequenceStrings(decodedA), ToSequenceStrings(decodedB));
    }

    private static List<KmerData> CreateKmers(string sequenceText, int kmerLength, int seed)
    {
        var sequence = new Sequence(
            sequenceText.Select(Kmer.CharToNucleotide).ToArray(),
            baseOffset: 0,
            setId: 1);
        var hasher = new KmerTabulationHash(seed);
        return sequence.GetKmers(kmerLength, hasher).ToList();
    }

    private static void Encode(ITable<KmerData> table, IEnumerable<KmerData> items)
    {
        var itemList = items.ToList();
        var buffer = Buffer<KmerData>.Rent(Math.Max(1, itemList.Count));
        try
        {
            foreach (var item in itemList)
            {
                buffer.Add(item);
            }

            table.Encode(buffer);
        }
        finally
        {
            buffer.Return();
        }
    }

    private static List<KmerData> Decode(ITable<KmerData> table)
    {
        var buffer = table.Decode();
        try
        {
            return buffer.ToList();
        }
        finally
        {
            buffer.Return();
        }
    }

    private static string[] ToSequenceStrings(IEnumerable<KmerData> kmers)
    {
        return kmers
            .Select(item => item.Data.ToString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
    }
}

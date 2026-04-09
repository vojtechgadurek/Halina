using System;
using System.Collections.Generic;
using System.Linq;
using Halina.Core;
using Xunit;

namespace Halina.Tests;

public class HashSetPredictorTests
{
    [Fact]
    public void Tables_CanComposeThreeSampledIbltsAndHashSetPredictor()
    {
        const int seed = 41;
        const int kmerLength = 4;
        const int samplingRatio = 3;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed);
        var pipeline = new TablesBuilder<KmerData>()
            .AddSwitch(
                item => (int)(item.Hash % (ulong)samplingRatio),
                KmerIBLTFactory.CreateKmerIBLT(3, kmerLength, 96),
                KmerIBLTFactory.CreateKmerIBLT(3, kmerLength, 96),
                KmerIBLTFactory.CreateKmerIBLT(3, kmerLength, 96))
            .Add(new HashSetPredictor(kmerLength, 96, seed))
            .WithDecodingControl(new TabuDecodingControl<KmerData>(3, data => data.Hash))
            .Build();

        Encode(pipeline, kmers);
        var decoded = Decode(pipeline);

        Assert.Equal(
            ToSequenceStrings(kmers),
            ToSequenceStrings(decoded));
    }

    [Fact]
    public void HashSetPredictor_PumpsNeighborsFromEncodedSeed()
    {
        const int seed = 17;
        const int kmerLength = 4;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed);
        var predictor = new HashSetPredictor(kmerLength, tableSize: 96, seed);

        Encode(predictor, kmers);
        predictor.ToDecode();

        Assert.Equal(TableState.Decoding, predictor.GetState());

        Encode(predictor, new[] { kmers[1] });
        var decoded = Decode(predictor);

        Assert.Equal(
            new[] { "ACGT", "GTGC", "TGCA" },
            ToSequenceStrings(decoded));

        Assert.Equal(TableState.Finished, predictor.GetState());
        Assert.Empty(Decode(predictor));
    }

    [Fact]
    public void HashSetPredictor_PostDecodeEncodeUsesSymmetricDifference()
    {
        const int seed = 29;
        const int kmerLength = 4;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed);
        var predictor = new HashSetPredictor(kmerLength, tableSize: 96, seed);

        Encode(predictor, kmers);
        predictor.ToDecode();

        Encode(predictor, new[] { kmers[1], kmers[1] });
        Assert.Empty(Decode(predictor));
        Assert.Equal(TableState.Decoding, predictor.GetState());

        Encode(predictor, new[] { kmers[1] });
        var decoded = Decode(predictor);

        Assert.Equal(
            new[] { "ACGT", "GTGC", "TGCA" },
            ToSequenceStrings(decoded));
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

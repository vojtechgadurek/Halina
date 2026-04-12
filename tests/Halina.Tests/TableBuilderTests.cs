using System;
using System.Collections.Generic;
using System.Linq;
using Halina.Core;
using Halina.Experiments;
using Xunit;

namespace Halina.Tests;

public class TableBuilderTests
{
    [Fact]
    public void TableBuilder_UlongPipelineMatchesFactoryBehavior()
    {
        const int totalTableSize = 90;
        ulong[] values = Enumerable.Range(1, 20).Select(value => (ulong)value).ToArray();

        var builderPipeline = BuildUlongPipeline(totalTableSize, 3);
        var factoryPipeline = IBLTFactory.GetStandardIBLT(3, totalTableSize);

        Encode(builderPipeline, values.Select(value => new UlongData(value)));
        Encode(factoryPipeline, values.Select(value => new UlongData(value)));

        var builderDecoded = Decode(builderPipeline).Select(item => item.Value).OrderBy(value => value).ToArray();
        var factoryDecoded = Decode(factoryPipeline).Select(item => item.Value).OrderBy(value => value).ToArray();

        Assert.Equal(values, builderDecoded);
        Assert.Equal(values, factoryDecoded);
        Assert.Equal(factoryDecoded, builderDecoded);
    }

    [Fact]
    public void TableBuilder_KmerPipelineMatchesFactoryBehavior()
    {
        const int seed = 23;
        const int kmerLength = 4;
        const int totalTableSize = 96;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed);

        var builderPipeline = BuildKmerPipeline(kmerLength, totalTableSize);
        var factoryPipeline = KmerIBLTFactory.CreateKmerIBLT(3, kmerLength, totalTableSize);

        Encode(builderPipeline, kmers);
        Encode(factoryPipeline, kmers);

        var builderDecoded = Decode(builderPipeline).Select(item => item.Data.ToString()).OrderBy(text => text, StringComparer.Ordinal).ToArray();
        var factoryDecoded = Decode(factoryPipeline).Select(item => item.Data.ToString()).OrderBy(text => text, StringComparer.Ordinal).ToArray();
        var expected = kmers.Select(item => item.Data.ToString()).OrderBy(text => text, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, builderDecoded);
        Assert.Equal(expected, factoryDecoded);
        Assert.Equal(factoryDecoded, builderDecoded);
    }

    [Fact]
    public void TableBuilder_PreservesMetadataPayloads()
    {
        const int seed = 31;
        const int kmerLength = 4;
        const int totalTableSize = 96;
        var kmers = CreateKmers("ACGTGCA", kmerLength, seed)
            .Select((item, index) => new KmerData
            {
                Hash = item.Hash,
                Data = item.Data,
                MetaData = new KmerMetaData
                {
                    Index = 100 + index,
                    SetId = 7,
                    MutationIndex = index + 1,
                    MutationValue = 20 + index
                }
            })
            .ToList();

        var pipeline = BuildKmerPipeline(kmerLength, totalTableSize);
        Encode(pipeline, kmers);
        var decoded = Decode(pipeline);

        var decodedByHash = new Dictionary<ulong, KmerData>();
        foreach (var item in decoded)
        {
            decodedByHash[item.Hash] = item;
        }
        foreach (var original in kmers)
        {
            Assert.True(decodedByHash.TryGetValue(original.Hash, out var recovered));
            Assert.Equal(original.MetaData.Index, recovered.MetaData.Index);
            Assert.Equal(original.MetaData.SetId, recovered.MetaData.SetId);
            Assert.Equal(original.MetaData.MutationIndex, recovered.MetaData.MutationIndex);
            Assert.Equal(original.MetaData.MutationValue, recovered.MetaData.MutationValue);
        }
    }

    [Fact]
    public void KmerExperiment_RunExperiment_CompletesWithBuilderPipeline()
    {
        const int kmerSize = 16;
        const int sequenceLength = 65;
        const int nSequences = 100;
        const int expectedItems = 10_000;

        var result = KmerExperiments.RunExperiment(
            kmerSize: kmerSize,
            nSequences: nSequences,
            sequenceLength: sequenceLength,
            k: 3,
            l: 2,
            seed: 11,
            maxDistance: 4);

        Assert.Equal(expectedItems, result.Result.TotalItems);
        Assert.Equal(result.Result.TotalItems, result.Result.CorrectlyRecovered + result.Result.NotRecovered);
        Assert.True(result.Result.FalsePositives >= 0);
    }

    private static Tables<UlongData> BuildUlongPipeline(int totalTableSize, int tableCount)
    {
        int childTableSize = totalTableSize / 3;
        var builder = new TablesBuilder<UlongData>()
            .WithDecodingControl(new TabuDecodingControl<UlongData>(3, data => data.Value));

        for (int i = 0; i < tableCount; i++)
        {
            var hash = new TabulationHash(i * 12345 + 6789);
            var indexer = new UlongIndexer { HashFunction = hash, Size = childTableSize };
            var pureTester = new UlongPureTester { HashFunction = hash, Size = childTableSize };

            builder.Add(
                new TableBuilder<UlongData, int>()
                    .WithSize(childTableSize)
                    .WithNullData(() => new UlongData(0))
                    .WithIndexer(indexer)
                    .WithPureTester(pureTester)
                    .Build());
        }

        return builder.Build();
    }

    private static Tables<KmerData> BuildKmerPipeline(int kmerLength, int totalTableSize)
    {
        int tableCount = 3;
        int childTableSize = totalTableSize / tableCount;
        var builder = new TablesBuilder<KmerData>()
            .WithDecodingControl(new TabuDecodingControl<KmerData>(3, data => data.Hash));

        for (int i = 0; i < tableCount; i++)
        {
            var hash = new TabulationHash(i * 9876 + 54321);
            var indexer = new KmerDataIndexer { HashFunction = hash, Size = childTableSize };
            var pureTester = new KmerDataPureTester { HashFunction = hash, Size = childTableSize };

            builder.Add(
                new TableBuilder<KmerData, int>()
                    .WithSize(childTableSize)
                    .WithNullData(() => new KmerData
                    {
                        MetaData = new KmerMetaData { Index = 0, SetId = 0, MutationIndex = 0, MutationValue = 0 },
                        Hash = 0,
                        Data = new Kmer(kmerLength)
                    })
                    .WithIndexer(indexer)
                    .WithPureTester(pureTester)
                    .Build());
        }

        return builder.Build();
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

    private static void Encode<TData>(ITable<TData> table, IEnumerable<TData> items)
    {
        var itemList = items.ToList();
        var buffer = Buffer<TData>.Rent(Math.Max(1, itemList.Count));
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

    private static List<TData> Decode<TData>(ITable<TData> table)
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
}

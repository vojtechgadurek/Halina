using Halina.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Halina.Experiments;

public record ExtendedExperimentArguments(
    int TableSize,
    int KmerSize,
    int NSequences,
    int SequenceLength,
    int K,
    int L,
    int SamplingStages,
    double ShrinkFactor,
    int MaxDistance,
    int Seed
);

public record ExtendedExperimentResult(
    string Version,
    ExtendedExperimentArguments Arguments,
    ExperimentOutcome Result
);

public static class HashSetPredictorExtended
{
    private static void AddToHashset<T>(HashSet<T> set, T item)
    {
        if (set.Contains(item))
        {
            set.Remove(item);
        }
        else
        {
            set.Add(item);
        }
    }

    public static ExtendedExperimentResult Run(
        int kmerSize,
        int nSequences,
        int sequenceLength,
        int k,
        int l,
        int samplingStages,
        double shrinkFactor,
        int seed = 123,
        int maxDistance = 0)
    {
         var swGen = Stopwatch.StartNew();
        // 1. Generate Data
        var hasher = new KmerTabulationHash(seed); // Must match generator's seed
        
        var allData = new List<KmerData>();
        for (int i = 0; i < nSequences; i++)
        {
            var doubleSeq = DatasetGenerator.Generate(sequenceLength, 0, 0, 1, 2, seed + i);
            allData.AddRange(doubleSeq.Seq1.GetKmers(kmerSize, hasher));
            allData.AddRange(doubleSeq.Seq2.GetKmers(kmerSize, hasher));
        }
        swGen.Stop();

        Console.WriteLine($"Generated {allData.Count} kmers from {nSequences} sequences of length {sequenceLength}.");

        int baseTableSize = allData.Count;
        double hashMagic = 1.5;
        var hashTable = IBLTFactory.GetStandardIBLT(3, (int)(baseTableSize * hashMagic));

        double compressedMagic = 1.3;
        int compressedTableSize = Math.Max(100, (int)(baseTableSize / Math.Max(1, l) * compressedMagic));
        var compressedTable = KmerIBLTFactory.CreateKmerIBLT(3, kmerSize, compressedTableSize);

        int stageCount = Math.Max(1, samplingStages);
        double shrink = Math.Max(1.0, shrinkFactor);
        var samplingStagesInfo = new List<SamplingStageInfo>(stageCount);


        var hashBuffer = Buffer<UlongData>.Rent(Math.Max(1, allData.Count));
        foreach (var item in allData)
        {
            hashBuffer.Add(new UlongData(item.Hash));
        }

        Console.WriteLine($"Encoding hash table with {hashBuffer.Length} items.");

        hashTable.Encode(hashBuffer);
        hashBuffer.Return();

        for (int stageIndex = 0; stageIndex < stageCount; stageIndex++)
        {
            int sampleInterval = CalculateSampleInterval(k, shrink, stageIndex);
            double stageMagic = 1.5;
            int stageTableSize = Math.Max(100, (int)(baseTableSize / Math.Max(1, sampleInterval) * stageMagic));
            var stageTable = KmerIBLTFactory.CreateKmerIBLT(3, kmerSize, stageTableSize);
            samplingStagesInfo.Add(new(stageTable, sampleInterval));
            EncodeSample(stageTable, allData, sampleInterval, Math.Max(1, baseTableSize));
            Console.WriteLine($"Stage {stageIndex + 1}: encoded {stageTableSize} cells with interval {sampleInterval}.");
        }

        

        var compressedBuffer = Buffer<KmerData>.Rent(Math.Max(1, allData.Count));
        foreach (var item in allData)
        {
            compressedBuffer.Add(item);
        }

        compressedTable.Encode(compressedBuffer);
        compressedBuffer.Return();

        var sw = Stopwatch.StartNew();

        var decodedHashesBuffer = hashTable.Decode();
        var recoveredHashes = new HashSet<ulong>();
        foreach (var decoded in decodedHashesBuffer)
        {
            AddToHashset(recoveredHashes, decoded.Value);
        }

        decodedHashesBuffer.Return();

        Console.WriteLine($"Recovered {recoveredHashes.Count} hashes from hash table.");

        var finalReconstructed = new HashSet<KmerData>();
        int totalSeeds = 0;

        for (int stageIndex = 0; stageIndex < samplingStagesInfo.Count; stageIndex++)
        {
            var stageInfo = samplingStagesInfo[stageIndex];

            if (stageIndex > 0 && finalReconstructed.Count > 0)
            {
                EncodeSample(stageInfo.Table, finalReconstructed, stageInfo.SampleInterval, Math.Max(1, baseTableSize));
            }

            var decodedStageBuffer = stageInfo.Table.Decode();
            var stageSeeds = new HashSet<KmerData>();
            foreach (var stageSeed in decodedStageBuffer)
            {
                AddToHashset(stageSeeds, stageSeed);
            }

            decodedStageBuffer.Return();

            totalSeeds += stageSeeds.Count;
            var stageReconstructed = PumpSeeds(recoveredHashes, stageSeeds, hasher);
            finalReconstructed.UnionWith(stageReconstructed);

            Console.WriteLine($"Stage {stageIndex + 1}/{samplingStagesInfo.Count}: decoded {stageSeeds.Count} seeds, pumped {stageReconstructed.Count} kmers.");
        }

        Console.WriteLine($"Total seeds processed: {totalSeeds}.");

        var reconstructedBuffer = Buffer<KmerData>.Rent(Math.Max(1, finalReconstructed.Count));
        foreach (var item in finalReconstructed)
        {
            reconstructedBuffer.Add(item);
        }

        compressedTable.Encode(reconstructedBuffer);
        var leftoversBuffer = compressedTable.Decode();
        var finalSet = new HashSet<KmerData>(finalReconstructed);
        foreach (var leftover in leftoversBuffer)
        {
            AddToHashset(finalSet, leftover);
        }

        leftoversBuffer.Return();
        reconstructedBuffer.Return();

        sw.Stop();

        if (allData.Count != finalSet.Count)
        {
            Console.WriteLine($"Experiment Failed: Expected {allData.Count} items, got {finalSet.Count}");
        }
        else
        {
            Console.WriteLine("Experiment Success: All items recovered.");
        }

        var originalSet = new HashSet<KmerData>(allData);
        int correctlyRecovered = 0;
        int falsePositives = 0;

        foreach (var item in finalSet)
        {
            if (originalSet.Contains(item))
            {
                correctlyRecovered++;
            }
            else
            {
                falsePositives++;
            }
        }

        int notRecovered = originalSet.Count - correctlyRecovered;

        var pumpedItems = finalReconstructed.Count;
        var arguments = new ExtendedExperimentArguments(baseTableSize, kmerSize, nSequences, sequenceLength, k, l, stageCount, shrink, Math.Max(0, maxDistance), seed);

        return new ExtendedExperimentResult(
            "v2",
            arguments,
            new ExperimentOutcome(originalSet.Count, correctlyRecovered, notRecovered, falsePositives, pumpedItems, sw.Elapsed.TotalMilliseconds, swGen.Elapsed.TotalMilliseconds)
        );
    }

    private static int CalculateSampleInterval(int baseK, double shrink, int stageIndex)
    {
        double interval = baseK * Math.Pow(shrink, stageIndex);
        return Math.Max(1, (int)Math.Ceiling(interval));
    }

    private static void EncodeSample(Tables<KmerData> table, IEnumerable<KmerData> data, int sampleInterval, int rentSize)
    {
        if (sampleInterval <= 0)
        {
            return;
        }

        var buffer = Buffer<KmerData>.Rent(Math.Max(1, rentSize));
        foreach (var item in data)
        {
            if (ShouldSample(item.Hash, sampleInterval))
            {
                buffer.Add(item);
            }
        }

        if (buffer.Length > 0)
        {
            table.Encode(buffer);
        }

        buffer.Return();
    }

    private static bool ShouldSample(ulong hash, int sampleInterval)
    {
        return sampleInterval <= 1 || hash % (ulong)sampleInterval == 0;
    }

    private static HashSet<KmerData> PumpSeeds(HashSet<ulong> recoveredHashes, HashSet<KmerData> seeds, KmerTabulationHash hasher)
    {
        var reconstructed = new HashSet<KmerData>();
        var stack = new Stack<KmerData>();

        foreach (var seed in seeds)
        {
            if (recoveredHashes.Contains(seed.Hash))
            {
                recoveredHashes.Remove(seed.Hash);
                stack.Push(seed);
                reconstructed.Add(seed);
            }
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            foreach (var nuc in new[] { Nucleotide.A, Nucleotide.C, Nucleotide.G, Nucleotide.T })
            {
                var nextData = KmerDataGenerator.RollingUpdate(current, nuc, hasher);
                if (recoveredHashes.Contains(nextData.Hash))
                {
                    recoveredHashes.Remove(nextData.Hash);
                    reconstructed.Add(nextData);
                    stack.Push(nextData);
                    break;
                }
            }

            foreach (var nuc in new[] { Nucleotide.A, Nucleotide.C, Nucleotide.G, Nucleotide.T })
            {
                var prevData = KmerDataGenerator.RollingUpdateReverse(current, nuc, hasher);
                if (recoveredHashes.Contains(prevData.Hash))
                {
                    recoveredHashes.Remove(prevData.Hash);
                    reconstructed.Add(prevData);
                    stack.Push(prevData);
                    break;
                }
            }
        }

        return reconstructed;
    }

    private readonly record struct SamplingStageInfo(Tables<KmerData> Table, int SampleInterval);
}

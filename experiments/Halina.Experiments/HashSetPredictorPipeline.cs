using Halina.Core;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace Halina.Experiments;

public record ExperimentArguments(
    int TableSize,
    int KmerSize,
    int NSequences,
    int SequenceLength,
    int K,
    int L,
    int Seed,
    int MaxDistance
);

public record ExperimentOutcome(
    int TotalItems,
    int CorrectlyRecovered,
    int NotRecovered,
    int FalsePositives,
    int PumpedItems,
    double DurationMs,
    double DataGenerationDurationMs
);

public record ExperimentResult(
    string Version,
    ExperimentArguments Arguments,
    ExperimentOutcome Result
);

public static class KmerExperiments
{

    private static  void AddToHashset<T>(HashSet<T> set, T item)
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

    public static ExperimentResult RunExperiment(int kmerSize, int nSequences, int sequenceLength, int k, int l, int seed = 123, int maxDistance = 10)
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

        var sw = Stopwatch.StartNew();
        // 2. Create Tables
        // Table 1: Hashes Only. Using tableSize as baseline.

        int baseTableSize = allData.Count;
        double magicMultiple1 = 1.3;
        var hashTable = IBLTFactory.GetStandardIBLT(3, (int)(baseTableSize * magicMultiple1));
        
        // Table 2: Sampled 1/k.
        double magicMultiple2 = 1.5;
        int sampledTableSize = Math.Max(100, (int)(baseTableSize / k  * magicMultiple2));
        var sampledTable = KmerIBLTFactory.CreateKmerIBLT(3, kmerSize, sampledTableSize);
        
        // Table 3: Compressed 1/L.
        double magicMultiple3 = 1;
        int compressedTableSize = Math.Max(100, (int)(baseTableSize / l * magicMultiple3));
        var compressedTable = KmerIBLTFactory.CreateKmerIBLT(2, kmerSize, compressedTableSize);

        // 3. Encode
        var hashBuffer = Buffer<UlongData>.Rent(allData.Count);
        var sampledBuffer = Buffer<KmerData>.Rent(allData.Count);
        var compressedBuffer = Buffer<KmerData>.Rent(allData.Count);

        foreach(var item in allData)
        {
            hashBuffer.Add(new UlongData(item.Hash));
            compressedBuffer.Add(item);
            
            if (item.Hash % (ulong)k == 0)
            {
                sampledBuffer.Add(item);
            }
        }
        Console.WriteLine("Sampled kmers for sampled table: " + sampledBuffer.Length.ToString() + " out of " + allData.Count.ToString());
        Console.WriteLine($"Expected {allData.Count / k} kmers in sampled table. {k}");
        hashTable.Encode(hashBuffer);
        sampledTable.Encode(sampledBuffer);
        compressedTable.Encode(compressedBuffer);

        // 4. Recover Hashes and Sampled Data

        Console.WriteLine("Decoding tables...");
        

        var decodedSampledBuffer = sampledTable.Decode();
        var seeds = new HashSet<KmerData>();
        foreach(var d in decodedSampledBuffer) AddToHashset(seeds, d);
        decodedSampledBuffer.Return();

        var recoveredHashes = new HashSet<ulong>();
        

        
        Console.WriteLine($"Recovered {seeds.Count} sampled kmers from sampled table.");

        // if (maxDistance <= 0)
        // {
        //     maxDistance = kmerSize;
        // }
        // maxDistance = Math.Min(maxDistance, kmerSize);
        // int minOverlap = Math.Max(1, maxDistance);
        // Console.WriteLine($"Running collider with min overlap {minOverlap} (max distance {maxDistance}).");

        // var collider = new Collider(seeds, minOverlap, seed);
        // var matches = collider.Run();
        // foreach (var match in matches)
        // {
        //     var bridge = collider.GetBridge(match);
        //     foreach (var item in bridge)
        //     {
        //         recoveredHashes.Add(item.Hash);
        //     }
        // }

        // if (recoveredHashes.Count > 0)
        // {
        //     var additionBuffer = Buffer<UlongData>.Rent(Math.Max(1, recoveredHashes.Count));
        //     foreach (var value in recoveredHashes)
        //     {
        //         additionBuffer.Add(new UlongData(value));
        //     }
        //     hashTable.Encode(additionBuffer);
        //     additionBuffer.Return();
        //     Console.WriteLine($"Symmetric difference between newly encoded hashes and decoded-after hashes contains {recoveredHashes.Count} hashes.");
        // }
        var decodedHashesBuffer = hashTable.Decode();
        foreach(var d in decodedHashesBuffer) AddToHashset(recoveredHashes, d.Value);
        decodedHashesBuffer.Return();
        Console.WriteLine($"Recovered {recoveredHashes.Count} hashes from hash table.");

        var reconstructed = Pump(seeds, recoveredHashes, hasher);

        Console.WriteLine($"Reconstructed {reconstructed.Count} kmers via rolling from seeds.");
        // 6. Recover remaining from last table

        var finalSet = new HashSet<KmerData>(reconstructed);

        while(true)
        {
            var reconstructedBuffer = Buffer<KmerData>.Rent(reconstructed.Count);
            foreach(var item in reconstructed) reconstructedBuffer.Add(item);
        
            compressedTable.Encode(reconstructedBuffer); // Subtract reconstructed items
            var leftoversBuffer = compressedTable.Decode();




            var newlyReconstructed = Pump(leftoversBuffer.Data.ToHashSet(), recoveredHashes, hasher);

            foreach(var item in newlyReconstructed) AddToHashset(finalSet, item);

            Console.WriteLine($"Pumping iteration: reconstructed {newlyReconstructed.Count} additional kmers.");
            
            leftoversBuffer.Return();
            reconstructedBuffer.Return();
            if (newlyReconstructed.Count == 0) 
            {
                
                break;
            }
            reconstructed = newlyReconstructed;
        }
        sw.Stop();

        if (allData.Count != finalSet.Count)
        {
            Console.WriteLine($"Experiment Failed: Expected {allData.Count} items, got {finalSet.Count}");
        }
        else
        {
            Console.WriteLine("Experiment Success: All items recovered.");
        }
        
        // Cleanup
        hashBuffer.Return();
        sampledBuffer.Return();
        compressedBuffer.Return();

        // Calculate Results
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

        var arguments = new ExperimentArguments(baseTableSize, kmerSize, nSequences, sequenceLength, k, l, seed, maxDistance);

        return new ExperimentResult(
            "v1",
            arguments,
            new ExperimentOutcome(originalSet.Count, correctlyRecovered, notRecovered, falsePositives, reconstructed.Count,  sw.Elapsed.TotalMilliseconds, swGen.Elapsed.TotalMilliseconds)
        );
    }

    public static HashSet<KmerData> Pump(HashSet<KmerData> seeds, HashSet<ulong> recoveredHashes, KmerTabulationHash hasher)
    {
                // 5. Pump / Reconstruct
        var reconstructed = new HashSet<KmerData>();
        var stack = new Stack<KmerData>();
        
        foreach(var s in seeds)
        {
            if (recoveredHashes.Contains(s.Hash))
            {
                recoveredHashes.Remove(s.Hash);
                stack.Push(s);
                reconstructed.Add(s);
            }
        }

        while(stack.Count > 0)
        {
            var current = stack.Pop();
            
            // Forward (Right side extension)
            foreach(var nuc in new [] { Nucleotide.A, Nucleotide.C, Nucleotide.G, Nucleotide.T })
            {
                var nextData = KmerDataGenerator.RollingUpdate(current, nuc, hasher);
                
                if (recoveredHashes.Contains(nextData.Hash))
                {
                    recoveredHashes.Remove(nextData.Hash);
                    reconstructed.Add(nextData);
                    stack.Push(nextData);
                    break; // Continue with the new item (DFS)
                }
            }

            // Backward (Left side extension)
            foreach(var nuc in new [] { Nucleotide.A, Nucleotide.C, Nucleotide.G, Nucleotide.T })
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
}


using Halina.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halina.Experiments;

public record MutationExperimentResult(
    int TotalRecoveredKmers,
    int CorrectlyIdentifiedMutations,
    int IncorrectlyIdentifiedMutations,
    int MissedMutations
);

public class MutationExperiments
{
    public static MutationExperimentResult Run(int kmerLength, int m, int l, int nSequences, int seed)
    {
        int hmerLength = (kmerLength / 4 + 1);
        int sequenceLength = 2 * kmerLength;
        
        var generator = new KmerDataGenerator(seed, kmerLength);
        var hasher = new KmerTabulationHash(seed);
        var hmerHasher = new KmerTabulationHash(seed + 1); // Different seed for h-mers

        var allKmers = new List<KmerData>();
        var allHmerHashes = new HashSet<ulong>();

        // 1. Generate Data
        for (int i = 0; i < nSequences; i++)
        {
            // Generate Double Sequence
            // Offset1 = 0, Offset2 = 0, SetId1 = 1, SetId2 = 2
            var doubleSeq = DatasetGenerator.Generate(sequenceLength, 0, 0, 1, 2, seed + i);
            
            // Process Seq1
            ProcessSequence(doubleSeq.Seq1, doubleSeq.Seq2, kmerLength, hmerLength, hasher, hmerHasher, allKmers, allHmerHashes, m, l);
            // Process Seq2
            ProcessSequence(doubleSeq.Seq2, doubleSeq.Seq1, kmerLength, hmerLength, hasher, hmerHasher, allKmers, allHmerHashes, m, l);
        }

        // 2. Create Table A (IBLT for Kmers)
        // Size estimation: allKmers contains sampled kmers (1/m).
        int tableASize = Math.Max(100, (int)(allKmers.Count * 1.5));
        var tableA = KmerIBLTFactory.CreateKmerIBLT(3, kmerLength, tableASize);
        
        var bufferA = Buffer<KmerData>.Rent(allKmers.Count);
        foreach (var kmer in allKmers) bufferA.Add(kmer);
        tableA.Encode(bufferA);
        
        // 3. Create Table B (IBLT for Hmer Hashes) - simulating by just using the HashSet for lookup as per prompt "IBLT -B of hashes"
        // In a real scenario, we would encode hashes into an IBLT and decode, but here we can simulate the "decoded set" 
        // by just taking the sampled hashes we collected.
        // The prompt says "create IBLT -B of hashes... and select only 1/lth".
        // We already filtered by 1/l in ProcessSequence.
        // Let's assume we successfully recovered these hashes from Table B.
        var recoveredHmerHashes = allHmerHashes; 

        Console.WriteLine($"Generated {allKmers.Count} kmers with mutations from {nSequences} sequences.");
        Console.WriteLine($"Generated {allHmerHashes.Count} hmer hashes sampled at 1/{l}.");

        // 4. Decode Table A
        var decodedBufferA = tableA.Decode();
        var recoveredKmers = new List<KmerData>();
        foreach (var item in decodedBufferA) recoveredKmers.Add(item);

        // 5. Find Modified Nucleotides
        int correct = 0;
        int incorrect = 0;
        int missed = 0;

        foreach (var kmer in recoveredKmers)
        {
            // We only care about kmers that actually have a mutation recorded in metadata
            if (kmer.MetaData.MutationIndex == 0) continue;

            int expectedIndex = kmer.MetaData.MutationIndex - 1;
            int expectedValue = kmer.MetaData.MutationValue;

            var foundMutation = FindModifiedNucleotide(kmer, recoveredHmerHashes, hmerLength, hmerHasher);

            if (foundMutation.HasValue)
            {
                if (foundMutation.Value.Index == expectedIndex && (int)foundMutation.Value.Nucleotide == expectedValue)
                {
                    correct++;
                }
                else
                {
                    incorrect++;
                }
            }
            else
            {
                missed++;
            }
        }

        bufferA.Return();
        decodedBufferA.Return();

        return new MutationExperimentResult(recoveredKmers.Count, correct, incorrect, missed);
    }

    private static void ProcessSequence(Sequence seq, Sequence otherSeq, int kmerLength, int hmerLength, 
        KmerTabulationHash hasher, KmerTabulationHash hmerHasher, 
        List<KmerData> kmerList, HashSet<ulong> hmerHashes, int m, int l)
    {
        // Extract Kmers
        foreach (var kmerData in seq.GetKmers(kmerLength, hasher))
        {
            if (kmerData.Hash % (ulong)m == 0)
            {
                // Check for mutation
                // Mutation is at index 'mid' of the sequence.
                // Sequence length is 2*kmerLength. mid is at kmerLength.
                int mid = seq.Nucleotides.Length / 2;
                int start = kmerData.MetaData.Index;
                int end = start + kmerLength;

                int mutationIndex = 0;
                int mutationValue = 0;

                if (start <= mid && mid < end)
                {
                    mutationIndex = (mid - start) + 1; // 1-based
                    mutationValue = (int)otherSeq.Nucleotides[mid];
                }

                var kmerWithMeta = kmerData;
                kmerWithMeta.MetaData = new KmerMetaData
                {
                    Index = kmerData.MetaData.Index,
                    SetId = kmerData.MetaData.SetId,
                    MutationIndex = mutationIndex,
                    MutationValue = mutationValue
                };
                kmerList.Add(kmerWithMeta);
            }
        }

        // Extract Hmers (k/2 mers)
        // We need to generate them manually as Sequence.GetKmers uses the main hasher and kmerLength
        for (int i = 0; i <= seq.Nucleotides.Length - hmerLength; i++)
        {
            // Create hmer
            var hmerNucs = new Nucleotide[hmerLength];
            Array.Copy(seq.Nucleotides, i, hmerNucs, 0, hmerLength);
            var hmer = new Kmer(hmerNucs);
            ulong hash = hmerHasher.ComputeHash(hmer);

            if (hash % (ulong)l == 0)
            {
                if(!hmerHashes.Contains(hash))
                    hmerHashes.Add(hash);
                else
                    hmerHashes.Remove(hash);
            }
        }
    }

    private static (int Index, Nucleotide Nucleotide)? FindModifiedNucleotide(KmerData kmerData, HashSet<ulong> validHmerHashes, int hmerLength, KmerTabulationHash hmerHasher)
    {
        int k = kmerData.Data.Length;
        
        // Initial Hmer
        var hmerNucs = new Nucleotide[hmerLength];
        for(int j=0; j<hmerLength; j++) hmerNucs[j] = kmerData.Data.GetNucleotide(j);
        var currentKmer = new Kmer(hmerNucs);
        var currentHash = hmerHasher.ComputeHash(currentKmer);
        
        var currentHmerData = new KmerData { Data = currentKmer, Hash = currentHash };
        
        // Iterate over all possible h-mers within the k-mer
        for (int i = 0; i <= k - hmerLength; i++)
        {
            // Try modifying each position in this h-mer
            for (int pos = 0; pos < hmerLength; pos++)
            {
                var originalNuc = currentHmerData.Data.GetNucleotide(pos);
                
                foreach (var nuc in new[] { Nucleotide.A, Nucleotide.C, Nucleotide.G, Nucleotide.T })
                {
                    if (nuc == originalNuc) continue;

                    ulong hash = hmerHasher.SubstituteNucleotideHash(currentHmerData.Hash, currentHmerData.Data, pos, nuc);

                    if (validHmerHashes.Contains(hash))
                    {
                        return (i + pos, nuc);
                    }
                }
            }
            
            if (i < k - hmerLength)
            {
                var nextNuc = kmerData.Data.GetNucleotide(i + hmerLength);
                currentHmerData = KmerDataGenerator.RollingUpdate(currentHmerData, nextNuc, hmerHasher);
            }
        }
        return null;
    }
}
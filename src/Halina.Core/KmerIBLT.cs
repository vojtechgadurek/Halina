using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.AccessControl;

namespace Halina.Core;

public struct KmerMetaData : IXorType<KmerMetaData>
{
    public int Index;
    public int SetId;
    public int MutationIndex; // 0 = none, else index + 1
    public int MutationValue;

    public KmerMetaData Xor(KmerMetaData other)
    {
        return new KmerMetaData { 
            Index = Index ^ other.Index, 
            SetId = SetId ^ other.SetId,
            MutationIndex = MutationIndex ^ other.MutationIndex,
            MutationValue = MutationValue ^ other.MutationValue
        };
    }
}

public struct KmerData : IHashMetadataData<ulong, KmerMetaData, Kmer>, IXorType<KmerData>
{
    public KmerMetaData MetaData { get; set; }
    public ulong Hash { get; set; }
    public Kmer Data { get; set; }

    public KmerData Xor(KmerData other)
    {
        Kmer newData;
        newData = Data.Xor(other.Data);

        return new KmerData
        {
            MetaData = MetaData.Xor(other.MetaData),
            Hash = Hash ^ other.Hash,
            Data = newData
        };
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is KmerData other &&
               Hash == other.Hash;
    }
}

public interface INullDataProvider<TData>
{
    TData GetNullData();
}
public struct KeyEncodeTable<TData, TNullDataProvider> : IKeyEncodeTable<int, TData>
    where TNullDataProvider : struct, INullDataProvider<TData>
    where TData : IXorType<TData>
{
    private readonly TData[] _table;
    private readonly TNullDataProvider _nullDataProvider;
    public KeyEncodeTable(int size){
        _table = new TData[size];
        _nullDataProvider = new();
        for (int i = 0; i < size; i++)
        {
            _table[i] = _nullDataProvider.GetNullData();
        }
    }
    
    public void KeyEncode(int key, TData data)
    {
        _table[key] = _table[key].Xor(data);
    }

    public TData Get(int key) => _table[key];
    
    public void Remove(int key)
    {
        _table[key] = _nullDataProvider.GetNullData(); // Use the null data provider to reset
    }
}


public class ObjectKeyEncodeTable<TData> : IKeyEncodeTable<int, TData>
    where TData : IXorType<TData>
{
    private readonly TData[] _table;

    private readonly Func<TData> _nullDataProvider;
    public ObjectKeyEncodeTable(int size, Func<TData> nullDataProvider){
        _table = new TData[size];
        _nullDataProvider = nullDataProvider;
        for (int i = 0; i < size; i++)
        {
            _table[i] = _nullDataProvider();
        }
    }
    
    public void KeyEncode(int key, TData data)
    {
        _table[key] = _table[key].Xor(data);
    }

    public TData Get(int key) => _table[key];
    
    public void Remove(int key)
    {
        _table[key] = _nullDataProvider(); // Use the null data provider to reset
    }
}






public struct KmerDataIndexer : IIndexer<int, KmerData>
{
    public TabulationHash HashFunction;
    public int Size;
    public int ComputeIndex(KmerData data) => (int)(HashFunction.ComputeHash(data.Hash) % (ulong)Size);
}

public struct KmerDataPureTester : IPureTester<int, KmerData>
{
    public TabulationHash HashFunction;
    public int Size;
    public bool IsPure(int index, KmerData data)
    {
        if (data.Hash == 0) return false;
        return (int)(HashFunction.ComputeHash(data.Hash) % (ulong)Size) == index;
    }
}



public struct KmerDataNullDataProvider<TSize> : INullDataProvider<KmerData>
{
    public KmerData GetNullData()
    {
        return new KmerData
        {
            MetaData = new KmerMetaData { Index = 0, SetId = 0, MutationIndex = 0, MutationValue = 0 },
            Hash = 0,
            Data = new Kmer(1) // Minimal Kmer
        };
    }
}

public static class KmerIBLTFactory
{
    public static Tables<KmerData> CreateKmerIBLT(int ntables, int kmerLength, int tableSize)
    {
        var tables = new List<ITable<KmerData>>();
        tableSize /= ntables; // Split total size among tables
        
        for (int i = 0; i < ntables; i++)
        {
            var hash = new TabulationHash(i * 9876 + 54321);
            var indexer = new KmerDataIndexer { HashFunction = hash, Size = tableSize };
            var pureTester = new KmerDataPureTester { HashFunction = hash, Size = tableSize };
            var dict = new ObjectKeyEncodeTable<KmerData>(tableSize, () => new KmerData
            {
                MetaData = new KmerMetaData { Index = 0, SetId = 0, MutationIndex = 0, MutationValue = 0 },
                Hash = 0,
                Data = new Kmer(kmerLength) // Minimal Kmer
            });
            tables.Add(new Table<ObjectKeyEncodeTable<KmerData>, KmerData, int, KmerDataIndexer, KmerDataPureTester>(dict, indexer, pureTester));
        }
        
        return new Tables<KmerData>(tables, new TabuDecodingControl<KmerData>(3, data => data.Hash));
    }
}

public class KmerDataGenerator
{
    private readonly Random _rng;
    private readonly int _kmerLength;
    private readonly KmerTabulationHash _hasher;

    public KmerDataGenerator(int seed, int kmerLength)
    {
        _rng = new Random(seed);
        _kmerLength = kmerLength;
        _hasher = new KmerTabulationHash(seed);
    }

    public List<List<KmerData>> GenerateSequences(int mSequences, int nKmersPerSequence, int setId)
    {
        var result = new List<List<KmerData>>();

        for (int i = 0; i < mSequences; i++)
        {
            var sequence = new List<KmerData>();
            
            // Generate initial random Kmer
            byte[] initialBytes = new byte[(_kmerLength * 2 + 7) / 8];
            _rng.NextBytes(initialBytes);
            // Mask unused bits
            int usedBits = _kmerLength * 2;
            if (usedBits % 8 != 0)
            {
                initialBytes[initialBytes.Length - 1] &= (byte)(0xFF << (8 - (usedBits % 8)));
            }
            
            // We need to construct a Kmer from bytes, but Kmer constructor takes length or string.
            // We'll use a string for simplicity of initialization or create a helper.
            // Let's generate a random string instead.
            char[] chars = new char[_kmerLength];
            for(int k=0; k<_kmerLength; k++) chars[k] = Kmer.NucleotideToChar((Nucleotide)_rng.Next(4));
            
            Kmer currentKmer = new Kmer(new string(chars));
            
            int index = Random.Shared.Next();
            for (int j = 0; j < nKmersPerSequence; j++)
            {
                ulong hash = _hasher.ComputeHash(currentKmer);
                
                sequence.Add(new KmerData
                {
                    MetaData = new KmerMetaData { Index = j + index, SetId = setId, MutationIndex = 0, MutationValue = 0 },
                    Hash = hash,
                    Data = currentKmer.DeepCopy()
                });

                // Push random nucleotide for next step (Rolling)
                if (j < nKmersPerSequence - 1)
                {
                    Nucleotide nextNuc = (Nucleotide)_rng.Next(4);
                    currentKmer.ShiftLeft(nextNuc);
                }
            }
            result.Add(sequence);
        }
        return result;
    }

    public static KmerData RollingUpdate(KmerData currentData, Nucleotide newNucleotide, KmerTabulationHash hasher)
    {
        // 1. Get old first byte (for rolling hash)
        byte[] bytes = currentData.Data.GetBytes();
        byte oldFirstByte = bytes[0];

        // 2. Update Kmer Data (Shift Left)
        Kmer newKmer = currentData.Data.DeepCopy();
        newKmer.ShiftLeft(newNucleotide);


        byte[] newBytes = newKmer.GetBytes();
        int len = newKmer.Length;
        int lastByteIdx = (len * 2 - 1) / 8; // Index of byte containing last bit
        

        int k = len - 4;
        int byteIdx = k >> 2;
        int bitShift = (k & 3) << 1;
        byte newLastByteVal = bitShift == 0 
            ? newBytes[byteIdx] 
            : (byte)((newBytes[byteIdx] << bitShift) | (newBytes[byteIdx + 1] >> (8 - bitShift)));

        // 4. Roll Hash
        ulong newHash = hasher.RollHash(currentData.Hash, oldFirstByte, newLastByteVal, len);

        return new KmerData
        {
            MetaData = new KmerMetaData { Index = currentData.MetaData.Index + 1, SetId = currentData.MetaData.SetId, MutationIndex = 0, MutationValue = 0 },
            Hash = newHash,
            Data = newKmer
        };
    }

    public static KmerData RollingUpdateReverse(KmerData currentData, Nucleotide newNucleotide, KmerTabulationHash hasher)
    {
        // 1. Get old last byte value (for rolling hash removal)
        byte[] bytes = currentData.Data.GetBytes();
        int len = currentData.Data.Length;
        
        int k = len - 4;
        int byteIdx = k >> 2;
        int bitShift = (k & 3) << 1;
        byte oldLastByteVal = bitShift == 0 
            ? bytes[byteIdx] 
            : (byte)((bytes[byteIdx] << bitShift) | (bytes[byteIdx + 1] >> (8 - bitShift)));

        // 2. Update Kmer Data (Shift Right)
        Kmer newKmer = currentData.Data.DeepCopy();
        newKmer.ShiftRight(newNucleotide);

        // 3. Calculate new first byte value (for rolling hash addition)
        byte oldFirstByte = bytes[0];
        byte newFirstByteVal = (byte)(((byte)newNucleotide << 6) | (oldFirstByte >> 2));

        // 4. Roll Hash
        ulong newHash = hasher.RollHashReverse(currentData.Hash, oldLastByteVal, newFirstByteVal, len);

        return new KmerData
        {
            MetaData = new KmerMetaData { Index = currentData.MetaData.Index - 1, SetId = currentData.MetaData.SetId, MutationIndex = 0, MutationValue = 0 },
            Hash = newHash,
            Data = newKmer
        };
    }
}
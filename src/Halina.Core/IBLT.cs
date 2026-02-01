using System;
using System.Collections;
using System.Threading.Tasks;
using System.Buffers;
using System.Collections.Generic;

namespace Halina.Core;
public interface IKeyEncodeTable<TKey, TDataType> {
    void KeyEncode(TKey key, TDataType data);
    TDataType Get(TKey key);
    void Remove(TKey key);    
 
}


public interface IIndex
{
    int ToInt();
}

public interface IXorType<T>
{
    T Xor(T other);
}


public class KeyEncodeTableDictionary<TKey, TDataType> 
    : IKeyEncodeTable<TKey, TDataType>
    where TKey : IIndex
    where TDataType : IXorType<TDataType>
{
    TDataType[] dictionary;
    Func<TDataType> nullDataProvider;
    public KeyEncodeTableDictionary(int size, Func<TDataType> nullDataProvider)
    {
        this.nullDataProvider = nullDataProvider;
        dictionary = new TDataType[size];
        for (int i = 0; i < size; i++)
        {
            dictionary[i] = nullDataProvider();
        }
    }
    public void KeyEncode(TKey key, TDataType data)
    {
        var value = dictionary[key.ToInt()];
        dictionary[key.ToInt()] = value.Xor(data);
    }

    public TDataType Get(TKey key)
    {
        return dictionary[key.ToInt()];
    }

    public void Remove(TKey key)
    {
        dictionary[key.ToInt()] = nullDataProvider();
    }
}

public struct Buffer<T> : IEnumerable<T> {
    public T[]? Data;
    public int Length;

    public IEnumerator<T> GetEnumerator()
    {
        if (Data == null)
            yield break;
        for (int i = 0; i < Length; i++)
        {
            yield return Data[i];
        }
    }
    public void Enlarge(int newSize)
    {
        if (Data == null)
        {
            Data = ArrayPool<T>.Shared.Rent(newSize);
            Length = 0;
            return;
        }
        if (newSize <= Data.Length)
        {
            throw new ArgumentException("New size must be larger than current size");
        }
        T[] newData = ArrayPool<T>.Shared.Rent(newSize);
        Array.Copy(Data, newData, Length);
        ArrayPool<T>.Shared.Return(Data);
        Data = newData;
    }
    public void Add(T item)
    {
        if (Data == null || Length >= Data.Length)
        {
            Enlarge((Data == null) ? 4 : Data.Length * 2);
        }
        if (Data == null)
            throw new InvalidOperationException("Data array is null after enlargement");
        Data[Length] = item;
        Length++;
    }

    public void CopyFrom(T[] sourceArray, int targetIndex, int count)
    {
        if (Data == null || Length + count > Data.Length)
        {
            Enlarge( (Data == null) ? count : Math.Max(Data.Length * 2, Length + count));
        }
        Array.Copy(sourceArray, 0, Data, targetIndex, count);
        Length += count;
    }
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static Buffer<T> Rent(int size)
    {
        Buffer<T> buffer = new Buffer<T>();
        buffer.Data = ArrayPool<T>.Shared.Rent(size);
        buffer.Length = 0;
        return buffer;
    }

    public Buffer<T> Copy(){
        Buffer<T> newBuffer = new Buffer<T>();
        newBuffer.Data = ArrayPool<T>.Shared.Rent(Length);
        Array.Copy(Data, newBuffer.Data, Length);
        newBuffer.Length = Length;
        return newBuffer;
    }
    public void Return()
    {
        if (Data != null)
        {
            ArrayPool<T>.Shared.Return(Data);
        }
        Data = null;
        Length = 0;
    }

}

public interface IDecodable<TDataType>
{    Buffer<TDataType> Decode();
}


public interface ITableDataType<TBaseType, TMetaData, THash,  TData>
{
    TMetaData MetaData { get; }
    THash Hash { get; }
    TData Data { get; }
    TBaseType Xor(TBaseType other);
}




public enum TableState
{
    Finished,
    Decoding,
}

public interface ITable<TDataType>
{
    Buffer<TDataType> Decode();
    void Encode(Buffer<TDataType> data);
    TableState GetState();
}


public interface IDecodingControl<TDataType>
{
    void Update(Buffer<TDataType> decodedData);
    bool Continue();
    void Reset();
}


public interface IIndexer<TIndex, TDataType>
{    
    TIndex ComputeIndex(TDataType data);
}


public interface IPureTester<TIndex, TDataType>
{
        bool IsPure(TIndex index, TDataType data);
}

public class Table<TDictionary, TDataType, TIndex, TIndexer, TPureTester> : ITable<TDataType>
where TDictionary : IKeyEncodeTable<TIndex, TDataType>
where TIndexer : struct,  IIndexer<TIndex, TDataType>
where TPureTester : struct, IPureTester<TIndex, TDataType>
{
    TDictionary dictionary;
    ModifiedIndexesKeeper<TIndex> modifiedIndexes = new ModifiedIndexesKeeper<TIndex>();
    TIndexer indexer;
    TPureTester pureTester;
    int bufferSize;

    public Table(TDictionary dictionary, int bufferSize = 1024 * 8)
    {
        this.dictionary = dictionary;
        this.bufferSize = bufferSize;
        this.indexer = new TIndexer();
        this.pureTester = new TPureTester();
    }

    public Table(TDictionary dictionary, TIndexer indexer, TPureTester pureTester, int bufferSize = 1024 * 8)
    {
        this.dictionary = dictionary;
        this.indexer = indexer;
        this.pureTester = pureTester;
        this.bufferSize = bufferSize;
    }

    public Buffer<TDataType> Decode()
    {
        Buffer<TDataType> decodedData = Buffer<TDataType>.Rent(bufferSize);
        Buffer<TIndex> modifiedIndexesBuffer = modifiedIndexes.GetModifiedIndexes();
        foreach (var index in modifiedIndexesBuffer)
        {
            var data = dictionary.Get(index);
            if (pureTester.IsPure(index, data))
            {
                dictionary.Remove(index);
                decodedData.Add(data);
            }
        }
        modifiedIndexes.Clear();
        modifiedIndexesBuffer.Return();
        return decodedData;
    }
    public void Encode(Buffer<TDataType> data)
    {
        foreach (var item in data)
        {
            var index = indexer.ComputeIndex(item);
            modifiedIndexes.MarkModified(index);
            dictionary.KeyEncode(index, item);
        }
    }
    public TableState GetState()
    {
        return modifiedIndexes.HasModifications ? TableState.Decoding : TableState.Finished;
    }
}

public class FilteredTable<TDataType> : ITable<TDataType>
{
    private readonly ITable<TDataType> table;
    private readonly IHashFunction<ulong, TDataType> hashFunction;
    private readonly ulong filterModulo;
    private readonly ulong targetRemainder;

    public FilteredTable(ITable<TDataType> table, IHashFunction<ulong, TDataType> hashFunction, ulong filteringRatio, ulong remainder = 0)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.hashFunction = hashFunction ?? throw new ArgumentNullException(nameof(hashFunction));
        if (filteringRatio == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(filteringRatio), "Filtering ratio must be greater than zero.");
        }
        filterModulo = filteringRatio;
        targetRemainder = remainder % filteringRatio;
    }

    public Buffer<TDataType> Decode() => table.Decode();
    public TableState GetState() => table.GetState();

    public void Encode(Buffer<TDataType> data)
    {
        Buffer<TDataType> filteredData = Buffer<TDataType>.Rent(data.Length);
        try
        {
            foreach (var item in data)
            {
                var hashValue = hashFunction.ComputeHash(item);
                if (hashValue % filterModulo == targetRemainder)
                {
                    filteredData.Add(item);
                }
            }

            if (filteredData.Length > 0)
            {
                table.Encode(filteredData);
            }
        }
        finally
        {
            filteredData.Return();
        }
    }
}

public class HashFilterTable<TDataType> : ITable<TDataType>
{
    private readonly ITable<UlongData> table;
    private readonly IHashFunction<ulong, TDataType> hashFunction;
    private readonly ulong targetRemainder;
    private readonly HashSet<ulong> encodedHashes = new();
    private bool isDecoding = false;
    public HashFilterTable(ITable<UlongData> table, IHashFunction<ulong, TDataType> hashFunction, ulong filteringRatio, ulong remainder = 0)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.hashFunction = hashFunction ?? throw new ArgumentNullException(nameof(hashFunction));
    }

    public Buffer<TDataType> Decode()
    {
        isDecoding = true;
        var decodedHashEntries = table.Decode();
        try
        {
            foreach (var hashed in decodedHashEntries)
            {
                Toggle(encodedHashes, hashed.Value);
            }
        }
        finally
        {
            decodedHashEntries.Return();
        }
        Buffer<TDataType> result = Buffer<TDataType>.Rent(0);
        return result;
    }

    public void Encode(Buffer<TDataType> data)
    {

        Buffer<UlongData> hashdata = Buffer<UlongData>.Rent(data.Length);
        foreach (var item in data)
        {
            var hashValue = hashFunction.ComputeHash(item);
            if (isDecoding)
            {
                Toggle(encodedHashes, hashValue);
            }
            else
                {hashdata.Add(new UlongData(hashValue));continue;}
        }
        table.Encode(hashdata);
    }

    public TableState GetState()
    {
        if (encodedHashes.Count > 0)
        {
            return TableState.Decoding;
        }
        return table.GetState() == TableState.Decoding ? TableState.Decoding : TableState.Finished;
    }

    // Mirrors the symmetric-difference reconstruction phase from the HashPredictor experiment.

    private static bool Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Add(value))
        {
            set.Remove(value);
            return false;
        }
        return true;
    }
}

public struct UlongIndexer : IIndexer<int, UlongData>
{
    public TabulationHash HashFunction;
    public int Size;
    public int ComputeIndex(UlongData data) => (int)(HashFunction.ComputeHash(data.Value) % (ulong)Size);
}

public struct UlongPureTester : IPureTester<int, UlongData>
{
    public TabulationHash HashFunction;
    public int Size;
    public bool IsPure(int index, UlongData data)
    {
        if (data.Value == 0) return false;
        return (int)(HashFunction.ComputeHash(data.Value) % (ulong)Size) == index;
    }
}

public class SimpleDecodingControl<T> : IDecodingControl<T>
{
    private int _emptySteps = 0;
    private readonly int _limit;
    public SimpleDecodingControl(int limit) => _limit = limit;
    public void Update(Buffer<T> decodedData)
    {
        if (decodedData.Length > 0)
            _emptySteps = 0;
        else
            _emptySteps++;
    }
    public bool Continue() => _emptySteps < _limit;
    public void Reset() => _emptySteps = 0;
}

public class TabuDecodingControl<T> : IDecodingControl<T>
{
    private readonly Func<T, ulong> _hashSelector;
    private readonly HashSet<ulong> _tabuStates = new();
    private readonly int _limit;
    private int _emptySteps;
    private ulong _stateXor;

    public TabuDecodingControl(int limit, Func<T, ulong> hashSelector)
    {
        _limit = limit;
        _hashSelector = hashSelector ?? throw new ArgumentNullException(nameof(hashSelector));
    }

    public void Update(Buffer<T> decodedData)
    {
        if (decodedData.Length == 0)
        {
            _emptySteps++;
            return;
        }

        _emptySteps = 0;
        ulong stepXor = 0;
        foreach (var item in decodedData)
        {
            stepXor ^= _hashSelector(item);
        }
        _stateXor ^= stepXor;
    }

    public bool Continue()
    {
        if (_emptySteps >= _limit)
            return false;
        if (_tabuStates.Contains(_stateXor))
            return false;
        _tabuStates.Add(_stateXor);
        return true;
    }

    public void Reset()
    {
        _emptySteps = 0;
        _stateXor = 0;
        _tabuStates.Clear();
    }
}

public struct UlongNullDataProvider : INullDataProvider<UlongData>
{
    public UlongData GetNullData() => new UlongData(0);
}

public record struct UlongData(ulong Value): IXorType<UlongData>
{
    public UlongData Xor(UlongData other)
    {
        return new UlongData(Value ^ other.Value);
    }
}


public static class IBLTFactory
{
    public static Tables<UlongData> GetStandardIBLT(int ntables, int tableSize)
    {
        var tables = new List<ITable<UlongData>>();
        tableSize /= 3;
        for (int i = 0; i < ntables; i++)
        {
            var hash = new TabulationHash(i * 12345 + 6789);
            var indexer = new UlongIndexer { HashFunction = hash, Size = tableSize };
            var pureTester = new UlongPureTester { HashFunction = hash, Size = tableSize };
            //var dict = newtableSize);
            var dict = new KeyEncodeTable<UlongData, UlongNullDataProvider>(tableSize);
            tables.Add(new Table<KeyEncodeTable<UlongData, UlongNullDataProvider>, UlongData, int, UlongIndexer, UlongPureTester>(dict, indexer, pureTester));
        }
        return new Tables<UlongData>(tables, new TabuDecodingControl<UlongData>(3, data => data.Value));
    }
}


public class ModifiedIndexesKeeper<TIndex>
{
    HashSet<TIndex> modifiedIndexes = new HashSet<TIndex>();

    public void MarkModified(TIndex index)
    {
        modifiedIndexes.Add(index);
    }

    public Buffer<TIndex> GetModifiedIndexes()
    {
        Buffer<TIndex> buffer = Buffer<TIndex>.Rent(modifiedIndexes.Count);
        modifiedIndexes.CopyTo(buffer.Data!, 0);
        buffer.Length = modifiedIndexes.Count;
        return buffer;
    }

    public void Clear()
    {
        modifiedIndexes.Clear();
    }    

    public bool HasModifications => modifiedIndexes.Count > 0;
}



public class Tables<TDataType> : ITable<TDataType>
{

    List<ITable<TDataType>> tables;
    int currentTable = 0;

    IDecodingControl<TDataType> decodingControl;

    public Tables(List<ITable<TDataType>> tables, IDecodingControl<TDataType> decodingControl )
    {
        this.tables = tables;
        this.decodingControl = decodingControl;
    }
    public Tables<TDataType> AddTable(ITable<TDataType> table)
    {
        tables.Add(table);
        return this;
    }

    public void Encode(Buffer<TDataType> data)
    {
        
        Parallel.ForEach(tables, table =>
        {
            var dataCopy = data.Copy();
            table.Encode(dataCopy);
            dataCopy.Return();});
    }

    public TableState GetState()
    {
        foreach(var table in tables)
        {
            if (table.GetState() == TableState.Decoding)
                return TableState.Decoding;
        }
        return TableState.Finished;
    }

    public Buffer<TDataType> Decode()
    {
        decodingControl.Reset();
        Buffer<TDataType> result = Buffer<TDataType>.Rent(0);
        while (decodingControl.Continue())
        {
            var decoded = DecodeStep();
            decodingControl.Update(decoded);
            result.CopyFrom(decoded.Data!, result.Length, decoded.Length);
            decoded.Return();
        }
        return result;
    }

    public Buffer<TDataType> DecodeStep()
    {
        var table = tables[currentTable];
        var decoded = table.Decode();
        Parallel.ForEach(tables, proccessTable =>
        {if (proccessTable == table)return;;
            proccessTable.Encode(decoded);
        });
        currentTable++;
        if (currentTable >= tables.Count)
            currentTable = 0;
        return decoded;
    }
}

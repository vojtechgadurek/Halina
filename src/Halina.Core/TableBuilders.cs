using System;
using System.Collections.Generic;

namespace Halina.Core;

internal sealed class RuntimeKeyEncodeTable<TKey, TData> : IKeyEncodeTable<TKey, TData>
    where TKey : notnull
    where TData : IXorType<TData>
{
    private readonly Dictionary<TKey, TData> _values = new();
    private readonly Func<TData> _nullDataFactory;

    public RuntimeKeyEncodeTable(Func<TData> nullDataFactory)
    {
        _nullDataFactory = nullDataFactory ?? throw new ArgumentNullException(nameof(nullDataFactory));
    }

    public void KeyEncode(TKey key, TData data)
    {
        var current = Get(key);
        _values[key] = current.Xor(data);
    }

    public TData Get(TKey key)
    {
        return _values.TryGetValue(key, out var value) ? value : _nullDataFactory();
    }

    public void Remove(TKey key)
    {
        _values.Remove(key);
    }
}

internal sealed class DelegateTable<TData, TIndex> : ITable<TData>
    where TData : IXorType<TData>
    where TIndex : notnull
{
    private readonly IKeyEncodeTable<TIndex, TData> _dictionary;
    private readonly Func<TData, TIndex> _indexer;
    private readonly Func<TIndex, TData, bool> _pureTester;
    private readonly ModifiedIndexesKeeper<TIndex> _modifiedIndexes = new();
    private readonly int _bufferSize;

    public DelegateTable(
        IKeyEncodeTable<TIndex, TData> dictionary,
        Func<TData, TIndex> indexer,
        Func<TIndex, TData, bool> pureTester,
        int bufferSize)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _pureTester = pureTester ?? throw new ArgumentNullException(nameof(pureTester));
        _bufferSize = bufferSize;
    }

    public Buffer<TData> Decode()
    {
        var decodedData = Buffer<TData>.Rent(_bufferSize);
        var modifiedIndexesBuffer = _modifiedIndexes.GetModifiedIndexes();
        try
        {
            foreach (var index in modifiedIndexesBuffer)
            {
                var data = _dictionary.Get(index);
                if (_pureTester(index, data))
                {
                    _dictionary.Remove(index);
                    decodedData.Add(data);
                }
            }

            _modifiedIndexes.Clear();
            return decodedData;
        }
        finally
        {
            modifiedIndexesBuffer.Return();
        }
    }

    public void Encode(Buffer<TData> data)
    {
        foreach (var item in data)
        {
            var index = _indexer(item);
            _modifiedIndexes.MarkModified(index);
            _dictionary.KeyEncode(index, item);
        }
    }

    public TableState GetState()
    {
        return _modifiedIndexes.HasModifications ? TableState.Decoding : TableState.Finished;
    }

    public void ToDecode()
    {
    }
}

public sealed class TableBuilder<TData, TIndex>
    where TData : IXorType<TData>
    where TIndex : notnull
{
    private int? _tableSize;
    private int _bufferSize = 1024 * 8;
    private Func<TData>? _nullDataFactory;
    private Func<TData, TIndex>? _indexer;
    private Func<TIndex, TData, bool>? _pureTester;

    public TableBuilder<TData, TIndex> WithSize(int tableSize)
    {
        if (tableSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tableSize), "Table size must be greater than zero.");
        }

        _tableSize = tableSize;
        return this;
    }

    public TableBuilder<TData, TIndex> WithBufferSize(int bufferSize)
    {
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");
        }

        _bufferSize = bufferSize;
        return this;
    }

    public TableBuilder<TData, TIndex> WithNullData(Func<TData> nullDataFactory)
    {
        _nullDataFactory = nullDataFactory ?? throw new ArgumentNullException(nameof(nullDataFactory));
        return this;
    }

    public TableBuilder<TData, TIndex> WithIndexer(Func<TData, TIndex> computeIndex)
    {
        _indexer = computeIndex ?? throw new ArgumentNullException(nameof(computeIndex));
        return this;
    }

    public TableBuilder<TData, TIndex> WithIndexer(IIndexer<TIndex, TData> indexer)
    {
        if (indexer == null)
        {
            throw new ArgumentNullException(nameof(indexer));
        }

        _indexer = indexer.ComputeIndex;
        return this;
    }

    public TableBuilder<TData, TIndex> WithPureTester(Func<TIndex, TData, bool> isPure)
    {
        _pureTester = isPure ?? throw new ArgumentNullException(nameof(isPure));
        return this;
    }

    public TableBuilder<TData, TIndex> WithPureTester(IPureTester<TIndex, TData> pureTester)
    {
        if (pureTester == null)
        {
            throw new ArgumentNullException(nameof(pureTester));
        }

        _pureTester = pureTester.IsPure;
        return this;
    }

    public ITable<TData> Build()
    {
        if (!_tableSize.HasValue)
        {
            throw new InvalidOperationException("Table size must be configured before building.");
        }

        if (_nullDataFactory == null)
        {
            throw new InvalidOperationException("Null data factory must be configured before building.");
        }

        if (_indexer == null)
        {
            throw new InvalidOperationException("Indexer must be configured before building.");
        }

        if (_pureTester == null)
        {
            throw new InvalidOperationException("Pure tester must be configured before building.");
        }

        if (typeof(TIndex) == typeof(int))
        {
            return BuildIntTable(_tableSize.Value, _nullDataFactory, _indexer, _pureTester, _bufferSize);
        }

        return new DelegateTable<TData, TIndex>(
            new RuntimeKeyEncodeTable<TIndex, TData>(_nullDataFactory),
            _indexer,
            _pureTester,
            _bufferSize);
    }

    private static ITable<TData> BuildIntTable(
        int tableSize,
        Func<TData> nullDataFactory,
        Func<TData, TIndex> indexer,
        Func<TIndex, TData, bool> pureTester,
        int bufferSize)
    {
        var intIndexer = new Func<TData, int>(data => (int)(object)indexer(data)!);
        var intPureTester = new Func<int, TData, bool>((index, data) => pureTester((TIndex)(object)index, data));

        return new DelegateTable<TData, int>(
            new ObjectKeyEncodeTable<TData>(tableSize, nullDataFactory),
            intIndexer,
            intPureTester,
            bufferSize);
    }
}

public sealed class TablesBuilder<TData>
{
    private readonly List<ITable<TData>> _tables = new();
    private IDecodingControl<TData>? _decodingControl;

    public TablesBuilder<TData> WithDecodingControl(IDecodingControl<TData> decodingControl)
    {
        _decodingControl = decodingControl ?? throw new ArgumentNullException(nameof(decodingControl));
        return this;
    }

    public TablesBuilder<TData> Add(ITable<TData> table)
    {
        _tables.Add(table ?? throw new ArgumentNullException(nameof(table)));
        return this;
    }

    public TablesBuilder<TData> AddSwitch(Func<TData, int> bucketSelector, params ITable<TData>[] buckets)
    {
        if (buckets == null)
        {
            throw new ArgumentNullException(nameof(buckets));
        }

        if (buckets.Length == 0)
        {
            throw new ArgumentException("Switch table requires at least one bucket.", nameof(buckets));
        }

        return Add(new SwitchTable<TData>(buckets, bucketSelector));
    }

    public Tables<TData> Build()
    {
        if (_decodingControl == null)
        {
            throw new InvalidOperationException("Decoding control must be configured before building.");
        }

        return new Tables<TData>(new List<ITable<TData>>(_tables), _decodingControl);
    }
}

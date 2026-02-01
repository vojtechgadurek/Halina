using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halina.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<IBLTBenchmarks>();
    }
}

[MemoryDiagnoser]
public class IBLTBenchmarks
{
    [Params(100, 1_000, 10_000, 100_000, 1_000_000)]
    public int N;

    private Tables<ulong> _iblt;
    private Buffer<ulong> _dataBuffer;
    
    [GlobalSetup]
    public void GlobalSetup()
    {
        var data = GenerateUniqueData(N);
        _dataBuffer = Buffer<ulong>.Rent(N);
        foreach (var item in data)
        {
            _dataBuffer.Add(item);
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _dataBuffer.Return();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        int tableSize = (int)(N * 1.3);
        if (tableSize < 10) tableSize = 10;
        _iblt = IBLTFactory.GetStandardIBLT(3, tableSize);
    }

    [Benchmark]
    public void Encode()
    {
        _iblt.Encode(_dataBuffer);
    }

    [Benchmark]
    public void EncodeAndDecode()
    {
        _iblt.Encode(_dataBuffer);
        var decoded = _iblt.Decode();
        decoded.Return();
    }

    private List<ulong> GenerateUniqueData(int count)
    {
        var rng = new Random(42);
        var set = new HashSet<ulong>();
        while (set.Count < count)
        {
            byte[] buf = new byte[8];
            rng.NextBytes(buf);
            ulong val = BitConverter.ToUInt64(buf, 0);
            if (val != 0 && !set.Contains(val))
            {
                set.Add(val);
            }
        }
        return set.ToList();
    }
}

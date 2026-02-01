﻿using Xunit;
using Xunit.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Halina.Core;

namespace Halina.Tests;

public class IBLTTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;

    public IBLTTests(ITestOutputHelper output)
    {
        _output = output;
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Console.SetOut(new TestOutputTextWriter(_output));
        Console.SetError(new TestOutputTextWriter(_output));
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
    }

    [Fact]
    public void TestIBLT_SmallDatasets_Decoding()
    {
        // Define small dataset sizes
        int[] sizes = new int[] { 10, 20, 50 };

        foreach (var size in sizes)
        {
            // Requirement: size of the tables should be at least 1.3 / 3 the size of the data
            int tableSize = (int)(size * 1.3);  

            // Create IBLT with 3 tables (standard configuration)
            var iblt = IBLTFactory.GetStandardIBLT(1000, tableSize);

            // Generate unique non-zero data
            var data = Enumerable.Range(1, size).Select(i => (ulong)i).ToList();
            

            // Encode data
            var buffer = Buffer<UlongData>.Rent(size);
            foreach (var item in data)
            {
                buffer.Add(new UlongData(item));
                _output.WriteLine($"encoded item: {item}");

            }
            iblt.Encode(buffer);

            // Decode data
            var decodedBuffer = iblt.Decode();

            // Verify results
            var decodedSet = new HashSet<ulong>();
            foreach (var item in decodedBuffer)
            {
                _output.WriteLine($"decoded item: {item.Value}");
                if (decodedSet.Contains(item.Value))
                {
                    _output.WriteLine($"Duplicate decoded item found: {item.Value}");
                    decodedSet.Remove(item.Value);
                    continue;
                }
                else{
                    decodedSet.Add(item.Value);
                }
            }

            Assert.Equal(size, decodedSet.Count);
            foreach (var item in data)
            {
                Assert.Contains(item, decodedSet);
            }   

            // Cleanup
            buffer.Return();
            decodedBuffer.Return();
        }
    }

    private List<ulong> GenerateUniqueData(int count)
    {
        var rng = new Random(42 + count); // Deterministic seed
        var set = new HashSet<ulong>();
        while (set.Count < count)
        {
            byte[] buf = new byte[8];
            rng.NextBytes(buf);
            ulong val = BitConverter.ToUInt64(buf, 0);
            
            // Ensure no 0 values as per requirement and IBLT constraints
            if (val != 0 && !set.Contains(val))
            {
                set.Add(val);
            }
        }
        return set.ToList();
    }
}

internal class TestOutputTextWriter : TextWriter
{
    private readonly ITestOutputHelper _output;
    public TestOutputTextWriter(ITestOutputHelper output) { _output = output; }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Write(value.ToString());
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            _output.WriteLine(line);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        Write(new string(buffer, index, count));
    }
}

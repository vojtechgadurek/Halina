using Xunit;
using Xunit.Abstractions;
using Halina.Core;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Halina.Tests;

public class KmerIBLTTests
{
    private readonly ITestOutputHelper _output;
    public KmerIBLTTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestKmerIBLT_Correctness()
    {
        int mSequences = 10;
        int nKmers = 100;
        int kmerLength = 31;
        int totalItems = mSequences * nKmers;
        
        // Use ~1.5x overhead for the total table size
        int totalTableSize = (int)(totalItems * 1.3); 

        var generator = new KmerDataGenerator(123, kmerLength);
        var sequences = generator.GenerateSequences(mSequences, nKmers, 1);
        
        var allData = new List<KmerData>();
        foreach(var seq in sequences)
        {
            allData.AddRange(seq);
        }

        var iblt = KmerIBLTFactory.CreateKmerIBLT(3, kmerLength, totalTableSize);

        var buffer = Buffer<KmerData>.Rent(allData.Count);
        foreach(var item in allData)
        {
            buffer.Add(item);
        }

        iblt.Encode(buffer);

        var decodedBuffer = iblt.Decode();
        
        // Verification
        var decodedList = new HashSet<KmerData>();
        foreach(var item in decodedBuffer)
        {
            if (decodedList.Contains(item))
            {
                decodedList.Remove(item);
            }
            else
            {
                decodedList.Add(item);
            }
        }

        

        
        Assert.Equal(allData.Count, decodedList.Count);

        var expectedSet = new HashSet<string>();
        foreach(var item in allData)
        {
            expectedSet.Add(ItemToString(item));
        }

        foreach(var item in decodedList)
        {
            string s = ItemToString(item);
            Assert.Contains(s, expectedSet);
        }

        buffer.Return();
        decodedBuffer.Return();
    }

    private string ItemToString(KmerData data)
    {
        return $"{data.MetaData.SetId}-{data.MetaData.Index}-{data.Hash}-{data.Data.ToString()}";
    }
}

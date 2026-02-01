using System;
using System.Collections.Generic;
using System.Linq;

namespace Halina.Core;

public readonly record struct KmerMatch(KmerData SuffixOwner, KmerData PrefixOwner, int OverlapLength);

public sealed class Collider
{
    private readonly List<KmerData> _candidates;
    private readonly int _minOverlap;
    private readonly int _maxLength;
    private readonly KmerTabulationHash _hasher;
    private readonly List<KmerMatch> _matches = new();
    private readonly List<KmerData> _suffixMatches = new();
    private readonly List<KmerData> _prefixMatches = new();

    public Collider(IEnumerable<KmerData> kmers, int minimumOverlap, int seed = 123)
    {
        if (kmers == null) throw new ArgumentNullException(nameof(kmers));
        _candidates = kmers.ToList();
        _maxLength = _candidates.Count == 0 ? 0 : _candidates.Max(k => k.Data.Length);
        _minOverlap = Math.Max(1, minimumOverlap);
        _hasher = new KmerTabulationHash(seed);
    }

    public IReadOnlyList<KmerMatch> Matches => _matches.AsReadOnly();

    public IReadOnlyList<KmerData> SuffixMatches => _suffixMatches.AsReadOnly();

    public IReadOnlyList<KmerData> PrefixMatches => _prefixMatches.AsReadOnly();

    public IReadOnlyCollection<KmerData> MatchedKmers => _matches
        .SelectMany(match => new[] { match.SuffixOwner, match.PrefixOwner })
        .ToList()
        .AsReadOnly();

    public IReadOnlyList<KmerMatch> Run()
    {
        if (_maxLength == 0 || _minOverlap == 0 || _maxLength < _minOverlap)
        {
            return _matches;
        }

        var remaining = new HashSet<KmerData>(_candidates);

        for (int length = _maxLength; length >= _minOverlap && remaining.Count >= 2; length--)
        {
            var prefixIndex = BuildIndex(remaining, length, prefix: true);
            var suffixIndex = BuildIndex(remaining, length, prefix: false);

            foreach (var suffixBucket in suffixIndex)
            {
                if (!prefixIndex.TryGetValue(suffixBucket.Key, out var prefixBucket))
                {
                    continue;
                }

                var suffixNode = suffixBucket.Value.Last;
                while (suffixNode != null)
                {
                    var currentSuffix = suffixNode.Value;
                    var nextSuffix = suffixNode.Previous;

                    if (!remaining.Contains(currentSuffix))
                    {
                        suffixBucket.Value.Remove(suffixNode);
                        suffixNode = nextSuffix;
                        continue;
                    }

                    var prefixNode = prefixBucket.First;
                    while (prefixNode != null && (prefixNode.Value.Equals(currentSuffix) || !remaining.Contains(prefixNode.Value)))
                    {
                        prefixNode = prefixNode.Next;
                    }

                    if (prefixNode == null)
                    {
                        suffixNode = nextSuffix;
                        continue;
                    }

                    var matchedPrefix = prefixNode.Value;
                    prefixBucket.Remove(prefixNode);
                    suffixBucket.Value.Remove(suffixNode);
                    remaining.Remove(currentSuffix);
                    remaining.Remove(matchedPrefix);
                    _suffixMatches.Add(currentSuffix);
                    _prefixMatches.Add(matchedPrefix);
                    _matches.Add(new KmerMatch(currentSuffix, matchedPrefix, length));
                    break;
                }
            }
        }

        return _matches;
    }

    public IReadOnlyList<KmerData> GetBridge(KmerMatch match)
    {
        if (_matches.Count == 0 || !_matches.Contains(match))
        {
            throw new InvalidOperationException("The requested match is not part of the most recent collision run.");
        }

        Console.WriteLine(match.OverlapLength);
        string suffix = match.SuffixOwner.Data.ToString();
        Console.WriteLine(suffix);
        string prefix = match.PrefixOwner.Data.ToString();
        Console.WriteLine(prefix);
        string overlap = suffix.Substring(suffix.Length - match.OverlapLength);
        Console.WriteLine(overlap);


        if (match.OverlapLength > suffix.Length || match.OverlapLength > prefix.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(match), "Overlap length cannot exceed the kmer size of either endpoint.");
        }

        string tail = prefix.Substring(match.OverlapLength);
        string connector = suffix + tail;
        int kmerLength = match.SuffixOwner.Data.Length;
        var bridge = new List<KmerData>();

        for (int start = 0; start + kmerLength <= connector.Length; start++)
        {
            string window = connector.Substring(start, kmerLength);
            bridge.Add(BuildKmerData(window));
        }

        return bridge.AsReadOnly();
    }

    private Dictionary<ulong, LinkedList<KmerData>> BuildIndex(HashSet<KmerData> source, int length, bool prefix)
    {
        var index = new Dictionary<ulong, LinkedList<KmerData>>();
        foreach (var data in source)
        {
            if (data.Data.Length < length)
            {
                continue;
            }
            int offset = prefix ? 0 : data.Data.Length - length;
            ulong hash = ComputeSegmentHash(data.Data, offset, length);
            if (!index.TryGetValue(hash, out var bucket))
            {
                bucket = new LinkedList<KmerData>();
                index[hash] = bucket;
            }
            bucket.AddLast(data);
        }
        return index;
    }

    private ulong ComputeSegmentHash(Kmer kmer, int offset, int length)
    {
        if (length == 0)
        {
            return 0UL;
        }

        var window = new Kmer(length);
        for (int i = 0; i < length; i++)
        {
            window.SetNucleotide(i, kmer.GetNucleotide(offset + i));
        }
        return _hasher.ComputeHash(window);
    }

    private KmerData BuildKmerData(string sequence)
    {
        var kmer = new Kmer(sequence);
        return new KmerData
        {
            Data = kmer,
            Hash = _hasher.ComputeHash(kmer),
            MetaData = new KmerMetaData()
        };
    }
}

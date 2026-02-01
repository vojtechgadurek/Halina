using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Halina.Experiments;
using System.Linq;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Halina.Experiments;

public class RangeConfig
{
    public double Start { get; set; }
    public double End { get; set; }
    public double Step { get; set; } = 1.0;
    public bool Multiplicative { get; set; }

    public IEnumerable<int> Values()
    {
        double step = Step <= 0 ? 1.0 : Step;
        if (Start > End)
        {
            yield break;
        }

        bool useMultiplicative = Multiplicative && step > 1.0 + 1e-12;

        if (useMultiplicative)
        {
            double current = Start;
            int? previous = null;

            while (current <= End + 1e-9)
            {
                int candidate = (int)Math.Round(current);
                if (!previous.HasValue || candidate != previous.Value)
                {
                    yield return candidate;
                    previous = candidate;
                }

                current *= step;
                if (current == 0)
                {
                    break;
                }
            }
        }
        else
        {
            double current = Start;
            while (current <= End + 1e-9)
            {
                yield return (int)Math.Round(current);
                current += step;
            }
        }
    }
}

public class KmerExperimentConfig
{
    public string Path { get; set; } = "results";
    public RangeConfig Seed { get; set; } = new() { Start = 123, End = 123, Multiplicative = true };
    public RangeConfig L { get; set; } = new() { Start = 10, End = 10, Multiplicative = true };
    public RangeConfig K { get; set; } = new() { Start = 15, End = 15, Multiplicative = true };
    public RangeConfig KmerLength { get; set; } = new() { Start = 31, End = 31 };
    public RangeConfig NSequences { get; set; } = new() { Start = 200, End = 200 };
    public RangeConfig SequenceLength { get; set; } = new() { Start = 100, End = 100 };
    public RangeConfig MaxDistance { get; set; } = new() { Start = 0, End = 0, Step = 1 };
}

public class MutationExperimentConfig
{
    public string Path { get; set; } = "results_mutation";
    public RangeConfig Seed { get; set; } = new() { Start = 123, End = 123, Multiplicative = true };
    public RangeConfig M { get; set; } = new() { Start = 15, End = 15, Multiplicative = true }; // Corresponds to K in KmerExp
    public RangeConfig L { get; set; } = new() { Start = 10, End = 10, Multiplicative = true };
    public RangeConfig KmerLength { get; set; } = new() { Start = 31, End = 31 };
    public RangeConfig NSequences { get; set; } = new() { Start = 200, End = 200 };

}

public class HashSetExtendedConfig
{
    public string Path { get; set; } = "results_hashset_extended";
    public RangeConfig Seed { get; set; } = new() { Start = 123, End = 123, Multiplicative = true };
    public RangeConfig L { get; set; } = new() { Start = 10, End = 10, Multiplicative = true };
    public RangeConfig K { get; set; } = new() { Start = 15, End = 15, Multiplicative = true };
    public RangeConfig KmerLength { get; set; } = new() { Start = 31, End = 31 };
    public RangeConfig NSequences { get; set; } = new() { Start = 200, End = 200 };
    public RangeConfig SequenceLength { get; set; } = new() { Start = 100, End = 100 };
    public RangeConfig SamplingStages { get; set; } = new() { Start = 3, End = 5, Multiplicative = true };
    public RangeConfig MaxDistance { get; set; } = new() { Start = 0, End = 0, Step = 1 };
    public double ShrinkFactor { get; set; } = 1.5;
}

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- kmer [config.json] [--parallel] [--max-concurrency N]");
            Console.WriteLine("  dotnet run -- mutation [config.json]");
            Console.WriteLine("  dotnet run -- hashset-extended [config.json] [--parallel] [--max-concurrency N]");
            return;
        }

        string mode = args[0].ToLower();
        string? configPath = null;
        bool useParallel = false;
        int? maxConcurrency = null;
        for (int i = 1; i < args.Length; i++)
        {
            var current = args[i];
            if (current.Equals("--parallel", StringComparison.OrdinalIgnoreCase))
            {
                useParallel = true;
                continue;
            }

            if (current.Equals("--max-concurrency", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    maxConcurrency = parsed;
                }
                continue;
            }

            if (configPath == null)
            {
                configPath = current;
            }
        }

        if (mode == "kmer")
        {
            RunKmerExperiments(configPath, useParallel, maxConcurrency);
        }
        else if (mode == "mutation")
        {
            RunMutationExperiments(configPath);
        }
        else if (mode == "hashset-extended")
        {
            RunHashSetPredictorExtended(configPath, useParallel, maxConcurrency);
        }
        else
        {
            Console.WriteLine($"Unknown mode: {mode}");
        }
    }

    private static void RunKmerExperiments(string? configPath, bool useParallel, int? maxConcurrency)
    {
        KmerExperimentConfig config;
        if (configPath != null && File.Exists(configPath))
        {
            Console.WriteLine($"Loading Kmer configuration from {configPath}");
            string json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<KmerExperimentConfig>(json) ?? new KmerExperimentConfig();
        }
        else
        {
            Console.WriteLine("Using default Kmer configuration.");
            config = new KmerExperimentConfig();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText("kmer_config_default.json", JsonSerializer.Serialize(config, options));
            Console.WriteLine("Created 'kmer_config_default.json' template.");
        }

        Directory.CreateDirectory(config.Path);
        var specs = BuildKmerExperimentSpecs(config).ToList();
        ExecuteKmerExperiments(specs, config, useParallel, maxConcurrency);
    }

    private static void RunMutationExperiments(string? configPath)
    {
        MutationExperimentConfig config;
        if (configPath != null && File.Exists(configPath))
        {
            Console.WriteLine($"Loading Mutation configuration from {configPath}");
            string json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<MutationExperimentConfig>(json) ?? new MutationExperimentConfig();
        }
        else
        {
            Console.WriteLine("Using default Mutation configuration.");
            config = new MutationExperimentConfig();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText("mutation_config_default.json", JsonSerializer.Serialize(config, options));
            Console.WriteLine("Created 'mutation_config_default.json' template.");
        }

        int count = 0;
        foreach (var seed in config.Seed.Values())
        foreach (var m in config.M.Values())
        foreach (var l in config.L.Values())
        foreach (var kmerLen in config.KmerLength.Values())
        foreach (var nSeq in config.NSequences.Values())
        {
            Console.WriteLine($"Running Mutation Experiment: Seed={seed}, M={m}, L={l}, Kmer={kmerLen}, NSeq={nSeq}");
            try
            {
                var result = MutationExperiments.Run(kmerLen, m, l, nSeq, seed);
                Console.WriteLine($"Result: Total={result.TotalRecoveredKmers}, Correct={result.CorrectlyIdentifiedMutations}, Incorrect={result.IncorrectlyIdentifiedMutations}, Missed={result.MissedMutations}");
                // TODO: Save mutation results to JSON if needed
                count++;
            }
            catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
        }
        Console.WriteLine($"Completed {count} experiments.");
    }

    private static void RunHashSetPredictorExtended(string? configPath, bool useParallel, int? maxConcurrency)
    {
        HashSetExtendedConfig config;
        if (configPath != null && File.Exists(configPath))
        {
            Console.WriteLine($"Loading HashSet-extended configuration from {configPath}");
            string json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<HashSetExtendedConfig>(json) ?? new HashSetExtendedConfig();
        }
        else
        {
            Console.WriteLine("Using default HashSet-extended configuration.");
            config = new HashSetExtendedConfig();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText("hashset_extended_config_default.json", JsonSerializer.Serialize(config, options));
            Console.WriteLine("Created 'hashset_extended_config_default.json' template.");
        }

        Directory.CreateDirectory(config.Path);
        var specs = BuildExtendedExperimentSpecs(config).ToList();
        ExecuteHashSetExtendedExperiments(specs, config, useParallel, maxConcurrency);
    }

    private static void SaveResult(ExperimentResult result, string saveDirectory)
    {
        Directory.CreateDirectory(saveDirectory);
        string filename = $"v={result.Version}_k={result.Arguments.K}_l={result.Arguments.L}_kmer={result.Arguments.KmerSize}_nseq={result.Arguments.NSequences}_len={result.Arguments.SequenceLength}_tbl={result.Arguments.TableSize}_seed={result.Arguments.Seed}_maxdist={result.Arguments.MaxDistance}.json";
        
        filename = Path.Combine(saveDirectory, filename);
        Console.WriteLine($"Saving result to {filename}");

        

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(result, options);
        File.WriteAllText(filename, json);
        TrackFileInCache(saveDirectory, filename);
        Console.WriteLine($"Saved result to {filename}");
    }

    private static void SaveExtendedResult(ExtendedExperimentResult result, string saveDirectory)
    {
        Directory.CreateDirectory(saveDirectory);
        var shrinkLabel = result.Arguments.ShrinkFactor.ToString("0.##", CultureInfo.InvariantCulture);
        string filename = $"v={result.Version}_k={result.Arguments.K}_l={result.Arguments.L}_kmer={result.Arguments.KmerSize}_nseq={result.Arguments.NSequences}_len={result.Arguments.SequenceLength}_stages={result.Arguments.SamplingStages}_shrink={shrinkLabel}_maxdist={result.Arguments.MaxDistance}_seed={result.Arguments.Seed}.json";
        filename = Path.Combine(saveDirectory, filename);
        Console.WriteLine($"Saving result to {filename}");

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(result, options);
        File.WriteAllText(filename, json);
        TrackFileInCache(saveDirectory, filename);
        Console.WriteLine($"Saved result to {filename}");
    }

    private record struct KmerExperimentSpec(int Seed, int L, int K, int KmerLength, int NSequences, int SequenceLength, int MaxDistance);
    private record struct HashSetExtendedSpec(int Seed, int L, int K, int KmerLength, int NSequences, int SequenceLength, int SamplingStages, double ShrinkFactor, int MaxDistance);
    private static readonly ConcurrentDictionary<string, DirectoryCache> DirectoryFilesCache = new();

    private static IEnumerable<KmerExperimentSpec> BuildKmerExperimentSpecs(KmerExperimentConfig config)
    {
        var specs = new List<KmerExperimentSpec>();
        foreach (var seed in config.Seed.Values())
        foreach (var l in config.L.Values())
        foreach (var k in config.K.Values())
        foreach (var kmerLen in config.KmerLength.Values())
        foreach (var nSeq in config.NSequences.Values())
        foreach (var seqLen in config.SequenceLength.Values())
        foreach (var distance in config.MaxDistance.Values())
        {
            specs.Add(new(seed, l, k, kmerLen, nSeq, seqLen, distance));
        }

        return specs;
    }

    private static void ExecuteKmerExperiments(IReadOnlyList<KmerExperimentSpec> specs, KmerExperimentConfig config, bool useParallel, int? maxConcurrency)
    {
        int total = specs.Count;
        if (total == 0)
        {
            Console.WriteLine("No K-mer experiment specs defined.");
            return;
        }

        Console.WriteLine($"Running {total} K-mer experiments {(useParallel ? "(parallel)" : "(sequential)")}.");
        int completed = 0;
        int processed = 0;
        var progressLock = new object();
        Action<KmerExperimentSpec> work = spec =>
        {
            if (ProcessKmerExperiment(spec, config))
            {
                Interlocked.Increment(ref completed);
            }

            int processedSoFar = Interlocked.Increment(ref processed);
            DrawProgress(processedSoFar, total, progressLock);
        };

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxConcurrency ?? Environment.ProcessorCount) };
        if (useParallel)
        {
            Parallel.ForEach(specs, parallelOptions, work);
        }
        else
        {
            foreach (var spec in specs)
            {
                work(spec);
            }
        }

        Console.WriteLine($"Completed {completed} experiments.");
    }

    private static bool ProcessKmerExperiment(KmerExperimentSpec spec, KmerExperimentConfig config)
    {
        var pattern = BuildKmerCachePattern(spec.K, spec.L, spec.KmerLength, spec.NSequences, spec.SequenceLength, spec.Seed, spec.MaxDistance);
        if (ResultAlreadyCached(config.Path, pattern))
        {
            Console.WriteLine("Skipping run because cached result already exists.");
            return false;
        }

        Console.WriteLine($"Running Kmer Experiment: Seed={spec.Seed}, L={spec.L}, K={spec.K}, Kmer={spec.KmerLength}, NSeq={spec.NSequences}, SeqLen={spec.SequenceLength}, MaxDistance={spec.MaxDistance}");
        try
        {
            var result = KmerExperiments.RunExperiment(spec.KmerLength, spec.NSequences, spec.SequenceLength, spec.K, spec.L, spec.Seed, spec.MaxDistance);
            Console.WriteLine($"Experiment finished in {result.Result.DurationMs:F2} ms (Gen: {result.Result.DataGenerationDurationMs:F2} ms)");
            SaveResult(result, config.Path);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }

    private static IEnumerable<HashSetExtendedSpec> BuildExtendedExperimentSpecs(HashSetExtendedConfig config)
    {
        var specs = new List<HashSetExtendedSpec>();
        foreach (var seed in config.Seed.Values())
        foreach (var l in config.L.Values())
        foreach (var k in config.K.Values())
        foreach (var kmerLen in config.KmerLength.Values())
        foreach (var nSeq in config.NSequences.Values())
        foreach (var seqLen in config.SequenceLength.Values())
        foreach (var stages in config.SamplingStages.Values())
        foreach (var maxDistance in config.MaxDistance.Values())
        {
            specs.Add(new(seed, l, k, kmerLen, nSeq, seqLen, stages, config.ShrinkFactor, maxDistance));
        }

        return specs;
    }

    private static void ExecuteHashSetExtendedExperiments(IReadOnlyList<HashSetExtendedSpec> specs, HashSetExtendedConfig config, bool useParallel, int? maxConcurrency)
    {
        int total = specs.Count;
        if (total == 0)
        {
            Console.WriteLine("No HashSet-extended experiment specs defined.");
            return;
        }

        Console.WriteLine($"Running {total} HashSet-extended experiments {(useParallel ? "(parallel)" : "(sequential)")}.");
        int completed = 0;
        int processed = 0;
        var progressLock = new object();
        Action<HashSetExtendedSpec> work = spec =>
        {
            if (ProcessHashSetExtendedSpec(spec, config))
            {
                Interlocked.Increment(ref completed);
            }

            int processedSoFar = Interlocked.Increment(ref processed);
            DrawProgress(processedSoFar, total, progressLock);
        };

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxConcurrency ?? Environment.ProcessorCount) };
        if (useParallel)
        {
            Parallel.ForEach(specs, parallelOptions, work);
        }
        else
        {
            foreach (var spec in specs)
            {
                work(spec);
            }
        }

        Console.WriteLine($"Completed {completed} experiments.");
    }

    private static bool ProcessHashSetExtendedSpec(HashSetExtendedSpec spec, HashSetExtendedConfig config)
    {
        var prefix = BuildExtendedResultPrefix(spec.K, spec.L, spec.KmerLength, spec.NSequences, spec.SequenceLength, spec.SamplingStages, spec.ShrinkFactor, spec.MaxDistance);
        var pattern = BuildExtendedCachePattern(prefix, spec.Seed);
        if (ResultAlreadyCached(config.Path, pattern))
        {
            Console.WriteLine("Skipping run because cached result already exists.");
            return false;
        }

        Console.WriteLine($"Running extended HashSet predictor: Seed={spec.Seed}, L={spec.L}, K={spec.K}, Kmer={spec.KmerLength}, NSeq={spec.NSequences}, SeqLen={spec.SequenceLength}, Stages={spec.SamplingStages}, MaxDistance={spec.MaxDistance}");
        try
        {
            var result = HashSetPredictorExtended.Run(spec.KmerLength, spec.NSequences, spec.SequenceLength, spec.K, spec.L, spec.SamplingStages, spec.ShrinkFactor, spec.Seed, spec.MaxDistance);
            Console.WriteLine($"Experiment finished in {result.Result.DurationMs:F2} ms (Gen: {result.Result.DataGenerationDurationMs:F2} ms)");
            SaveExtendedResult(result, config.Path);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }

    private static string BuildKmerCachePattern(int k, int l, int kmerLen, int nSeq, int seqLen, int seed, int maxDistance)
    {
        // Table size is always deterministic (seqLen * kmerLen), so omit it from the glob pattern.
        return $"v=v1_k={k}_l={l}_kmer={kmerLen}_nseq={nSeq}_len={seqLen}_tbl={ (1 + seqLen) *kmerLen}_seed={seed}_maxdist={maxDistance}.json";
    }

    private static string BuildExtendedResultPrefix(int k, int l, int kmerLen, int nSeq, int seqLen, int stages, double shrinkFactor, int maxDistance)
    {
        var shrinkLabel = shrinkFactor.ToString("0.##", CultureInfo.InvariantCulture);
        return $"v=v2_k={k}_l={l}_kmer={kmerLen}_nseq={nSeq}_len={seqLen}_stages={stages}_shrink={shrinkLabel}_maxdist={maxDistance}";
    }

    private static string BuildExtendedCachePattern(string prefix, int seed)
    {
        return $"{prefix}_seed={seed}.json";
    }

    private static bool ResultAlreadyCached(string saveDirectory, string searchPattern)
    {
        Console.WriteLine($"Checking cache for pattern: {searchPattern}");
        return GetDirectoryCache(saveDirectory).ContainsPattern(searchPattern);
    }

    private static DirectoryCache GetDirectoryCache(string saveDirectory)
        => DirectoryFilesCache.GetOrAdd(saveDirectory, dir =>
        {
            var cache = new DirectoryCache();
            cache.Load(dir);
            return cache;
        });

    private static void TrackFileInCache(string saveDirectory, string filePath)
        => GetDirectoryCache(saveDirectory).Add(filePath);

    private static void DrawProgress(int current, int total, object sync)
    {
        const int barWidth = 40;
        double ratio = total == 0 ? 1.0 : (double)current / total;
        int filled = (int)Math.Round(ratio * barWidth);
        string bar = new string('#', filled).PadRight(barWidth);
        lock (sync)
        {
            Console.Write($"\rProgress: [{bar}] {current}/{total}");
            if (current >= total)
            {
                Console.WriteLine();
            }
        }
    }

    private sealed class DirectoryCache
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void Load(string directory)
        {
            lock (_sync)
            {
                _files.Clear();
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    _files.Add(Path.GetFileName(file));
                    Console.WriteLine($"Cached existing file: {Path.GetFileName(file)}");
                }
            }
        }

        public bool ContainsPattern(string pattern)
        {
        return _files.Contains(pattern);
        }

        public void Add(string filePath)
        {
            lock (_sync)
            {
                _files.Add(Path.GetFileName(filePath));
            }
        }
    }
}

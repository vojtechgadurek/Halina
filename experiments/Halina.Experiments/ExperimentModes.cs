using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            yield break;
        }

        double additive = Start;
        while (additive <= End + 1e-9)
        {
            yield return (int)Math.Round(additive);
            additive += step;
        }
    }
}

public class DoubleRangeConfig
{
    public double Start { get; set; }
    public double End { get; set; }
    public double Step { get; set; } = 1.0;

    public IEnumerable<double> Values()
    {
        double step = Step <= 0 ? 1.0 : Step;
        if (Start > End)
        {
            yield break;
        }

        int index = 0;
        while (true)
        {
            double value = Start + index * step;
            if (value > End + 1e-12)
            {
                yield break;
            }

            yield return Math.Round(value, 10, MidpointRounding.AwayFromZero);
            index++;
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
    public RangeConfig M { get; set; } = new() { Start = 15, End = 15, Multiplicative = true };
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

public sealed class KmerExperimentMode : IExperimentMode
{
    private readonly record struct KmerExperimentSpec(int Seed, int L, int K, int KmerLength, int NSequences, int SequenceLength, int MaxDistance);

    public string Name => "kmer";
    public string Usage => "kmer [config.json] [--parallel] [--max-concurrency N]";
    public string Description => "Run batched K-mer experiments and persist JSON results.";

    public ExperimentRunSummary Run(ExperimentInvocation invocation)
    {
        var stopwatch = Stopwatch.StartNew();
        var config = ExperimentModeSupport.LoadConfigOrCreateDefault<KmerExperimentConfig>(
            invocation.PrimaryArgument,
            "kmer_config_default.json",
            "K-mer");

        Directory.CreateDirectory(config.Path);
        var specs = BuildSpecs(config).ToList();
        int successful = ExperimentModeSupport.ExecuteSpecs(
            specs,
            invocation.UseParallel,
            invocation.MaxConcurrency,
            "No K-mer experiment specs defined.",
            $"Running {specs.Count} K-mer experiments {(invocation.UseParallel ? "(parallel)" : "(sequential)")}.",
            spec => ProcessSpec(spec, config));

        stopwatch.Stop();
        return new ExperimentRunSummary(Name, specs.Count, successful, Math.Max(0, specs.Count - successful), stopwatch.Elapsed.TotalMilliseconds);
    }

    private static IEnumerable<KmerExperimentSpec> BuildSpecs(KmerExperimentConfig config)
    {
        foreach (var seed in config.Seed.Values())
        foreach (var l in config.L.Values())
        foreach (var k in config.K.Values())
        foreach (var kmerLength in config.KmerLength.Values())
        foreach (var nSequences in config.NSequences.Values())
        foreach (var sequenceLength in config.SequenceLength.Values())
        foreach (var maxDistance in config.MaxDistance.Values())
        {
            yield return new KmerExperimentSpec(seed, l, k, kmerLength, nSequences, sequenceLength, maxDistance);
        }
    }

    private static bool ProcessSpec(KmerExperimentSpec spec, KmerExperimentConfig config)
    {
        string pattern = BuildCachePattern(spec.K, spec.L, spec.KmerLength, spec.NSequences, spec.SequenceLength, spec.Seed, spec.MaxDistance);
        if (ExperimentModeSupport.ResultAlreadyCached(config.Path, pattern))
        {
            Console.WriteLine("Skipping run because cached result already exists.");
            return true;
        }

        Console.WriteLine(
            $"Running Kmer Experiment: Seed={spec.Seed}, L={spec.L}, K={spec.K}, " +
            $"Kmer={spec.KmerLength}, NSeq={spec.NSequences}, SeqLen={spec.SequenceLength}, MaxDistance={spec.MaxDistance}");

        try
        {
            var result = KmerExperiments.RunExperiment(
                spec.KmerLength,
                spec.NSequences,
                spec.SequenceLength,
                spec.K,
                spec.L,
                spec.Seed,
                spec.MaxDistance);

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

    private static string BuildCachePattern(int k, int l, int kmerLength, int nSequences, int sequenceLength, int seed, int maxDistance)
    {
        int tableSize = 2 * nSequences * Math.Max(0, sequenceLength - kmerLength + 1);
        return $"v=v1_k={k}_l={l}_kmer={kmerLength}_nseq={nSequences}_len={sequenceLength}_tbl={tableSize}_seed={seed}_maxdist={maxDistance}.json";
    }

    private static void SaveResult(ExperimentResult result, string saveDirectory)
    {
        string filename =
            $"v={result.Version}_k={result.Arguments.K}_l={result.Arguments.L}_kmer={result.Arguments.KmerSize}" +
            $"_nseq={result.Arguments.NSequences}_len={result.Arguments.SequenceLength}_tbl={result.Arguments.TableSize}" +
            $"_seed={result.Arguments.Seed}_maxdist={result.Arguments.MaxDistance}.json";

        ExperimentModeSupport.SaveJson(result, saveDirectory, filename);
    }
}

public sealed class MutationExperimentMode : IExperimentMode
{
    public string Name => "mutation";
    public string Usage => "mutation [config.json]";
    public string Description => "Run mutation reconstruction experiments.";

    public ExperimentRunSummary Run(ExperimentInvocation invocation)
    {
        var stopwatch = Stopwatch.StartNew();
        var config = ExperimentModeSupport.LoadConfigOrCreateDefault<MutationExperimentConfig>(
            invocation.PrimaryArgument,
            "mutation_config_default.json",
            "Mutation");

        var cases = new List<ExperimentCaseSummary>();
        foreach (var seed in config.Seed.Values())
        foreach (var m in config.M.Values())
        foreach (var l in config.L.Values())
        foreach (var kmerLength in config.KmerLength.Values())
        foreach (var nSequences in config.NSequences.Values())
        {
            string name = $"seed={seed},m={m},l={l},kmer={kmerLength},nseq={nSequences}";
            Console.WriteLine($"Running Mutation Experiment: {name}");
            var caseStopwatch = Stopwatch.StartNew();
            try
            {
                var result = MutationExperiments.Run(kmerLength, m, l, nSequences, seed);
                caseStopwatch.Stop();
                string details =
                    $"Correct={result.CorrectlyIdentifiedMutations}, Incorrect={result.IncorrectlyIdentifiedMutations}, Missed={result.MissedMutations}";
                Console.WriteLine($"Result: Total={result.TotalRecoveredKmers}, {details}");
                cases.Add(new ExperimentCaseSummary(name, true, result.TotalRecoveredKmers, result.TotalRecoveredKmers, caseStopwatch.Elapsed.TotalMilliseconds, details));
            }
            catch (Exception ex)
            {
                caseStopwatch.Stop();
                Console.WriteLine($"Error: {ex.Message}");
                cases.Add(new ExperimentCaseSummary(name, false, Details: ex.Message, DurationMs: caseStopwatch.Elapsed.TotalMilliseconds));
            }
        }

        stopwatch.Stop();
        return BuildSummary(Name, stopwatch.Elapsed.TotalMilliseconds, cases);
    }

    private static ExperimentRunSummary BuildSummary(string mode, double durationMs, IReadOnlyList<ExperimentCaseSummary> cases)
    {
        int successful = cases.Count(entry => entry.Succeeded);
        return new ExperimentRunSummary(mode, cases.Count, successful, cases.Count - successful, durationMs, cases);
    }
}

public sealed class HashSetExtendedExperimentMode : IExperimentMode
{
    private readonly record struct HashSetExtendedSpec(
        int Seed,
        int L,
        int K,
        int KmerLength,
        int NSequences,
        int SequenceLength,
        int SamplingStages,
        double ShrinkFactor,
        int MaxDistance);

    public string Name => "hashset-extended";
    public string Usage => "hashset-extended [config.json] [--parallel] [--max-concurrency N]";
    public string Description => "Run staged hash-set predictor experiments and persist JSON results.";

    public ExperimentRunSummary Run(ExperimentInvocation invocation)
    {
        var stopwatch = Stopwatch.StartNew();
        var config = ExperimentModeSupport.LoadConfigOrCreateDefault<HashSetExtendedConfig>(
            invocation.PrimaryArgument,
            "hashset_extended_config_default.json",
            "HashSet-extended");

        Directory.CreateDirectory(config.Path);
        var specs = BuildSpecs(config).ToList();
        int successful = ExperimentModeSupport.ExecuteSpecs(
            specs,
            invocation.UseParallel,
            invocation.MaxConcurrency,
            "No HashSet-extended experiment specs defined.",
            $"Running {specs.Count} HashSet-extended experiments {(invocation.UseParallel ? "(parallel)" : "(sequential)")}.",
            spec => ProcessSpec(spec, config));

        stopwatch.Stop();
        return new ExperimentRunSummary(Name, specs.Count, successful, Math.Max(0, specs.Count - successful), stopwatch.Elapsed.TotalMilliseconds);
    }

    private static IEnumerable<HashSetExtendedSpec> BuildSpecs(HashSetExtendedConfig config)
    {
        foreach (var seed in config.Seed.Values())
        foreach (var l in config.L.Values())
        foreach (var k in config.K.Values())
        foreach (var kmerLength in config.KmerLength.Values())
        foreach (var nSequences in config.NSequences.Values())
        foreach (var sequenceLength in config.SequenceLength.Values())
        foreach (var samplingStages in config.SamplingStages.Values())
        foreach (var maxDistance in config.MaxDistance.Values())
        {
            yield return new HashSetExtendedSpec(seed, l, k, kmerLength, nSequences, sequenceLength, samplingStages, config.ShrinkFactor, maxDistance);
        }
    }

    private static bool ProcessSpec(HashSetExtendedSpec spec, HashSetExtendedConfig config)
    {
        string prefix = BuildResultPrefix(spec.K, spec.L, spec.KmerLength, spec.NSequences, spec.SequenceLength, spec.SamplingStages, spec.ShrinkFactor, spec.MaxDistance);
        string pattern = $"{prefix}_seed={spec.Seed}.json";
        if (ExperimentModeSupport.ResultAlreadyCached(config.Path, pattern))
        {
            Console.WriteLine("Skipping run because cached result already exists.");
            return true;
        }

        Console.WriteLine(
            $"Running extended HashSet predictor: Seed={spec.Seed}, L={spec.L}, K={spec.K}, Kmer={spec.KmerLength}, " +
            $"NSeq={spec.NSequences}, SeqLen={spec.SequenceLength}, Stages={spec.SamplingStages}, MaxDistance={spec.MaxDistance}");

        try
        {
            var result = HashSetPredictorExtended.Run(
                spec.KmerLength,
                spec.NSequences,
                spec.SequenceLength,
                spec.K,
                spec.L,
                spec.SamplingStages,
                spec.ShrinkFactor,
                spec.Seed,
                spec.MaxDistance);

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

    private static string BuildResultPrefix(int k, int l, int kmerLength, int nSequences, int sequenceLength, int stages, double shrinkFactor, int maxDistance)
    {
        string shrink = shrinkFactor.ToString("0.##", CultureInfo.InvariantCulture);
        return $"v=v2_k={k}_l={l}_kmer={kmerLength}_nseq={nSequences}_len={sequenceLength}_stages={stages}_shrink={shrink}_maxdist={maxDistance}";
    }

    private static void SaveResult(ExtendedExperimentResult result, string saveDirectory)
    {
        string shrink = result.Arguments.ShrinkFactor.ToString("0.##", CultureInfo.InvariantCulture);
        string filename =
            $"v={result.Version}_k={result.Arguments.K}_l={result.Arguments.L}_kmer={result.Arguments.KmerSize}" +
            $"_nseq={result.Arguments.NSequences}_len={result.Arguments.SequenceLength}_stages={result.Arguments.SamplingStages}" +
            $"_shrink={shrink}_maxdist={result.Arguments.MaxDistance}_seed={result.Arguments.Seed}.json";

        ExperimentModeSupport.SaveJson(result, saveDirectory, filename);
    }
}

public sealed class ResultsAnalysisExperimentMode : IExperimentMode
{
    public string Name => "analyze";
    public string Usage => "analyze [results_directory] [output_csv]";
    public string Description => "Aggregate experiment JSON files into an analysis CSV.";

    public ExperimentRunSummary Run(ExperimentInvocation invocation)
    {
        var stopwatch = Stopwatch.StartNew();
        string directory = string.IsNullOrWhiteSpace(invocation.PrimaryArgument) ? "results" : invocation.PrimaryArgument;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Results directory '{directory}' does not exist.");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var experiments = new List<ExperimentResult>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var text = File.ReadAllText(file);
                var result = JsonSerializer.Deserialize<ExperimentResult>(text, options);
                if (result != null)
                {
                    experiments.Add(result);
                }
            }
            catch (JsonException)
            {
            }
        }

        if (experiments.Count == 0)
        {
            throw new InvalidOperationException($"No experiment results could be read from '{directory}'.");
        }

        var statsByPair = new Dictionary<(int K, int L), StatsAccumulator>();
        foreach (var experiment in experiments)
        {
            double successRate = (double)experiment.Result.CorrectlyRecovered / Math.Max(1, experiment.Result.TotalItems);
            double duration = experiment.Result.DurationMs;
            var key = (experiment.Arguments.K, experiment.Arguments.L);
            if (!statsByPair.TryGetValue(key, out var accumulator))
            {
                accumulator = new StatsAccumulator();
                statsByPair[key] = accumulator;
            }

            accumulator.Add(successRate, duration);
        }

        var lines = new List<string>
        {
            "K,L,AvgSuccessRate,VarianceSuccessRate,AvgDurationMs,VarianceDurationMs,SampleCount,Ratio,AvgRecoveryFraction,VarianceRecoveryFraction"
        };

        foreach (var entry in statsByPair.OrderBy(item => item.Key.K).ThenBy(item => item.Key.L))
        {
            var (k, l) = entry.Key;
            var stats = entry.Value;
            double ratio = (double)(k * 1.5 + l) / Math.Max(1, stats.Count);
            lines.Add(
                $"{k},{l},{stats.AverageSuccessRate:0.0000},{stats.VarianceSuccessRate:0.0000}," +
                $"{stats.AverageDuration:0.000},{stats.VarianceDuration:0.000},{stats.Count}," +
                $"{ratio:0.000000},{stats.AverageRecoveryFraction:0.0000},{stats.VarianceRecoveryFraction:0.0000}");
        }

        string output = string.IsNullOrWhiteSpace(invocation.SecondaryArgument)
            ? Path.Combine(directory, "analysis.csv")
            : invocation.SecondaryArgument;

        string? outputDirectory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.WriteAllLines(output, lines);
        Console.WriteLine($"Saved analysis to {output}");

        stopwatch.Stop();
        var details = $"Output={output}";
        return new ExperimentRunSummary(Name, 1, 1, 0, stopwatch.Elapsed.TotalMilliseconds, new[]
        {
            new ExperimentCaseSummary("analysis", true, experiments.Count, experiments.Count, stopwatch.Elapsed.TotalMilliseconds, details)
        });
    }
}

internal static class ExperimentModeSupport
{
    private static readonly ConcurrentDictionary<string, DirectoryCache> DirectoryFilesCache = new(StringComparer.OrdinalIgnoreCase);

    public static T LoadConfigOrCreateDefault<T>(string? configPath, string defaultFileName, string label)
        where T : new()
    {
        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            Console.WriteLine($"Loading {label} configuration from {configPath}");
            string json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<T>(json) ?? new T();
        }

        Console.WriteLine($"Using default {label} configuration.");
        var config = new T();
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(defaultFileName, JsonSerializer.Serialize(config, options));
        Console.WriteLine($"Created '{defaultFileName}' template.");
        return config;
    }

    public static int ExecuteSpecs<TSpec>(
        IReadOnlyList<TSpec> specs,
        bool useParallel,
        int? maxConcurrency,
        string emptyMessage,
        string startMessage,
        Func<TSpec, bool> work)
    {
        if (specs.Count == 0)
        {
            Console.WriteLine(emptyMessage);
            return 0;
        }

        Console.WriteLine(startMessage);
        int successful = 0;
        int processed = 0;
        var progressLock = new object();

        Action<TSpec> action = spec =>
        {
            if (work(spec))
            {
                Interlocked.Increment(ref successful);
            }

            int processedSoFar = Interlocked.Increment(ref processed);
            DrawProgress(processedSoFar, specs.Count, progressLock);
        };

        if (useParallel)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxConcurrency ?? Environment.ProcessorCount)
            };
            Parallel.ForEach(specs, options, action);
        }
        else
        {
            foreach (var spec in specs)
            {
                action(spec);
            }
        }

        Console.WriteLine($"Completed {successful} experiments.");
        return successful;
    }

    public static void SaveJson<T>(T result, string saveDirectory, string filename)
    {
        Directory.CreateDirectory(saveDirectory);
        string fullPath = Path.Combine(saveDirectory, filename);
        Console.WriteLine($"Saving result to {fullPath}");
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fullPath, json);
        TrackFileInCache(saveDirectory, fullPath);
        Console.WriteLine($"Saved result to {fullPath}");
    }

    public static bool ResultAlreadyCached(string saveDirectory, string searchPattern)
    {
        return GetDirectoryCache(saveDirectory).ContainsPattern(searchPattern);
    }

    private static DirectoryCache GetDirectoryCache(string saveDirectory)
    {
        return DirectoryFilesCache.GetOrAdd(saveDirectory, directory =>
        {
            var cache = new DirectoryCache();
            cache.Load(directory);
            return cache;
        });
    }

    private static void TrackFileInCache(string saveDirectory, string filePath)
    {
        GetDirectoryCache(saveDirectory).Add(filePath);
    }

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
}

internal sealed class DirectoryCache
{
    private readonly object _sync = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    public void Load(string directory)
    {
        lock (_sync)
        {
            _files.Clear();
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                _files.Add(Normalize(Path.GetFileName(file)));
            }
        }
    }

    public bool ContainsPattern(string pattern)
    {
        lock (_sync)
        {
            return _files.Contains(Normalize(pattern));
        }
    }

    public void Add(string filePath)
    {
        lock (_sync)
        {
            _files.Add(Normalize(Path.GetFileName(filePath)));
        }
    }

    private static string Normalize(string filename)
    {
        return Regex.Replace(filename, @"_tbl=\d+_", "_tbl=0_");
    }
}

internal sealed class StatsAccumulator
{
    private readonly object _sync = new();
    private double _sumSuccess;
    private double _sumSuccessSq;
    private double _sumDuration;
    private double _sumDurationSq;
    private double _sumRecoveryFraction;
    private double _sumRecoveryFractionSq;
    private int _count;

    public void Add(double success, double duration)
    {
        lock (_sync)
        {
            _count++;
            _sumSuccess += success;
            _sumSuccessSq += success * success;
            _sumRecoveryFraction += success;
            _sumRecoveryFractionSq += success * success;
            _sumDuration += duration;
            _sumDurationSq += duration * duration;
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public double AverageSuccessRate
    {
        get
        {
            lock (_sync)
            {
                return _count == 0 ? 0 : _sumSuccess / _count;
            }
        }
    }

    public double VarianceSuccessRate
    {
        get
        {
            lock (_sync)
            {
                if (_count == 0)
                {
                    return 0;
                }

                double mean = _sumSuccess / _count;
                double variance = _sumSuccessSq / _count - mean * mean;
                return variance < 0 ? 0 : variance;
            }
        }
    }

    public double AverageDuration
    {
        get
        {
            lock (_sync)
            {
                return _count == 0 ? 0 : _sumDuration / _count;
            }
        }
    }

    public double VarianceDuration
    {
        get
        {
            lock (_sync)
            {
                if (_count == 0)
                {
                    return 0;
                }

                double mean = _sumDuration / _count;
                double variance = _sumDurationSq / _count - mean * mean;
                return variance < 0 ? 0 : variance;
            }
        }
    }

    public double AverageRecoveryFraction
    {
        get
        {
            lock (_sync)
            {
                return _count == 0 ? 0 : _sumRecoveryFraction / _count;
            }
        }
    }

    public double VarianceRecoveryFraction
    {
        get
        {
            lock (_sync)
            {
                if (_count == 0)
                {
                    return 0;
                }

                double mean = _sumRecoveryFraction / _count;
                double variance = _sumRecoveryFractionSq / _count - mean * mean;
                return variance < 0 ? 0 : variance;
            }
        }
    }
}

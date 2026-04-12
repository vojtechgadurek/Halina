using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Halina.Core;

namespace Halina.Experiments;

public sealed class NextInPathOnlyOnceRecoveryExperimentMode : IExperimentMode
{
    private readonly record struct PointSpec(int TableCount, double MemoryMultiplier, int MultiplierIndex);
    private readonly record struct RunSpec(int TableCount, double MemoryMultiplier, int MultiplierIndex, int ExperimentIndex, int Seed);

    public string Name => "next-in-path-only-once";
    public string Usage => "next-in-path-only-once [config.json] [--parallel] [--max-concurrency N]";
    public string Description => "Sweep memory multipliers for a tabu-decoded 2/3-table 31-mer IBLT paired with NextInPathOnlyOnce, measured over recovered ulong hashes.";

    public ExperimentRunSummary Run(ExperimentInvocation invocation)
    {
        var stopwatch = Stopwatch.StartNew();
        var config = ExperimentModeSupport.LoadConfigOrCreateDefault<NextInLineRecoveryConfig>(
            invocation.PrimaryArgument,
            "next_in_path_only_once_config_default.json",
            "Next-in-path-only-once");

        Directory.CreateDirectory(config.Path);

        var pointSpecs = BuildPointSpecs(config).ToList();
        if (pointSpecs.Count == 0)
        {
            Console.WriteLine("No next-in-path-only-once recovery specs defined.");
            return new ExperimentRunSummary(Name, 0, 0, 0, 0);
        }

        var runSpecs = BuildRunSpecs(pointSpecs, config).ToList();
        Console.WriteLine(
            $"Running {runSpecs.Count} next-in-path-only-once recovery experiments across {pointSpecs.Count} memory points " +
            $"{(invocation.UseParallel ? "(parallel)" : "(sequential)")}");

        var runs = ExecuteRuns(runSpecs, config, invocation.UseParallel, invocation.MaxConcurrency);
        var points = Aggregate(runs, pointSpecs);

        var summaryData = new NextInLineRecoverySummary(
            "v1",
            config,
            points,
            config.SaveIndividualRuns ? runs.OrderBy(run => run.TableCount).ThenBy(run => run.MemoryMultiplier).ThenBy(run => run.ExperimentIndex).ToArray() : null);

        SaveSummaryArtifacts(summaryData, config.Path);

        var cases = points
            .Select(point => new ExperimentCaseSummary(
                Name: $"tables={point.TableCount},mem={point.MemoryMultiplier:0.00}",
                Succeeded: point.FailedExperiments == 0,
                ExpectedCount: (int)Math.Round(point.AverageOriginalHashes),
                RecoveredCount: (int)Math.Round(point.AverageTrueRecoveredHashes),
                DurationMs: point.AverageDurationMs,
                Details:
                    $"ratio={point.AverageRecoveryRatio:0.0000}, var={point.VarianceRecoveryRatio:0.000000}, " +
                    $"decoded={point.AverageDecodedHashes:0.00}, fp={point.AverageFalsePositiveHashes:0.00}, " +
                    $"runs={point.SuccessfulExperiments}/{point.RequestedExperiments}"))
            .ToArray();

        int successfulPoints = points.Count(point => point.FailedExperiments == 0);
        stopwatch.Stop();

        return new ExperimentRunSummary(
            Name,
            pointSpecs.Count,
            successfulPoints,
            pointSpecs.Count - successfulPoints,
            stopwatch.Elapsed.TotalMilliseconds,
            cases);
    }

    private static IEnumerable<PointSpec> BuildPointSpecs(NextInLineRecoveryConfig config)
    {
        var multipliers = config.MemoryMultiplier.Values().ToList();
        foreach (var tableCount in config.TableCount.Values().Distinct())
        {
            if (tableCount < 2 || tableCount > 3)
            {
                continue;
            }

            for (int i = 0; i < multipliers.Count; i++)
            {
                yield return new PointSpec(tableCount, multipliers[i], i);
            }
        }
    }

    private static IEnumerable<RunSpec> BuildRunSpecs(IReadOnlyList<PointSpec> points, NextInLineRecoveryConfig config)
    {
        foreach (var point in points)
        {
            for (int experimentIndex = 0; experimentIndex < Math.Max(1, config.ExperimentsPerStep); experimentIndex++)
            {
                yield return new RunSpec(
                    point.TableCount,
                    point.MemoryMultiplier,
                    point.MultiplierIndex,
                    experimentIndex,
                    ComputeSeed(config.BaseSeed, point.TableCount, point.MultiplierIndex, experimentIndex));
            }
        }
    }

    private static int ComputeSeed(int baseSeed, int tableCount, int multiplierIndex, int experimentIndex)
    {
        return unchecked(baseSeed + tableCount * 1_000_000 + multiplierIndex * 10_000 + experimentIndex);
    }

    private static List<NextInLineRecoveryRunResult> ExecuteRuns(
        IReadOnlyList<RunSpec> runSpecs,
        NextInLineRecoveryConfig config,
        bool useParallel,
        int? maxConcurrency)
    {
        var runs = new ConcurrentBag<NextInLineRecoveryRunResult>();
        int processed = 0;
        var progressLock = new object();

        Action<RunSpec> work = spec =>
        {
            runs.Add(ExecuteRun(spec, config));
            int processedSoFar = Interlocked.Increment(ref processed);
            DrawProgress(processedSoFar, runSpecs.Count, progressLock);
        };

        if (useParallel)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxConcurrency ?? Environment.ProcessorCount)
            };
            Parallel.ForEach(runSpecs, options, work);
        }
        else
        {
            foreach (var runSpec in runSpecs)
            {
                work(runSpec);
            }
        }

        return runs.ToList();
    }

    private static NextInLineRecoveryRunResult ExecuteRun(RunSpec spec, NextInLineRecoveryConfig config)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var data = GenerateKmers(
                seed: spec.Seed,
                kmerLength: config.KmerLength,
                stringCount: config.StringCount,
                kmersPerString: config.KmersPerString);

            int tableSize = Math.Max(spec.TableCount, (int)Math.Ceiling(data.Count * spec.MemoryMultiplier));
            var pipeline = new TablesBuilder<KmerData>()
                .Add(KmerIBLTFactory.CreateKmerIBLT(spec.TableCount, config.KmerLength, tableSize))
                .Add(new NextInPathOnlyOnce(spec.Seed, config.PredictorSelectionDivisor))
                .WithDecodingControl(new TabuDecodingControl<KmerData>(Math.Max(1, config.TabuLimit), item => item.Hash))
                .Build();

            Encode(pipeline, data);
            var decoded = Decode(pipeline);

            var originalHashes = BuildOddHashSet(data.Select(item => item.Hash));
            var recoveredHashes = BuildOddHashSet(decoded.Select(item => item.Hash));
            int trueRecoveredHashes = recoveredHashes.Count(hash => originalHashes.Contains(hash));
            int falsePositiveHashes = recoveredHashes.Count - trueRecoveredHashes;

            stopwatch.Stop();
            return new NextInLineRecoveryRunResult(
                spec.TableCount,
                spec.MemoryMultiplier,
                spec.ExperimentIndex,
                spec.Seed,
                originalHashes.Count,
                trueRecoveredHashes,
                recoveredHashes.Count,
                falsePositiveHashes,
                originalHashes.Count == 0 ? 0 : (double)trueRecoveredHashes / originalHashes.Count,
                stopwatch.Elapsed.TotalMilliseconds,
                true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new NextInLineRecoveryRunResult(
                spec.TableCount,
                spec.MemoryMultiplier,
                spec.ExperimentIndex,
                spec.Seed,
                0,
                0,
                0,
                0,
                0,
                stopwatch.Elapsed.TotalMilliseconds,
                false,
                ex.Message);
        }
    }

    private static List<NextInLineRecoveryPoint> Aggregate(
        IReadOnlyList<NextInLineRecoveryRunResult> runs,
        IReadOnlyList<PointSpec> pointSpecs)
    {
        var grouped = runs
            .GroupBy(run => (run.TableCount, run.MemoryMultiplier))
            .ToDictionary(group => group.Key, group => group.ToList());

        var points = new List<NextInLineRecoveryPoint>(pointSpecs.Count);
        foreach (var point in pointSpecs.OrderBy(item => item.TableCount).ThenBy(item => item.MemoryMultiplier))
        {
            grouped.TryGetValue((point.TableCount, point.MemoryMultiplier), out var groupRuns);
            groupRuns ??= new List<NextInLineRecoveryRunResult>();
            var successful = groupRuns.Where(run => run.Succeeded).ToList();

            double avgRatio = Average(successful.Select(run => run.RecoveryRatio));
            double varRatio = Variance(successful.Select(run => run.RecoveryRatio));
            double avgTrueRecovered = Average(successful.Select(run => (double)run.TrueRecoveredHashes));
            double avgDecoded = Average(successful.Select(run => (double)run.DecodedHashes));
            double avgFalsePositives = Average(successful.Select(run => (double)run.FalsePositiveHashes));
            double avgOriginal = Average(successful.Select(run => (double)run.OriginalHashes));
            double avgDuration = Average(successful.Select(run => run.DurationMs));
            double varDuration = Variance(successful.Select(run => run.DurationMs));

            points.Add(new NextInLineRecoveryPoint(
                point.TableCount,
                point.MemoryMultiplier,
                groupRuns.Count,
                successful.Count,
                groupRuns.Count - successful.Count,
                avgRatio,
                varRatio,
                avgTrueRecovered,
                avgDecoded,
                avgFalsePositives,
                avgOriginal,
                avgDuration,
                varDuration));
        }

        return points;
    }

    private static void SaveSummaryArtifacts(NextInLineRecoverySummary summary, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string jsonPath = Path.Combine(outputDirectory, "next_in_path_only_once_recovery_summary.json");
        string csvPath = Path.Combine(outputDirectory, "next_in_path_only_once_recovery_summary.csv");

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        var csv = new StringBuilder();
        csv.AppendLine("TableCount,MemoryMultiplier,RequestedExperiments,SuccessfulExperiments,FailedExperiments,AverageRecoveryRatio,VarianceRecoveryRatio,AverageTrueRecoveredHashes,AverageDecodedHashes,AverageFalsePositiveHashes,AverageOriginalHashes,AverageDurationMs,VarianceDurationMs");
        foreach (var point in summary.Points.OrderBy(item => item.TableCount).ThenBy(item => item.MemoryMultiplier))
        {
            csv.AppendLine(
                string.Join(",",
                    point.TableCount.ToString(CultureInfo.InvariantCulture),
                    point.MemoryMultiplier.ToString("0.0000", CultureInfo.InvariantCulture),
                    point.RequestedExperiments.ToString(CultureInfo.InvariantCulture),
                    point.SuccessfulExperiments.ToString(CultureInfo.InvariantCulture),
                    point.FailedExperiments.ToString(CultureInfo.InvariantCulture),
                    point.AverageRecoveryRatio.ToString("0.000000", CultureInfo.InvariantCulture),
                    point.VarianceRecoveryRatio.ToString("0.000000", CultureInfo.InvariantCulture),
                    point.AverageTrueRecoveredHashes.ToString("0.00", CultureInfo.InvariantCulture),
                    point.AverageDecodedHashes.ToString("0.00", CultureInfo.InvariantCulture),
                    point.AverageFalsePositiveHashes.ToString("0.00", CultureInfo.InvariantCulture),
                    point.AverageOriginalHashes.ToString("0.00", CultureInfo.InvariantCulture),
                    point.AverageDurationMs.ToString("0.000", CultureInfo.InvariantCulture),
                    point.VarianceDurationMs.ToString("0.000", CultureInfo.InvariantCulture)));
        }

        File.WriteAllText(csvPath, csv.ToString());
        Console.WriteLine($"Saved next-in-path-only-once recovery summary to {jsonPath}");
        Console.WriteLine($"Saved next-in-path-only-once recovery CSV to {csvPath}");
    }

    private static List<KmerData> GenerateKmers(int seed, int kmerLength, int stringCount, int kmersPerString)
    {
        var generator = new KmerDataGenerator(seed, kmerLength);
        return generator.GenerateSequences(stringCount, kmersPerString, setId: 1).SelectMany(sequence => sequence).ToList();
    }

    private static void Encode(ITable<KmerData> table, IEnumerable<KmerData> items)
    {
        var itemList = items.ToList();
        var buffer = Buffer<KmerData>.Rent(Math.Max(1, itemList.Count));
        try
        {
            foreach (var item in itemList)
            {
                buffer.Add(item);
            }

            table.Encode(buffer);
        }
        finally
        {
            buffer.Return();
        }
    }

    private static List<KmerData> Decode(ITable<KmerData> table)
    {
        var buffer = table.Decode();
        try
        {
            return buffer.ToList();
        }
        finally
        {
            buffer.Return();
        }
    }

    private static double Average(IEnumerable<double> values)
    {
        double sum = 0;
        int count = 0;
        foreach (var value in values)
        {
            sum += value;
            count++;
        }

        return count == 0 ? 0 : sum / count;
    }

    private static double Variance(IEnumerable<double> values)
    {
        var materialized = values.ToList();
        if (materialized.Count == 0)
        {
            return 0;
        }

        double mean = materialized.Average();
        double variance = materialized.Sum(value => (value - mean) * (value - mean)) / materialized.Count;
        return variance < 0 ? 0 : variance;
    }

    private static HashSet<ulong> BuildOddHashSet(IEnumerable<ulong> hashes)
    {
        var result = new HashSet<ulong>();
        foreach (var hash in hashes)
        {
            if (!result.Add(hash))
            {
                result.Remove(hash);
            }
        }

        return result;
    }

    private static void DrawProgress(int current, int total, object sync)
    {
        const int barWidth = 40;
        double ratio = total == 0 ? 1.0 : (double)current / total;
        int filled = (int)Math.Round(ratio * barWidth);
        string bar = new string('#', filled).PadRight(barWidth);
        lock (sync)
        {
            Console.Write($"\r[{bar}] {current}/{total} ({ratio:P1})");
            if (current == total)
            {
                Console.WriteLine();
            }
        }
    }
}

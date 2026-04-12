using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Halina.Core;

namespace Halina.Experiments;

public sealed class SmokeExperimentMode : IExperimentMode
{
    private const int SmokeItemCount = 10_000;
    private const int SmokeKmerLength = 16;
    private const int UlongTableSize = 90_000;
    private const int KmerTableSize = 90_000;
    private const int SwitchBucketTableSize = 90_000;
    private const int PredictorHashTableSize = 120_000;

    private const int LargeExperimentKmerSize = 16;
    private const int LargeExperimentSequenceLength = 65;
    private const int LargeExperimentSequenceCount = 100;
    private const int LargeExperimentExpectedItems = 10_000;

    private readonly Dictionary<string, Func<ExperimentCaseSummary>> _scenarios;

    public SmokeExperimentMode()
    {
        _scenarios = new Dictionary<string, Func<ExperimentCaseSummary>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ulong-iblt"] = RunUlongIbltScenario,
            ["kmer-iblt"] = RunKmerIbltScenario,
            ["filtered-kmer"] = RunFilteredKmerScenario,
            ["switch-kmer"] = RunSwitchKmerScenario,
            ["hashset-predictor"] = RunHashSetPredictorScenario,
            ["sampled-switch-pipeline"] = RunSampledSwitchPipelineScenario,
            ["next-in-path-predictor"] = RunNextInPathPredictorScenario,
            ["next-in-path-only-once"] = RunNextInPathOnlyOnceScenario,
            ["mutation-experiment"] = RunMutationExperimentScenario
        };
    }

    public string Name => "smoke";
    public string Usage => "smoke [scenario-name|all]";
    public string Description => "Run deterministic smoke scenarios for the base IBLTs and experiment pipelines.";

    public IReadOnlyList<string> GetScenarioNames()
    {
        return _scenarios.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    public ExperimentCaseSummary RunScenario(string scenarioName)
    {
        if (!_scenarios.TryGetValue(scenarioName, out var scenario))
        {
            throw new ArgumentException(
                $"Unknown smoke scenario '{scenarioName}'. Available: {string.Join(", ", GetScenarioNames())}",
                nameof(scenarioName));
        }

        try
        {
            return scenario();
        }
        catch (Exception ex)
        {
            return new ExperimentCaseSummary(scenarioName, false, Details: ex.Message);
        }
    }

    public ExperimentRunSummary Run(ExperimentInvocation invocation)
    {
        var stopwatch = Stopwatch.StartNew();
        var selected = ResolveScenarioNames(invocation.PrimaryArgument).ToList();
        var results = new List<ExperimentCaseSummary>(selected.Count);
        foreach (var name in selected)
        {
            results.Add(RunScenario(name));
        }

        stopwatch.Stop();
        PrintResults(results);
        int successful = results.Count(item => item.Succeeded);
        return new ExperimentRunSummary(Name, results.Count, successful, results.Count - successful, stopwatch.Elapsed.TotalMilliseconds, results);
    }

    private IEnumerable<string> ResolveScenarioNames(string? scenarioArgument)
    {
        if (string.IsNullOrWhiteSpace(scenarioArgument) ||
            scenarioArgument.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return GetScenarioNames();
        }

        return new[] { scenarioArgument };
    }

    private static ExperimentCaseSummary RunUlongIbltScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        ulong[] values = Enumerable.Range(1, SmokeItemCount).Select(value => (ulong)value).ToArray();
        var table = IBLTFactory.GetStandardIBLT(3, UlongTableSize);

        Encode(table, values.Select(value => new UlongData(value)));
        var decoded = Decode(table).Select(item => item.Value).OrderBy(value => value).ToArray();
        bool succeeded = values.SequenceEqual(decoded);

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "ulong-iblt",
            succeeded,
            values.Length,
            decoded.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            succeeded ? null : $"Decoded={string.Join(",", decoded)}");
    }

    private static ExperimentCaseSummary RunKmerIbltScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var kmers = CreateKmerPath(seed: 23);
        var table = KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, KmerTableSize);

        Encode(table, kmers);
        var decoded = ToSortedHashes(Decode(table));
        var expected = ToSortedHashes(kmers);
        bool succeeded = expected.SequenceEqual(decoded);

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "kmer-iblt",
            succeeded,
            expected.Length,
            decoded.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            succeeded ? null : $"Decoded={string.Join(",", decoded)}");
    }

    private static ExperimentCaseSummary RunFilteredKmerScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var kmers = CreateKmerPath(seed: 23);
        var table = new FilteredTable<KmerData>(
            KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, KmerTableSize),
            new KmerDataHashFunction(),
            filteringRatio: 3,
            remainder: 0);

        Encode(table, kmers);
        var decoded = ToSortedHashes(Decode(table));
        var expected = ToSortedHashes(kmers.Where(item => item.Hash % 3UL == 0));
        bool succeeded = expected.SequenceEqual(decoded);

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "filtered-kmer",
            succeeded,
            expected.Length,
            decoded.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            succeeded ? null : $"Decoded={string.Join(",", decoded)}");
    }

    private static ExperimentCaseSummary RunSwitchKmerScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var kmers = CreateKmerPath(seed: 23);
        ITable<KmerData> table = new SwitchTable<KmerData>(
            new[]
            {
                KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, SwitchBucketTableSize),
                KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, SwitchBucketTableSize),
                KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, SwitchBucketTableSize)
            },
            item => (int)(item.Hash % 3UL));

        Encode(table, kmers);
        var decoded = ToSortedHashes(Decode(table));
        var expected = ToSortedHashes(kmers);
        bool succeeded = expected.SequenceEqual(decoded);

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "switch-kmer",
            succeeded,
            expected.Length,
            decoded.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            succeeded ? null : $"Decoded={string.Join(",", decoded)}");
    }

    private static ExperimentCaseSummary RunHashSetPredictorScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var kmers = CreateKmerPath(seed: 17);
        var predictor = new HashSetPredictor(kmerLength: SmokeKmerLength, tableSize: PredictorHashTableSize, seed: 17);

        Encode(predictor, kmers);
        predictor.ToDecode();
        var seedKmer = kmers[kmers.Count / 2];
        Encode(predictor, new[] { seedKmer });

        var decoded = ToSortedHashes(Decode(predictor));
        var expected = ToSortedHashes(kmers.Where(item => item.Hash != seedKmer.Hash));
        bool succeeded = expected.SequenceEqual(decoded);

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "hashset-predictor",
            succeeded,
            expected.Length,
            decoded.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            succeeded ? null : $"Decoded={string.Join(",", decoded)}");
    }

    private static ExperimentCaseSummary RunSampledSwitchPipelineScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var kmers = CreateKmerPath(seed: 41);
        var pipeline = new TablesBuilder<KmerData>()
            .AddSwitch(
                item => (int)(item.Hash % 3UL),
                KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, SwitchBucketTableSize),
                KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, SwitchBucketTableSize),
                KmerIBLTFactory.CreateKmerIBLT(3, SmokeKmerLength, SwitchBucketTableSize))
            .Add(new HashSetPredictor(SmokeKmerLength, PredictorHashTableSize, 41))
            .WithDecodingControl(new TabuDecodingControl<KmerData>(3, data => data.Hash))
            .Build();

        Encode(pipeline, kmers);
        var decoded = ToSortedHashes(Decode(pipeline));
        var expected = ToSortedHashes(kmers);
        bool succeeded = expected.SequenceEqual(decoded);

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "sampled-switch-pipeline",
            succeeded,
            expected.Length,
            decoded.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            succeeded ? null : $"Decoded={string.Join(",", decoded)}");
    }

    private static ExperimentCaseSummary RunNextInPathPredictorScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var predictor = new NextInPathPredictor(seed: 17, selectionDivisor: 3);
        var kmers = CreateKmerPath(seed: 17);

        predictor.ToDecode();
        Encode(predictor, kmers);

        var first = ToSortedHashes(Decode(predictor));
        var second = ToSortedHashes(Decode(predictor));
        var third = ToSortedHashes(Decode(predictor));

        bool succeeded = first.Length > 0 && first.SequenceEqual(second) && third.Length == 0;
        string details = $"Replay={first.Length}, Third={third.Length}";

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "next-in-path-predictor",
            succeeded,
            first.Length,
            second.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            details);
    }

    private static ExperimentCaseSummary RunNextInPathOnlyOnceScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var predictor = new NextInPathOnlyOnce(seed: 17, selectionDivisor: 1);
        var kmers = CreateKmerPath(seed: 17);

        predictor.ToDecode();
        Encode(predictor, kmers);

        var first = ToSortedHashes(Decode(predictor));
        var second = ToSortedHashes(Decode(predictor));

        bool succeeded = first.Length > 0 && second.Length == 0;
        string details = $"First={first.Length}, Second={second.Length}";

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "next-in-path-only-once",
            succeeded,
            first.Length,
            first.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            details);
    }

    private static ExperimentCaseSummary RunKmerExperimentScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var parameters = GetLargeExperimentParameters();
        var result = KmerExperiments.RunExperiment(
            kmerSize: parameters.KmerSize,
            nSequences: parameters.NSequences,
            sequenceLength: parameters.SequenceLength,
            k: 3,
            l: 2,
            seed: 11,
            maxDistance: 4);

        bool succeeded =
            result.Result.TotalItems == parameters.ExpectedItems &&
            result.Result.TotalItems == result.Result.CorrectlyRecovered + result.Result.NotRecovered &&
            result.Result.FalsePositives >= 0;

        string details =
            $"Correct={result.Result.CorrectlyRecovered}, Missing={result.Result.NotRecovered}, " +
            $"FalsePositives={result.Result.FalsePositives}";

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "kmer-experiment",
            succeeded,
            result.Result.TotalItems,
            result.Result.CorrectlyRecovered,
            stopwatch.Elapsed.TotalMilliseconds,
            details);
    }

    private static ExperimentCaseSummary RunMutationExperimentScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = MutationExperiments.Run(
            kmerLength: 19,
            m: 3,
            l: 2,
            nSequences: 250,
            seed: 11);

        bool succeeded =
            result.TotalRecoveredKmers > 0 &&
            result.CorrectlyIdentifiedMutations >= 0 &&
            result.IncorrectlyIdentifiedMutations >= 0 &&
            result.MissedMutations >= 0;

        string details =
            $"Correct={result.CorrectlyIdentifiedMutations}, Incorrect={result.IncorrectlyIdentifiedMutations}, " +
            $"Missed={result.MissedMutations}";

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "mutation-experiment",
            succeeded,
            result.TotalRecoveredKmers,
            result.TotalRecoveredKmers,
            stopwatch.Elapsed.TotalMilliseconds,
            details);
    }

    private static ExperimentCaseSummary RunHashSetExtendedScenario()
    {
        var stopwatch = Stopwatch.StartNew();
        var parameters = GetLargeExperimentParameters();
        var result = HashSetPredictorExtended.Run(
            kmerSize: parameters.KmerSize,
            nSequences: parameters.NSequences,
            sequenceLength: parameters.SequenceLength,
            k: 3,
            l: 2,
            samplingStages: 3,
            shrinkFactor: 1.5,
            seed: 11,
            maxDistance: 0);

        bool succeeded =
            result.Result.TotalItems == parameters.ExpectedItems &&
            result.Result.TotalItems == result.Result.CorrectlyRecovered + result.Result.NotRecovered &&
            result.Result.FalsePositives >= 0;

        string details =
            $"Correct={result.Result.CorrectlyRecovered}, Missing={result.Result.NotRecovered}, " +
            $"FalsePositives={result.Result.FalsePositives}";

        stopwatch.Stop();
        return new ExperimentCaseSummary(
            "hashset-extended-experiment",
            succeeded,
            result.Result.TotalItems,
            result.Result.CorrectlyRecovered,
            stopwatch.Elapsed.TotalMilliseconds,
            details);
    }

    private static void PrintResults(IReadOnlyList<ExperimentCaseSummary> results)
    {
        Console.WriteLine("Smoke scenario results:");
        foreach (var result in results.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            string expected = result.ExpectedCount?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string recovered = result.RecoveredCount?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string duration = result.DurationMs?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";
            string status = result.Succeeded ? "PASS" : "FAIL";
            Console.WriteLine(
                $"  {result.Name,-28} {status,-4} expected={expected,-4} recovered={recovered,-4} ms={duration,-8} {result.Details}");
        }
    }

    private static List<KmerData> CreateKmerPath(int seed)
    {
        return new KmerDataGenerator(seed, SmokeKmerLength)
            .GenerateSequences(mSequences: 1, nKmersPerSequence: SmokeItemCount, setId: 1)
            .Single();
    }

    private static void Encode<TData>(ITable<TData> table, IEnumerable<TData> items)
    {
        var itemList = items.ToList();
        var buffer = Buffer<TData>.Rent(Math.Max(1, itemList.Count));
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

    private static List<TData> Decode<TData>(ITable<TData> table)
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

    private static ulong[] ToSortedHashes(IEnumerable<KmerData> kmers)
    {
        return kmers
            .Select(item => item.Hash)
            .OrderBy(hash => hash)
            .ToArray();
    }

    private static (int KmerSize, int NSequences, int SequenceLength, int ExpectedItems) GetLargeExperimentParameters()
    {
        return (LargeExperimentKmerSize, LargeExperimentSequenceCount, LargeExperimentSequenceLength, LargeExperimentExpectedItems);
    }
}

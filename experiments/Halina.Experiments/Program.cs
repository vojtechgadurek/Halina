using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Halina.Experiments;
using System.Linq;
using System.Runtime.Serialization;
using System.Diagnostics;

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
            Console.WriteLine("  dotnet run -- kmer [config.json]");
            Console.WriteLine("  dotnet run -- mutation [config.json]");
            Console.WriteLine("  dotnet run -- hashset-extended [config.json]");
            return;
        }

        string mode = args[0].ToLower();
        string? configPath = args.Length > 1 ? args[1] : null;

        if (mode == "kmer")
        {
            RunKmerExperiments(configPath);
        }
        else if (mode == "mutation")
        {
            RunMutationExperiments(configPath);
        }
        else if (mode == "hashset-extended")
        {
            RunHashSetPredictorExtended(configPath);
        }
        else
        {
            Console.WriteLine($"Unknown mode: {mode}");
        }
    }

    private static void RunKmerExperiments(string? configPath)
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

        int count = 0;
        foreach (var seed in config.Seed.Values())
        foreach (var l in config.L.Values())
        foreach (var k in config.K.Values())
        foreach (var kmerLen in config.KmerLength.Values())
        foreach (var nSeq in config.NSequences.Values())
        foreach (var seqLen in config.SequenceLength.Values())
        foreach (var distance in config.MaxDistance.Values())
        {
            Console.WriteLine($"Running Kmer Experiment: Seed={seed}, L={l}, K={k}, Kmer={kmerLen}, NSeq={nSeq}, SeqLen={seqLen}");
            try 
            {
                var result = KmerExperiments.RunExperiment(kmerLen, nSeq, seqLen, k, l, seed);
                Console.WriteLine($"Experiment finished in {result.Result.DurationMs:F2} ms (Gen: {result.Result.DataGenerationDurationMs:F2} ms)");
                SaveResult(result, config.Path);
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        Console.WriteLine($"Completed {count} experiments.");
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

    private static void RunHashSetPredictorExtended(string? configPath)
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

        int count = 0;
        foreach (var seed in config.Seed.Values())
        foreach (var l in config.L.Values())
        foreach (var k in config.K.Values())
        foreach (var kmerLen in config.KmerLength.Values())
        foreach (var nSeq in config.NSequences.Values())
        foreach (var seqLen in config.SequenceLength.Values())
        foreach (var stages in config.SamplingStages.Values())
        foreach (var maxDistance in config.MaxDistance.Values())
        {
            Console.WriteLine($"Running extended HashSet predictor: Seed={seed}, L={l}, K={k}, Kmer={kmerLen}, NSeq={nSeq}, SeqLen={seqLen}, Stages={stages}, MaxDistance={maxDistance}");
            try
            {
                var result = HashSetPredictorExtended.Run(kmerLen, nSeq, seqLen, k, l, stages, config.ShrinkFactor, seed);
                Console.WriteLine($"Experiment finished in {result.Result.DurationMs:F2} ms (Gen: {result.Result.DataGenerationDurationMs:F2} ms)");
                SaveExtendedResult(result, config.Path);
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        Console.WriteLine($"Completed {count} experiments.");
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
        Console.WriteLine($"Saved result to {filename}");
    }

    private static void SaveExtendedResult(ExtendedExperimentResult result, string saveDirectory)
    {
        Directory.CreateDirectory(saveDirectory);
        var shrinkLabel = result.Arguments.ShrinkFactor.ToString("0.##", CultureInfo.InvariantCulture);
        string filename = $"v={result.Version}_k={result.Arguments.K}_l={result.Arguments.L}_kmer={result.Arguments.KmerSize}_nseq={result.Arguments.NSequences}_len={result.Arguments.SequenceLength}_stages={result.Arguments.SamplingStages}_shrink={shrinkLabel}_seed={result.Arguments.Seed}.json";
        filename = Path.Combine(saveDirectory, filename);
        Console.WriteLine($"Saving result to {filename}");

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(result, options);
        File.WriteAllText(filename, json);
        Console.WriteLine($"Saved result to {filename}");
    }
}

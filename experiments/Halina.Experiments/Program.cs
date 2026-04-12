using System;
using System.Linq;

namespace Halina.Experiments;

public static class Program
{
    public static int Main(string[] args)
    {
        var registry = ExperimentRegistry.CreateDefault();
        if (args.Length == 0)
        {
            PrintUsage(registry);
            return 1;
        }

        var invocation = ParseInvocation(args);
        if (!registry.TryGetMode(invocation.Mode, out var mode))
        {
            Console.WriteLine($"Unknown mode: {invocation.Mode}");
            PrintUsage(registry);
            return 1;
        }

        try
        {
            var summary = mode.Run(invocation);
            PrintSummary(summary);
            return summary.FailedRuns == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    internal static ExperimentInvocation ParseInvocation(string[] args)
    {
        string mode = args[0].ToLowerInvariant();
        string? primaryArgument = null;
        string? secondaryArgument = null;
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
                if (int.TryParse(args[++i], out var parsed) && parsed > 0)
                {
                    maxConcurrency = parsed;
                }

                continue;
            }

            if (primaryArgument == null)
            {
                primaryArgument = current;
            }
            else if (secondaryArgument == null)
            {
                secondaryArgument = current;
            }
        }

        return new ExperimentInvocation(mode, primaryArgument, secondaryArgument, useParallel, maxConcurrency);
    }

    private static void PrintUsage(ExperimentRegistry registry)
    {
        Console.WriteLine("Usage:");
        foreach (var mode in registry.GetModes().OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"  dotnet run -- {mode.Usage}");
            Console.WriteLine($"    {mode.Description}");
        }
    }

    private static void PrintSummary(ExperimentRunSummary summary)
    {
        Console.WriteLine(
            $"Summary for '{summary.Mode}': {summary.SuccessfulRuns}/{summary.TotalRuns} successful, " +
            $"{summary.FailedRuns} failed, {summary.DurationMs:F2} ms.");
    }
}

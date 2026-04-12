using System;
using System.Collections.Generic;
using System.Linq;

namespace Halina.Experiments;

public sealed record ExperimentInvocation(
    string Mode,
    string? PrimaryArgument = null,
    string? SecondaryArgument = null,
    bool UseParallel = false,
    int? MaxConcurrency = null);

public sealed record ExperimentCaseSummary(
    string Name,
    bool Succeeded,
    int? ExpectedCount = null,
    int? RecoveredCount = null,
    double? DurationMs = null,
    string? Details = null);

public sealed record ExperimentRunSummary(
    string Mode,
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    double DurationMs,
    IReadOnlyList<ExperimentCaseSummary>? Cases = null);

public interface IExperimentMode
{
    string Name { get; }
    string Usage { get; }
    string Description { get; }
    ExperimentRunSummary Run(ExperimentInvocation invocation);
}

public sealed class ExperimentRegistry
{
    private readonly Dictionary<string, IExperimentMode> _modes;

    public ExperimentRegistry(IEnumerable<IExperimentMode> modes)
    {
        _modes = new Dictionary<string, IExperimentMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in modes ?? throw new ArgumentNullException(nameof(modes)))
        {
            _modes[mode.Name] = mode;
        }
    }

    public IEnumerable<IExperimentMode> GetModes() => _modes.Values;

    public bool TryGetMode(string name, out IExperimentMode mode)
    {
        return _modes.TryGetValue(name, out mode!);
    }

    public static ExperimentRegistry CreateDefault()
    {
        return new ExperimentRegistry(new IExperimentMode[]
        {
            new KmerExperimentMode(),
            new MutationExperimentMode(),
            new HashSetExtendedExperimentMode(),
            new NextInLineRecoveryExperimentMode(),
            new NextInPathOnlyOnceRecoveryExperimentMode(),
            new ResultsAnalysisExperimentMode(),
            new SmokeExperimentMode()
        });
    }
}

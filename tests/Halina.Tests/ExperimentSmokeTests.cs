using System.Collections.Generic;
using System.Linq;
using Halina.Experiments;
using Xunit;

namespace Halina.Tests;

public class ExperimentSmokeTests
{
    [Fact]
    public void SmokeMode_RunAllScenarios_Succeeds()
    {
        var mode = new SmokeExperimentMode();

        var summary = mode.Run(new ExperimentInvocation("smoke", "all"));

        Assert.NotNull(summary.Cases);
        Assert.Equal(summary.TotalRuns, summary.SuccessfulRuns);
        Assert.Equal(0, summary.FailedRuns);
        Assert.All(summary.Cases!, entry => Assert.True(entry.Succeeded, entry.Name));
    }

    [Theory]
    [MemberData(nameof(GetScenarioNames))]
    public void SmokeScenario_IsDeterministic(string scenarioName)
    {
        var mode = new SmokeExperimentMode();

        var first = mode.RunScenario(scenarioName);
        var second = mode.RunScenario(scenarioName);

        Assert.Equal(first.Succeeded, second.Succeeded);
        Assert.Equal(first.ExpectedCount, second.ExpectedCount);
        Assert.Equal(first.RecoveredCount, second.RecoveredCount);
        Assert.Equal(first.Details, second.Details);
    }

    [Fact]
    public void ExperimentRegistry_ExposesSmokeMode()
    {
        var registry = ExperimentRegistry.CreateDefault();

        Assert.True(registry.TryGetMode("smoke", out var mode));
        Assert.Equal("smoke", mode.Name);
    }

    public static IEnumerable<object[]> GetScenarioNames()
    {
        return new SmokeExperimentMode()
            .GetScenarioNames()
            .Select(name => new object[] { name });
    }
}

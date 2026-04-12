using System;
using System.IO;
using System.Text.Json;
using Halina.Experiments;
using Xunit;

namespace Halina.Tests;

public class NextInLineRecoveryExperimentTests
{
    [Fact]
    public void NextInLineMode_WritesSummaryArtifactsFromConfig()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "halina-next-in-line-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string resultsPath = Path.Combine(tempRoot, "results");
            string configPath = Path.Combine(tempRoot, "next_in_line_test_config.json");

            var config = new NextInLineRecoveryConfig
            {
                Path = resultsPath,
                KmerLength = 7,
                StringCount = 4,
                KmersPerString = 6,
                BaseSeed = 10,
                ExperimentsPerStep = 2,
                PredictorSelectionDivisor = 3,
                TabuLimit = 3,
                TableCount = new RangeConfig { Start = 2, End = 3, Step = 1 },
                MemoryMultiplier = new DoubleRangeConfig { Start = 0.4, End = 0.5, Step = 0.1 },
                SaveIndividualRuns = true
            };

            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            var mode = new NextInLineRecoveryExperimentMode();
            var summary = mode.Run(new ExperimentInvocation("next-in-line", configPath));

            Assert.Equal(4, summary.TotalRuns);
            Assert.NotNull(summary.Cases);
            Assert.Equal(4, summary.Cases!.Count);
            Assert.True(File.Exists(Path.Combine(resultsPath, "next_in_line_recovery_summary.json")));
            Assert.True(File.Exists(Path.Combine(resultsPath, "next_in_line_recovery_summary.csv")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

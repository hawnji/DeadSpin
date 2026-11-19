using DeadSpin.Core.Simulation;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class CampaignSimulationTests
    {
        [TestCase(BotStyle.Cautious)]
        [TestCase(BotStyle.Balanced)]
        [TestCase(BotStyle.Aggressive)]
        [TestCase(BotStyle.Bold)]
        public void FullRuns_EndCleanly_WithNoInvariantProblems(BotStyle style)
        {
            SimulationReport report = RunSimulator.SimulateMany(40, 1000, style);
            TestContext.WriteLine(report.Describe());
            foreach (SimulatedRun run in report.Runs)
            {
                Assert.IsNotNull(run.EndingId, "Run " + run.Seed + " ended without an ending.");
                Assert.IsEmpty(run.Problems, "Run " + run.Seed + ": " + string.Join(" | ", run.Problems));
            }
        }
    }
}

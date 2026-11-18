using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class RunTests
    {
        private static RunConfig QuickConfig()
        {
            var config = RunConfig.CreateDefault();
            config.AnnouncementSeconds = 0f;
            config.SummarySeconds = 0f;
            config.Seed = 42;
            return config;
        }

        private static void PlayEmptyRound(Run run)
        {
            Assert.IsTrue(run.TryBeginRound());
            run.Round.TryCloseBettingEarly();
            run.Round.NotifySpinStarted();
            run.Round.NotifySpinFinished();
            run.Round.NotifyResolutionFinished();
        }

        [Test]
        public void DefaultConfig_MatchesDesignBrief()
        {
            var config = RunConfig.CreateDefault();
            Assert.AreEqual(2500, config.StartingBalance);
            Assert.AreEqual(50000, config.StartingDebt);
            Assert.AreEqual(18, config.TotalRounds);
            Assert.IsEmpty(config.Validate());
        }

        [Test]
        public void InvalidConfig_IsRejected()
        {
            var config = RunConfig.CreateDefault();
            config.MinimumBet = 5000;
            Assert.IsNotEmpty(config.Validate());
            Assert.Throws<System.InvalidOperationException>(() => new Run(config));
        }

        [Test]
        public void Run_CopiesConfig_SoLaterEditsDoNotLeakIn()
        {
            var config = QuickConfig();
            var run = new Run(config);
            config.TotalRounds = 1;
            Assert.AreEqual(18, run.Config.TotalRounds);
        }

        [Test]
        public void SeededRun_IsReproducible()
        {
            var a = new Run(QuickConfig());
            var b = new Run(QuickConfig());
            Assert.AreEqual(a.Random.NextInt(100000), b.Random.NextInt(100000));
        }

        [Test]
        public void CompletingRounds_AdvancesCounter_AndStampsLedger()
        {
            var run = new Run(QuickConfig());
            Assert.AreEqual(1, run.CurrentRoundNumber);
            PlayEmptyRound(run);
            Assert.AreEqual(1, run.RoundsPlayed);
            Assert.AreEqual(2, run.CurrentRoundNumber);
            Assert.AreEqual(2, run.Ledger.CurrentRound);
        }

        [Test]
        public void PayingDebt_WinsTheRun()
        {
            var config = QuickConfig();
            config.StartingDebt = 1000;
            var run = new Run(config);
            RunOutcome? ended = null;
            run.Ended += o => ended = o;
            run.Ledger.PayDebt(1000, "freedom");
            Assert.AreEqual(RunOutcome.Freedom, run.Outcome);
            Assert.AreEqual(RunOutcome.Freedom, ended);
            Assert.IsFalse(run.TryBeginRound());
        }

        [Test]
        public void LosingAllMoney_IsBankruptcy()
        {
            var run = new Run(QuickConfig());
            run.Ledger.ForceDebit(2495, TransactionKind.Penalty, "house collects");
            Assert.AreEqual(RunOutcome.Bankrupt, run.Outcome, "5 left is below the 10 minimum bet.");
        }

        [Test]
        public void MoneyOnTable_PreventsBankruptcy()
        {
            var run = new Run(QuickConfig());
            run.TryBeginRound();
            run.SetMoneyAtRisk(2500);
            run.Ledger.TryDebit(2500, TransactionKind.WagerPlaced, "all in");
            Assert.AreEqual(RunOutcome.InProgress, run.Outcome);
        }

        [Test]
        public void Bankruptcy_IsDeferredUntilSpinResolves()
        {
            var run = new Run(QuickConfig());
            run.TryBeginRound();
            run.Round.TryCloseBettingEarly();
            run.Round.NotifySpinStarted();
            // Payout system clears the table, then the balance is checked before winnings arrive.
            run.SetMoneyAtRisk(0);
            run.Ledger.ForceDebit(2500, TransactionKind.Penalty, "skull pocket");
            Assert.AreEqual(RunOutcome.InProgress, run.Outcome);

            run.Round.NotifySpinFinished();
            run.Ledger.Credit(5, TransactionKind.Winnings, "tiny win");
            Assert.AreEqual(RunOutcome.InProgress, run.Outcome);

            run.Round.NotifyResolutionFinished();
            Assert.AreEqual(RunOutcome.Bankrupt, run.Outcome);
        }

        [Test]
        public void PlayingAllRoundsWithDebt_MissesDeadline()
        {
            var config = QuickConfig();
            config.TotalRounds = 3;
            var run = new Run(config);
            PlayEmptyRound(run);
            PlayEmptyRound(run);
            Assert.AreEqual(RunOutcome.InProgress, run.Outcome);
            PlayEmptyRound(run);
            Assert.AreEqual(RunOutcome.DeadlineMissed, run.Outcome);
            Assert.AreEqual(3, run.CurrentRoundNumber);
        }

        [Test]
        public void Ended_FiresOnlyOnce()
        {
            var config = QuickConfig();
            config.StartingDebt = 100;
            var run = new Run(config);
            int count = 0;
            run.Ended += o => count++;
            run.Ledger.PayDebt(100, "pay");
            run.Ledger.Credit(10, TransactionKind.Reward, "after");
            run.EvaluateOutcome();
            Assert.AreEqual(1, count);
        }

        [Test]
        public void ExplicitRandomSource_IsUsed()
        {
            var rng = new SeededRandom(5);
            var run = new Run(QuickConfig(), rng);
            Assert.AreSame(rng, run.Random);
        }
    }
}

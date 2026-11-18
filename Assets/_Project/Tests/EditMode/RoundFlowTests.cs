using System.Collections.Generic;
using DeadSpin.Core.GameState;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class RoundFlowTests
    {
        private static RunConfig Config(float announce = 2f, float betting = 10f, float summary = 3f)
        {
            var config = RunConfig.CreateDefault();
            config.AnnouncementSeconds = announce;
            config.BettingSeconds = betting;
            config.SummarySeconds = summary;
            return config;
        }

        [Test]
        public void FullRound_FollowsPhaseOrder()
        {
            var flow = new RoundFlow(Config());
            var phases = new List<RoundPhase>();
            bool completed = false;
            flow.PhaseChanged += (from, to) => phases.Add(to);
            flow.RoundCompleted += () => completed = true;

            Assert.IsTrue(flow.TryBeginRound());
            flow.Tick(2f);
            Assert.IsTrue(flow.IsBettingOpen);
            flow.Tick(10f);
            Assert.AreEqual(RoundPhase.BettingClosed, flow.Phase);
            Assert.IsTrue(flow.NotifySpinStarted());
            Assert.IsTrue(flow.NotifySpinFinished());
            Assert.IsTrue(flow.NotifyResolutionFinished());
            flow.Tick(3f);

            Assert.IsTrue(completed);
            CollectionAssert.AreEqual(new[]
            {
                RoundPhase.Announcing, RoundPhase.Betting, RoundPhase.BettingClosed,
                RoundPhase.Spinning, RoundPhase.Resolving, RoundPhase.Summary, RoundPhase.Idle
            }, phases);
        }

        [Test]
        public void Tick_CountsDownWithinPhase()
        {
            var flow = new RoundFlow(Config(announce: 0f, betting: 10f));
            flow.TryBeginRound();
            flow.Tick(4f);
            Assert.AreEqual(RoundPhase.Betting, flow.Phase);
            Assert.AreEqual(6f, flow.TimeRemaining, 1e-4);
        }

        [Test]
        public void LargeTick_CarriesAcrossTimedPhases_ButStopsAtUntimedPhase()
        {
            var flow = new RoundFlow(Config(announce: 2f, betting: 5f));
            flow.TryBeginRound();
            flow.Tick(100f);
            Assert.AreEqual(RoundPhase.BettingClosed, flow.Phase, "The spin must wait for the spin system, regardless of elapsed time.");
        }

        [Test]
        public void ExternalPhases_RejectOutOfOrderNotifications()
        {
            var flow = new RoundFlow(Config());
            Assert.IsFalse(flow.NotifySpinStarted());
            flow.TryBeginRound();
            Assert.IsFalse(flow.NotifySpinFinished());
            Assert.IsFalse(flow.NotifyResolutionFinished());
            Assert.IsFalse(flow.TryBeginRound(), "Cannot start a round while one is in progress.");
        }

        [Test]
        public void ExtendBetting_OnlyWhileBetting()
        {
            var flow = new RoundFlow(Config(announce: 1f, betting: 10f));
            flow.TryBeginRound();
            Assert.IsFalse(flow.TryExtendBetting(5f));
            flow.Tick(1f);
            Assert.IsTrue(flow.TryExtendBetting(5f));
            Assert.AreEqual(15f, flow.TimeRemaining, 1e-4);
            Assert.AreEqual(5f, flow.BonusBettingSeconds, 1e-4);
        }

        [Test]
        public void CloseBettingEarly()
        {
            var flow = new RoundFlow(Config(announce: 0f));
            flow.TryBeginRound();
            Assert.IsTrue(flow.TryCloseBettingEarly());
            Assert.AreEqual(RoundPhase.BettingClosed, flow.Phase);
        }

        [Test]
        public void ZeroSummary_CompletesImmediately()
        {
            var flow = new RoundFlow(Config(announce: 0f, summary: 0f));
            bool completed = false;
            flow.RoundCompleted += () => completed = true;
            flow.TryBeginRound();
            flow.TryCloseBettingEarly();
            flow.NotifySpinStarted();
            flow.NotifySpinFinished();
            flow.NotifyResolutionFinished();
            Assert.IsTrue(completed);
            Assert.AreEqual(RoundPhase.Idle, flow.Phase);
        }

        [Test]
        public void Abort_ReturnsToIdleWithoutCompleting()
        {
            var flow = new RoundFlow(Config());
            bool completed = false;
            flow.RoundCompleted += () => completed = true;
            flow.TryBeginRound();
            flow.Abort();
            Assert.AreEqual(RoundPhase.Idle, flow.Phase);
            Assert.IsFalse(completed);
        }
    }
}

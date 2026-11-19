using System.Collections.Generic;
using System.Linq;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    /// <summary>Phase 5: the data-driven rule engine, fairness and the Game Master's decisions.</summary>
    public class RuleEngineTests
    {
        private sealed class Env : IRuleEnvironment
        {
            public int Stage { get; set; } = 5;
            public int Round { get; set; } = 1;
            public long Balance { get; set; } = 2500;
            public long Debt { get; set; } = 50000;
            public int RoundsRemaining { get; set; } = 18;
            public long ConfiguredMinimumBet { get; set; } = 10;
            public long ConfiguredMaximumBet { get; set; } = 10000;
            public int FavoriteNumber { get; set; } = 17;
            public int FavoriteDozen { get; set; } = 1;
            public IReadOnlyList<int> History { get; set; } = new int[0];
        }

        private static RuleEngine NewEngine(Env env = null)
        {
            return new RuleEngine(RuleCatalog.CreateDefault(), StandardWheels.European(), env ?? new Env());
        }

        [Test]
        public void Catalog_HasEveryInterventionTypeAndUniqueIds()
        {
            RuleCatalog c = RuleCatalog.CreateDefault();
            Assert.GreaterOrEqual(c.All.Count, 38);
            Assert.AreEqual(c.All.Count, c.All.Select(r => r.Id).Distinct().Count());
            foreach (RuleCategory category in System.Enum.GetValues(typeof(RuleCategory)))
            {
                Assert.IsTrue(c.All.Any(r => r.Category == category), "No rule in " + category);
            }
            Assert.AreEqual(1, c.Get("move_numbers").AuthorityCost);
            Assert.AreEqual(2, c.Get("remove_number").AuthorityCost);
            Assert.AreEqual(3, c.Get("add_twelve").AuthorityCost);
            Assert.AreEqual(3, c.Get("special_pocket").AuthorityCost);
            Assert.AreEqual(5, c.Get("full_transformation").AuthorityCost);
        }

        [Test]
        public void EveryRule_ActivatesOnAFreshWheelOrGivesAReason()
        {
            RuleCatalog catalog = RuleCatalog.CreateDefault();
            foreach (RuleDefinition rule in catalog.All)
            {
                RuleEngine engine = new RuleEngine(catalog, StandardWheels.European(), new Env());
                RuleActivation a = engine.Activate(rule, rule.Id == "player_lucky_coin" ? 17 : rule.Id == "event_inspection" ? (int)BetType.Red : -1, 3);
                Assert.IsTrue(a.Success || !string.IsNullOrEmpty(a.Reason), rule.Id);
                Assert.IsTrue(engine.EffectiveWheel.IsValid, rule.Id);
                Assert.AreEqual(0, engine.EffectiveModifiers.Validate().Count, rule.Id);
            }
        }

        [Test]
        public void RemoveNumber_TakesTheFavouriteNumberPermanently()
        {
            RuleEngine engine = NewEngine();
            Assert.IsTrue(engine.Activate(engine.Catalog.Get("remove_number")).Success);
            Assert.IsFalse(engine.BaseWheel.Contains(17));
            engine.TickRound();
            Assert.IsFalse(engine.IsActive("remove_number"));
            engine.Recompose();
            Assert.IsFalse(engine.EffectiveWheel.Contains(17), "Permanent changes stay after the rule leaves the panel.");
        }

        [Test]
        public void TimedRules_ExpireAndStackingRefreshes()
        {
            RuleEngine engine = NewEngine();
            RuleDefinition squeeze = engine.Catalog.Get("payout_squeeze");
            Assert.IsTrue(engine.Activate(squeeze).Success);
            engine.TickRound();
            Assert.IsTrue(engine.Activate(squeeze).Success);
            Assert.AreEqual(1, engine.Active.Count(r => r.Id == "payout_squeeze"));
            Assert.AreEqual(squeeze.Duration, engine.Find("payout_squeeze").RoundsRemaining);
            for (int i = 0; i < squeeze.Duration; i++) engine.TickRound();
            Assert.IsFalse(engine.IsActive("payout_squeeze"));
        }

        [Test]
        public void Conflicts_HigherPriorityReplacesLower()
        {
            RuleEngine engine = NewEngine();
            Assert.IsTrue(engine.Activate(engine.Catalog.Get("higher_stakes")).Success);
            RuleActivation last = engine.Activate(engine.Catalog.Get("final_last_spin"));
            Assert.IsTrue(last.Success);
            Assert.AreEqual("higher_stakes", last.Replaced.Id);
            Assert.IsFalse(engine.IsActive("higher_stakes"));
            Assert.IsFalse(engine.Activate(engine.Catalog.Get("higher_stakes")).Success, "Lower priority cannot replace higher.");
        }

        [Test]
        public void Fairness_MinimumBetNeverExceedsHalfTheBalance()
        {
            var env = new Env { Balance = 100 };
            RuleEngine engine = NewEngine(env);
            engine.Activate(engine.Catalog.Get("higher_stakes"));
            engine.Recompose();
            Assert.LessOrEqual(engine.EffectiveModifiers.MinimumBet, 50);
        }

        [Test]
        public void Fairness_RefusesRulesThatMakeTheDebtUnpayable()
        {
            var env = new Env { Debt = 10_000_000, RoundsRemaining = 1 };
            RuleEngine engine = NewEngine(env);
            RuleActivation a = engine.Activate(engine.Catalog.Get("payout_squeeze"));
            Assert.IsFalse(a.Success);
            StringAssert.Contains("unfair", a.Reason);
        }

        [Test]
        public void RedTax_HasACombinationLoophole()
        {
            RuleEngine engine = NewEngine();
            engine.Activate(engine.Catalog.Get("red_tax"));
            engine.Recompose();
            PayoutModifier tax = engine.EffectiveModifiers.PayoutModifiers.Single();
            var red = new Pocket(1, 1, PocketColor.Red);
            Assert.IsTrue(tax.AppliesTo(BetSelection.Straight(1), red));
            Assert.IsTrue(tax.Loophole(BetSelection.Combination(0), red));
            Assert.IsFalse(tax.Loophole(BetSelection.Straight(1), red));
        }

        [Test]
        public void Brain_TargetsTheFavouriteNumberPlayer()
        {
            var profile = new PlayerProfile();
            var result = new RoundResult(1, 0, new Pocket(0, 0, PocketColor.Green),
                new List<BetOutcome> { new BetOutcome(BetSelection.Straight(17), 1000, false, 0, 35m) });
            for (int i = 0; i < 5; i++) profile.Observe(result, 36);
            var brain = new GameMasterBrain(new AuthorityPool(10), GameMasterPersonality.Showman);
            RuleCatalog c = RuleCatalog.CreateDefault();
            float antiFavorite = brain.Score(c.Get("remove_number"), profile, 2500, 50000, 50000);
            float neutral = brain.Score(c.Get("reverse_rotation"), profile, 2500, 50000, 50000);
            Assert.Greater(antiFavorite, neutral);
        }

        [Test]
        public void Brain_NeverPicksMoreThanItCanAfford()
        {
            var env = new Env();
            RuleEngine engine = NewEngine(env);
            var brain = new GameMasterBrain(new AuthorityPool(2), GameMasterPersonality.Brute) { Difficulty = GameMasterDifficulty.ForStage(5) };
            var random = new SeededRandom(4);
            for (int i = 0; i < 50; i++)
            {
                int total = brain.Choose(engine, new PlayerProfile(), env, 50000, random, false).Sum(c => c.Rule.AuthorityCost);
                Assert.LessOrEqual(total, 2);
            }
        }

        [Test]
        public void Dialogue_HasALineForEveryTrigger()
        {
            DialogueBank bank = DialogueBank.CreateDefault();
            var random = new SeededRandom(1);
            foreach (DialogueTrigger t in System.Enum.GetValues(typeof(DialogueTrigger)))
            {
                string key = t == DialogueTrigger.StageStart || t == DialogueTrigger.FinalPhase ? "1" : t == DialogueTrigger.Ending ? "Freedom" : t == DialogueTrigger.SpecialPocket ? "Skull" : null;
                Assert.IsNotNull(bank.Pick(t, GameMasterMood.Calm, 1, "showman", random, key), "No line for " + t);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Persistence;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    /// <summary>Phases 6 to 9: economy, specials, progression, endings and saves, driven through the round director.</summary>
    public class CampaignTests
    {
        private static CasinoRun NewRun(ulong seed = 11, long balance = 2500, long debt = 50000, int rounds = 18, MetaProgress meta = null)
        {
            var config = RunConfig.CreateDefault();
            config.StartingBalance = balance;
            config.StartingDebt = debt;
            config.TotalRounds = rounds;
            config.AnnouncementSeconds = 1f;
            config.SummarySeconds = 1f;
            RunSetup setup = RunSetup.Create(seed, meta, config, randomizeNumbers: false);
            var run = new CasinoRun(setup, meta);
            run.Start();
            return run;
        }

        /// <summary>Opens betting for the next round (through the announcement).</summary>
        private static bool OpenBetting(CasinoRun run)
        {
            if (!run.BeginRound()) return false;
            if (run.Run.Round.Phase == RoundPhase.Announcing) run.Tick(run.Run.Round.TimeRemaining + 0.01f);
            return run.Run.Round.Phase == RoundPhase.Betting;
        }

        /// <summary>Closes betting, spins (including any double spin) and finishes the round.</summary>
        private static RoundResult SpinAndFinish(CasinoRun run)
        {
            if (run.Run.Round.Phase == RoundPhase.Betting) run.CloseBetting();
            RoundResult last = null;
            while (!run.Run.IsOver && run.Run.Round.Phase == RoundPhase.BettingClosed)
            {
                run.StartSpin();
                last = run.FinishSpin();
            }
            if (!run.Run.IsOver && run.Run.Round.Phase == RoundPhase.Summary) run.Tick(run.Run.Round.TimeRemaining + 0.01f);
            return last;
        }

        private static RoundResult PlayRound(CasinoRun run, long redBet = 0)
        {
            if (!OpenBetting(run)) return null;
            if (redBet > 0) run.PlaceBet(BetSelection.Outside(BetType.Red), redBet);
            return SpinAndFinish(run);
        }

        private static RoundResult FakeResult(CasinoRun run, Pocket pocket)
        {
            return new RoundResult(run.CurrentRound, 0, pocket, new List<BetOutcome>());
        }

        // ------------------------------------------------------------------ progression

        [Test]
        public void Stages_CoverEighteenRoundsAsDesigned()
        {
            StageTable t = StageTable.CreateDefault(18);
            CollectionAssert.AreEqual(new[] { 3, 7, 11, 15, 18 }, t.StageEndRounds());
            Assert.AreEqual(1, t.StageOf(1));
            Assert.AreEqual(2, t.StageOf(4));
            Assert.AreEqual(5, t.StageOf(16));
        }

        [TestCase(5)]
        [TestCase(8)]
        [TestCase(12)]
        [TestCase(30)]
        public void Stages_AreContiguousForAnyRunLength(int rounds)
        {
            StageTable t = StageTable.CreateDefault(rounds);
            int expectedFirst = 1;
            foreach (StageDefinition s in t.Stages)
            {
                Assert.AreEqual(expectedFirst, s.FirstRound, "Stage " + s.Number);
                Assert.GreaterOrEqual(s.LastRound, s.FirstRound);
                expectedFirst = s.LastRound + 1;
            }
            Assert.AreEqual(rounds, t.Stages.Last().LastRound);
        }

        [Test]
        public void StageOne_IsPlainRoulette()
        {
            CasinoRun run = NewRun();
            Assert.IsFalse(run.Table.Modifiers.SpecialBetsAllowed, "Special bets open in stage 2.");
            Assert.AreEqual(37, run.Table.Wheel.PocketCount);
            Assert.IsFalse(run.Table.Wheel.Pockets.Any(p => p.Special != SpecialPocketType.None));
        }

        [Test]
        public void StageTwo_AddsSpecialPocketsAndSpecialBets()
        {
            CasinoRun run = NewRun();
            for (int i = 0; i < 3; i++) PlayRound(run);
            Assert.IsTrue(OpenBetting(run));
            Assert.AreEqual(2, run.Stage);
            Assert.IsTrue(run.Table.Modifiers.SpecialBetsAllowed || run.Rules.IsActive("plain_roulette"));
            Assert.GreaterOrEqual(run.Rules.BaseWheel.Pockets.Count(p => p.Special != SpecialPocketType.None), 1);
        }

        [Test]
        public void GameMaster_StageOne_UsesOnlyTheStagePool()
        {
            CasinoRun run = NewRun(seed: 5);
            StageDefinition stage = run.Stages.Get(1);
            for (int i = 0; i < 3; i++)
            {
                OpenBetting(run);
                foreach (RuleInstance r in run.AnnouncedThisRound) Assert.IsTrue(stage.RulePool.Contains(r.Id), r.Id + " is not in the stage 1 pool.");
                SpinAndFinish(run);
            }
        }

        // ------------------------------------------------------------------ economy

        [Test]
        public void PaymentSchedule_DefaultsAreTwoFiveAndTenPercent()
        {
            PaymentSchedule s = PaymentSchedule.Create(50000, StageTable.CreateDefault(18).StageEndRounds());
            CollectionAssert.AreEqual(new long[] { 1000, 2500, 5000 }, s.Installments.Select(i => i.CumulativeAmount).ToArray());
            CollectionAssert.AreEqual(new[] { 7, 11, 15 }, s.Installments.Select(i => i.DueRound).ToArray());
        }

        [Test]
        public void PaymentSchedule_CollectsThenGraceThenDefault()
        {
            var ledger = new Ledger(5000, 50000);
            PaymentSchedule s = PaymentSchedule.Create(50000, new[] { 3, 7, 11, 15, 18 });
            long amount;
            Assert.AreEqual(InstallmentResult.NotDue, s.Collect(6, ledger, (a, r) => { ledger.IncreaseDebt(a, r); return a; }, out amount));
            Assert.AreEqual(InstallmentResult.Collected, s.Collect(7, ledger, (a, r) => { ledger.IncreaseDebt(a, r); return a; }, out amount));
            Assert.AreEqual(1000, amount);
            Assert.AreEqual(4000, ledger.Balance);

            ledger.ForceDebit(3900, TransactionKind.Penalty, "test");
            long debtBefore = ledger.Debt;
            Assert.AreEqual(InstallmentResult.Grace, s.Collect(11, ledger, (a, r) => { ledger.IncreaseDebt(a, r); return a; }, out amount));
            Assert.AreEqual(100, ledger.Balance, "A missed installment takes nothing.");
            Assert.AreEqual(debtBefore + 750, ledger.Debt, "Late fee is 50% of the $1,500 shortfall.");
            Assert.IsFalse(s.Defaulted);
            Assert.AreEqual(InstallmentResult.Default, s.Collect(15, ledger, (a, r) => { ledger.IncreaseDebt(a, r); return a; }, out amount));
            Assert.IsTrue(s.Defaulted);
        }

        [Test]
        public void VoluntaryPayments_CountTowardInstallments()
        {
            CasinoRun run = NewRun(balance: 6000);
            Assert.AreEqual(1500, run.PayDebt(1500));
            Assert.AreEqual(0, run.Payments.OutstandingFor(run.Payments.Next));
        }

        [Test]
        public void Vault_StoredMoneyIsSafeFromTaxesAndCountsAgainstBankruptcy()
        {
            CasinoRun run = NewRun(balance: 3000);
            Assert.AreEqual(2000, run.Deposit(2000));
            Assert.AreEqual(1000, run.Ledger.Balance);
            Assert.AreEqual(2000, run.Ledger.Stored);
            run.Pockets.Get(SpecialPocketType.Tax).Resolve(run, FakeResult(run, new Pocket(99, 5, PocketColor.Red)), new Pocket(99, 5, PocketColor.Red));
            Assert.AreEqual(900, run.Ledger.Balance);
            Assert.AreEqual(2000, run.Ledger.Stored);
            run.Ledger.ForceDebit(900, TransactionKind.Penalty, "test");
            Assert.IsFalse(run.Run.IsOver, "Money in the Vault keeps the player in the game.");
        }

        [Test]
        public void BankruptcyForecast_WarnsBeforeTheEnd()
        {
            var schedule = PaymentSchedule.Create(50000, new[] { 3, 7, 11, 15, 18 });
            List<FinancialWarning> w = BankruptcyForecast.Evaluate(25, 50000, 10, 0, schedule, 2, 16);
            Assert.AreEqual(WarningLevel.Danger, BankruptcyForecast.Worst(w));
            w = BankruptcyForecast.Evaluate(5, 50000, 10, 0, schedule, 2, 16);
            Assert.AreEqual(WarningLevel.Critical, BankruptcyForecast.Worst(w));
        }

        [Test]
        public void GenerousOffer_PaysAndAddsDebt()
        {
            CasinoRun run = NewRun(seed: 3, rounds: 18);
            run.Contracts.Offer = ContractCatalog.Get("generous_offer");
            Assert.IsNull(run.SignContract("generous_offer"));
            Assert.AreEqual(7500, run.Ledger.Balance);
            Assert.AreEqual(60000, run.Ledger.Debt);
        }

        [Test]
        public void DebtFreeze_BlocksDebtIncreases_AndCountsAsALoophole()
        {
            CasinoRun run = NewRun();
            run.Inventory.AddCharges("debt_freeze", 1);
            Assert.IsTrue(OpenBetting(run));
            Assert.IsTrue(run.UseAbility("debt_freeze").Success);
            run.Contracts.Offer = ContractCatalog.Get("generous_offer");
            Assert.IsNull(run.SignContract("generous_offer"));
            Assert.AreEqual(50000, run.Ledger.Debt, "Frozen debt cannot grow.");
            Assert.AreEqual(1, run.Profile.TotalLoopholeExploits);
        }

        [Test]
        public void LoanShark_RepaysFromTheBalance()
        {
            CasinoRun run = NewRun(balance: 10000);
            var contract = run.Contracts.Sign(ContractCatalog.Get("loan_shark"), run);
            Assert.AreEqual(12000, run.Ledger.Balance);
            PlayRound(run);
            Assert.AreEqual(ContractStatus.Fulfilled, contract.Status);
            Assert.AreEqual(0, contract.Outstanding);
        }

        // ------------------------------------------------------------------ abilities

        [Test]
        public void Abilities_RespectOwnershipCostAndCooldown()
        {
            CasinoRun run = NewRun();
            Assert.IsTrue(OpenBetting(run));
            Assert.IsFalse(run.UseAbility("lucky_coin", 7).Success, "Not owned yet.");
            run.Inventory.AddUpgrade("lucky_coin");
            run.Influence.Restore(10, 10, 0);
            AbilityResult r = run.UseAbility("lucky_coin", 7);
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(7, run.Influence.Current);
            Assert.AreEqual(2, run.Table.Wheel.CountOf(7), "Lucky Coin is a real, visible wheel change.");
            Assert.AreEqual(38, run.Table.Wheel.PocketCount);
            Assert.Less(run.Table.PayoutRatio(BetSelection.Straight(7)), 35m, "Payouts follow the new odds.");
            SpinAndFinish(run);
            Assert.IsTrue(OpenBetting(run));
            Assert.IsFalse(run.UseAbility("lucky_coin", 7).Success, "Cooldown.");
            Assert.AreEqual(1, run.Table.Wheel.CountOf(7), "The coin lasts one spin.");
        }

        [Test]
        public void HouseInspection_RevealsHiddenRules()
        {
            CasinoRun run = NewRun();
            run.Rules.Catalog.Tune("house_cut", null, null, null, 1);
            Assert.IsTrue(run.ActivateRule("house_cut", true));
            RuleInstance hidden = run.Rules.Find("house_cut");
            Assert.IsFalse(hidden.IsVisible);
            Assert.IsTrue(run.UseAbility("house_inspection").Success);
            Assert.IsTrue(hidden.IsVisible);
            Assert.IsTrue(run.Insight.RevealWheel);
        }

        [Test]
        public void ChipAnchor_StopsTheShake()
        {
            CasinoRun run = NewRun();
            run.Inventory.AddUpgrade("chip_anchor");
            Assert.IsTrue(OpenBetting(run));
            run.PlaceBet(BetSelection.Straight(17), 100);
            Assert.IsTrue(run.UseAbility("chip_anchor").Success);
            ShakeReport report = run.Table.Shake(100, run.Random);
            Assert.AreEqual(0, report.Displacements.Count);
        }

        [Test]
        public void RuleCancel_RemovesTheAnnouncedIntervention()
        {
            CasinoRun run = NewRun();
            run.Inventory.AddUpgrade("rule_cancel");
            run.Influence.Restore(10, 10, 0);
            Assert.IsTrue(OpenBetting(run));
            Assert.IsTrue(run.ImposeRule("higher_stakes"));
            long raised = run.Table.Modifiers.MinimumBet;
            Assert.Greater(raised, 10);
            AbilityResult r = run.UseAbility("rule_cancel");
            Assert.IsTrue(r.Success, r.Message);
            Assert.IsFalse(run.Rules.IsActive("higher_stakes"));
            Assert.AreEqual(10, run.Table.Modifiers.MinimumBet);
        }

        [Test]
        public void ConsumableAbilities_UseCharges()
        {
            CasinoRun run = NewRun();
            run.Inventory.AddCharges("bet_insurance", 1);
            Assert.IsTrue(OpenBetting(run));
            run.PlaceBet(BetSelection.Straight(3), 100);
            Assert.IsTrue(run.UseAbility("bet_insurance").Success);
            Assert.AreEqual(0, run.Inventory.ChargesOf("bet_insurance"));
            Assert.AreEqual(50, run.Table.Bets.Get(BetSelection.Straight(3)).InsuredPercent);
        }

        // ------------------------------------------------------------------ special pockets

        [Test]
        public void SpecialPockets_DebtReliefTaxInfluenceAndTicket()
        {
            CasinoRun run = NewRun(balance: 2000);
            var p = new Pocket(500, 4, PocketColor.Black);
            run.Pockets.Get(SpecialPocketType.DebtRelief).Resolve(run, FakeResult(run, p), p);
            Assert.AreEqual(49000, run.Ledger.Debt);
            run.Pockets.Get(SpecialPocketType.Tax).Resolve(run, FakeResult(run, p), p);
            Assert.AreEqual(1800, run.Ledger.Balance);
            int influence = run.Influence.Current;
            run.Pockets.Get(SpecialPocketType.Influence).Resolve(run, FakeResult(run, p), p);
            Assert.AreEqual(influence + 2, run.Influence.Current);
            run.Pockets.Get(SpecialPocketType.GoldenTicket).Resolve(run, FakeResult(run, p), p);
            Assert.AreEqual(1, run.Inventory.GoldenTickets);
        }

        [Test]
        public void BombPocket_DestroysItsNeighbours()
        {
            CasinoRun run = NewRun();
            Assert.IsTrue(run.ChangeBaseWheel(w => WheelTransforms.SetSpecial(w, 5, SpecialPocketType.Bomb)));
            Pocket bomb = run.Rules.BaseWheel.Pockets[5];
            int before = run.Rules.BaseWheel.PocketCount;
            run.Pockets.Get(SpecialPocketType.Bomb).Resolve(run, FakeResult(run, bomb), bomb);
            Assert.AreEqual(before - 2, run.Rules.BaseWheel.PocketCount);
            Assert.AreEqual(SpecialPocketType.None, run.Rules.BaseWheel.Pockets[run.Rules.BaseWheel.IndexOfId(bomb.Id)].Special);
            Assert.AreEqual(before - 2, run.Table.Wheel.PocketCount, "The table follows between rounds.");
        }

        [Test]
        public void CursePocket_ImposesANegativeRule()
        {
            CasinoRun run = NewRun();
            var p = new Pocket(501, 9, PocketColor.Red);
            int active = run.Rules.Active.Count;
            run.Pockets.Get(SpecialPocketType.Curse).Resolve(run, FakeResult(run, p), p);
            Assert.AreEqual(active + 1, run.Rules.Active.Count);
            Assert.IsTrue(run.Rules.Active.Last().Definition.Negative);
        }

        [Test]
        public void GoldenTicket_CancelsHisNextIntervention()
        {
            CasinoRun run = NewRun(seed: 21);
            run.Inventory.AddGoldenTicket();
            int cancelled = 0;
            run.InterventionAnnounced += n => { if (n.Cancelled) cancelled++; };
            for (int i = 0; i < 6 && cancelled == 0; i++) PlayRound(run);
            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(0, run.Inventory.GoldenTickets);
        }

        // ------------------------------------------------------------------ events and challenges

        [Test]
        public void HouseCollects_TakesAPercentOfTheBalance()
        {
            CasinoRun run = NewRun(balance: 10000);
            run.Events.Start(run.Events.Get("house_collects"), run);
            Assert.AreEqual(10000 - 10000 * TheHouseCollects.PercentAt(1) / 100, run.Ledger.Balance);
        }

        [Test]
        public void DoubleSpin_SpinsTwiceWithOneBettingWindow()
        {
            CasinoRun run = NewRun(balance: 5000);
            Assert.IsTrue(OpenBetting(run));
            run.Events.Start(run.Events.Get("double_spin"), run);
            run.PlaceBet(BetSelection.Outside(BetType.Red), 100);
            int spins = 0;
            run.SpinResolved += r => spins++;
            SpinAndFinish(run);
            Assert.AreEqual(2, spins);
            Assert.AreEqual(1, run.Run.RoundsPlayed);
        }

        [Test]
        public void Auction_HiddenReserveDecidesTheSale()
        {
            CasinoRun run = NewRun(balance: 20000);
            Assert.IsTrue(OpenBetting(run));
            run.Events.Start(run.Events.Get("auction"), run);
            AuctionLot lot = run.Events.Auction.Lots[0];
            string low = run.Bid(0, lot.RevealReserve() - 10);
            StringAssert.StartsWith("Refused", low);
            Assert.AreEqual(20000, run.Ledger.Balance, "A refused bid costs nothing.");
            AuctionLot second = run.Events.Auction.Lots[1];
            StringAssert.StartsWith("Sold", run.Bid(1, second.RevealReserve()));
            Assert.AreEqual(20000 - second.RevealReserve(), run.Ledger.Balance);
        }

        [Test]
        public void Challenge_OutsideOnly_FailsOnAnInsideBet()
        {
            CasinoRun run = NewRun();
            typeof(ChallengeBoard).GetProperty("Offer").SetValue(run.Challenges, run.Challenges.Get("outside_only"));
            Assert.IsNotNull(run.AcceptChallenge());
            OpenBetting(run);
            run.PlaceBet(BetSelection.Straight(12), 10);
            SpinAndFinish(run);
            Assert.IsNull(run.Challenges.Active);
            Assert.AreEqual(ChallengeStatus.Lost, run.Challenges.History[0].Status);
        }

        [Test]
        public void Challenge_ReachBalance_PaysItsReward()
        {
            CasinoRun run = NewRun(balance: 2000);
            typeof(ChallengeBoard).GetProperty("Offer").SetValue(run.Challenges, run.Challenges.Get("reach_balance"));
            ChallengeInstance c = run.AcceptChallenge();
            Assert.AreEqual(3000, c.Target);
            run.Ledger.Credit(1500, TransactionKind.Reward, "test");
            PlayRound(run);
            Assert.AreEqual(ChallengeStatus.Won, c.Status);
            Assert.AreEqual(50000 - 2500, run.Ledger.Debt);
            Assert.IsTrue(run.Inventory.HasUpgrade("rule_peek"));
        }

        // ------------------------------------------------------------------ the final encounter and endings

        [Test]
        public void FinalEncounter_PhasesFollowTheLastStage()
        {
            var f = new FinalEncounter(16, 18, 0);
            Assert.AreEqual(FinalPhase.HisHouse, f.PhaseFor(16));
            Assert.AreEqual(FinalPhase.HisGame, f.PhaseFor(17));
            Assert.AreEqual(FinalPhase.TheLastSpin, f.PhaseFor(18));
            Assert.AreEqual(FinalPhase.None, f.PhaseFor(15));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void FinalEncounter_HisGameBuildsAValidWheelWithRingsDuplicatesAndSpecials(int variant)
        {
            var f = new FinalEncounter(16, 18, variant);
            WheelChange change = f.BuildHisGame(StandardWheels.European(), new DeadSpin.Core.Common.SeededRandom(9));
            Assert.IsTrue(change.Succeeded, change.Error);
            WheelConfiguration w = change.After;
            Assert.IsTrue(w.IsValid);
            Assert.IsNotNull(w.Ring);
            Assert.IsTrue(w.Pockets.GroupBy(p => p.Number).Any(g => !g.First().IsZero && g.Count() > 1));
            Assert.GreaterOrEqual(w.Pockets.Count(p => p.Special != SpecialPocketType.None), 2);
        }

        [Test]
        public void ShortRun_ReachesTheFinalOffer_AndTheDealEndsIt()
        {
            CasinoRun run = NewRun(rounds: 6, balance: 100000);
            int guard = 0;
            while (run.PendingDecision == PendingDecision.None && !run.Run.IsOver && guard++ < 20) PlayRound(run, 10);
            Assert.AreEqual(PendingDecision.FinalOffer, run.PendingDecision);
            Assert.AreEqual(FinalPhase.HisFinalOffer, run.CurrentFinalPhase);
            Assert.IsTrue(run.AnswerFinalOffer(FinalOfferChoice.TheDeal));
            Assert.IsTrue(run.Run.IsOver);
            Assert.AreEqual(RunOutcome.TheDeal, run.Run.Outcome);
            Assert.AreEqual("the_deal", run.Ending.Id);
        }

        [Test]
        public void ShortRun_RefusingTheOffer_PlaysTheLastSpinAndEnds()
        {
            CasinoRun run = NewRun(rounds: 6, balance: 100000);
            int guard = 0;
            while (!run.Run.IsOver && guard++ < 20)
            {
                if (run.PendingDecision == PendingDecision.FinalOffer) run.AnswerFinalOffer(FinalOfferChoice.Refuse);
                else PlayRound(run, 50);
            }
            Assert.IsTrue(run.Run.IsOver);
            Assert.IsNotNull(run.Ending);
            Assert.IsTrue(run.Rules.Catalog.Get("final_last_spin") != null);
        }

        [Test]
        public void Endings_FreedomAtTheFinalTableWithMoneyLeftIsTheNewOwner()
        {
            EndingCatalog e = EndingCatalog.CreateDefault();
            var ctx = new EndingContext { RawOutcome = RunOutcome.Freedom, StartingDebt = 50000, Balance = 20000, ReachedFinalTable = true };
            Assert.AreEqual("new_owner", e.Resolve(ctx).Id);
            ctx.Balance = 100;
            Assert.AreEqual("freedom", e.Resolve(ctx).Id);
            ctx.Balance = 20000;
            ctx.ReachedFinalTable = false;
            Assert.AreEqual("freedom", e.Resolve(ctx).Id);
        }

        [Test]
        public void Endings_LoopholeNeedsEvidenceAndAPartlyPaidDebt()
        {
            EndingCatalog e = EndingCatalog.CreateDefault();
            var ctx = new EndingContext { RawOutcome = RunOutcome.DeadlineMissed, StartingDebt = 50000, Debt = 30000, LoopholeEvidence = 6 };
            Assert.AreEqual("loophole", e.Resolve(ctx).Id);
            ctx.LoopholeEvidence = 5;
            Assert.AreEqual("house_always_wins_deadline", e.Resolve(ctx).Id);
            ctx.LoopholeEvidence = 9;
            ctx.Debt = 45000;
            Assert.AreEqual("house_always_wins_deadline", e.Resolve(ctx).Id);
            Assert.AreEqual("house_always_wins", e.Resolve(new EndingContext { RawOutcome = RunOutcome.Bankrupt }).Id);
        }

        [Test]
        public void PayingTheDebt_EndsInFreedom()
        {
            CasinoRun run = NewRun(balance: 60000);
            run.PayDebt(50000);
            Assert.IsTrue(run.Run.IsOver);
            Assert.AreEqual("freedom", run.Ending.Id);
        }

        // ------------------------------------------------------------------ rooms and meta

        [Test]
        public void Records_SecretsGiveInfluenceOnce()
        {
            CasinoRun run = NewRun();
            int before = run.Influence.Current;
            Assert.IsNotNull(run.ReadRecord("house_rules"));
            Assert.AreEqual(before + 1, run.Influence.Current);
            run.ReadRecord("house_rules");
            Assert.AreEqual(before + 1, run.Influence.Current);
            Assert.IsNull(run.ReadRecord("the_deed"), "Locked until stage 4.");
            Assert.AreEqual(1, run.LoopholeEvidence);
        }

        [Test]
        public void Lounge_RestsOncePerStage()
        {
            CasinoRun run = NewRun();
            int before = run.Influence.Current;
            Assert.IsNotEmpty(run.Rest());
            run.Rest();
            Assert.AreEqual(before + 1, run.Influence.Current);
        }

        [Test]
        public void Shop_SellsThroughTheLedger()
        {
            CasinoRun run = NewRun(balance: 50000);
            ShopOffer offer = run.Shop.Shelf.First();
            Assert.IsNull(run.BuyItem(offer.Item.Id));
            Assert.AreEqual(50000 - offer.Price, run.Ledger.Balance);
        }

        [Test]
        public void Meta_MarksUnlocksAndRunSetup()
        {
            var meta = new MetaProgress();
            int marks = meta.RecordRun(new RunSummary { EndingId = "freedom", RoundsPlayed = 18, DebtPaid = 50000 });
            Assert.Greater(marks, 10);
            Assert.IsNull(meta.TryUnlock("gm_trickster"));
            Assert.IsTrue(meta.IsUnlocked("gm_trickster"));
            Assert.IsNotNull(meta.TryUnlock("env_flooded"), "Needs the House Always Wins ending first.");

            RunSetup a = RunSetup.Create(77, meta);
            RunSetup b = RunSetup.Create(77, meta);
            Assert.AreEqual(a.Config.StartingDebt, b.Config.StartingDebt);
            Assert.AreEqual(a.Personality.Id, b.Personality.Id);
            Assert.AreEqual(a.FinalVariant, b.FinalVariant);
            Assert.That(a.Config.StartingDebt, Is.InRange(45000, 60000));
            Assert.That(a.Config.StartingBalance, Is.InRange(2000, 3000));
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void MiniJson_RoundTrips()
        {
            var data = new Dictionary<string, object>
            {
                { "a", 1L }, { "b", "text \"quoted\"\n" }, { "c", new List<object> { true, false, null, 2.5 } }, { "big", ulong.MaxValue }
            };
            var back = (IDictionary<string, object>)MiniJson.Deserialize(MiniJson.Serialize(data));
            Assert.AreEqual(1L, back["a"]);
            Assert.AreEqual("text \"quoted\"\n", back["b"]);
            Assert.AreEqual(2.5, ((List<object>)back["c"])[3]);
            Assert.AreEqual(ulong.MaxValue, back["big"]);
        }

        [Test]
        public void SaveAndLoad_ContinueTheSameGame()
        {
            CasinoRun original = NewRun(seed: 31, balance: 20000);
            original.Inventory.AddCharges("double_down", 2);
            for (int i = 0; i < 5; i++) PlayRound(original, 100);
            Assert.IsTrue(SaveSystem.CanSave(original));
            string json = SaveSystem.SaveRun(original);
            CasinoRun loaded = SaveSystem.LoadRun(json);

            Assert.AreEqual(original.Ledger.Balance, loaded.Ledger.Balance);
            Assert.AreEqual(original.Ledger.Debt, loaded.Ledger.Debt);
            Assert.AreEqual(original.Run.RoundsPlayed, loaded.Run.RoundsPlayed);
            Assert.AreEqual(original.Table.Wheel.PocketCount, loaded.Table.Wheel.PocketCount);
            Assert.AreEqual(original.Rules.Active.Count, loaded.Rules.Active.Count);
            Assert.AreEqual(2, loaded.Inventory.ChargesOf("double_down"));
            Assert.AreEqual(original.GameMaster.Personality.Id, loaded.GameMaster.Personality.Id);

            for (int i = 0; i < 3; i++)
            {
                RoundResult a = PlayRound(original, 100);
                RoundResult b = PlayRound(loaded, 100);
                if (a == null || b == null) break;
                Assert.AreEqual(a.Pocket.Number, b.Pocket.Number, "The random sequence continues exactly.");
            }
            Assert.AreEqual(original.Ledger.Balance, loaded.Ledger.Balance);
        }

        [Test]
        public void Meta_SavesAndLoads()
        {
            var meta = new MetaProgress { Marks = 12, RunsStarted = 3 };
            meta.RecordRun(new RunSummary { EndingId = "loophole", RoundsPlayed = 18 });
            meta.TryUnlock("chips_fragile");
            MetaProgress back = SaveSystem.LoadMeta(SaveSystem.SaveMeta(meta));
            Assert.AreEqual(meta.Marks, back.Marks);
            Assert.IsTrue(back.IsUnlocked("chips_fragile"));
            Assert.IsTrue(back.HasReached("loophole"));
        }

        [Test]
        public void Saving_IsRefusedMidRound()
        {
            CasinoRun run = NewRun();
            OpenBetting(run);
            Assert.IsFalse(SaveSystem.CanSave(run));
        }
    }
}

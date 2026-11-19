using System;
using System.Collections.Generic;
using System.Text;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.Simulation
{
    public enum BotStyle
    {
        /// <summary>Small outside bets, pays installments early, rarely takes risks.</summary>
        Cautious,
        /// <summary>A mix of outside and inside bets sized to the debt still owed.</summary>
        Balanced,
        /// <summary>Large inside bets chasing the debt.</summary>
        Aggressive,
        /// <summary>"Bold play": on one number, just enough that a hit covers the debt (or everything), until the debt is within reach.</summary>
        Bold
    }

    /// <summary>What happened in one simulated run.</summary>
    public sealed class SimulatedRun
    {
        public ulong Seed;
        public BotStyle Style;
        public string EndingId;
        public RunOutcome Outcome;
        public int RoundsPlayed;
        public int StageReached;
        public long FinalDebt;
        public long FinalBalance;
        public long PeakBalance;
        public int Interventions;
        public int Events;
        public int SpecialPocketHits;
        public int AbilitiesUsed;
        public int LoopholesFound;
        public int ChallengesWon;
        public int Spins;
        public int MaxPockets;
        /// <summary>Problems found by the invariant checks (money conservation, illegal state). Empty when healthy.</summary>
        public List<string> Problems = new List<string>();
    }

    /// <summary>Totals over many simulated runs (Phase 10 balancing).</summary>
    public sealed class SimulationReport
    {
        public readonly List<SimulatedRun> Runs = new List<SimulatedRun>();

        public int Count => Runs.Count;

        public Dictionary<string, int> EndingCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (SimulatedRun r in Runs)
            {
                int n;
                counts[r.EndingId ?? "none"] = counts.TryGetValue(r.EndingId ?? "none", out n) ? n + 1 : 1;
            }
            return counts;
        }

        public double WinRate
        {
            get
            {
                if (Runs.Count == 0) return 0;
                int wins = 0;
                foreach (SimulatedRun r in Runs) if (r.EndingId == "freedom" || r.EndingId == "new_owner" || r.EndingId == "loophole") wins++;
                return (double)wins / Runs.Count;
            }
        }

        public double Average(Func<SimulatedRun, double> value)
        {
            if (Runs.Count == 0) return 0;
            double total = 0;
            foreach (SimulatedRun r in Runs) total += value(r);
            return total / Runs.Count;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Runs: " + Count + ", win rate " + (WinRate * 100).ToString("0.0") + "%");
            foreach (KeyValuePair<string, int> e in EndingCounts()) sb.AppendLine("  " + e.Key + ": " + e.Value);
            sb.AppendLine("Average rounds played: " + Average(r => r.RoundsPlayed).ToString("0.0"));
            sb.AppendLine("Average stage reached: " + Average(r => r.StageReached).ToString("0.0"));
            sb.AppendLine("Average final debt: $" + Average(r => r.FinalDebt).ToString("N0"));
            sb.AppendLine("Average interventions: " + Average(r => r.Interventions).ToString("0.0") + ", events: " + Average(r => r.Events).ToString("0.0")
                          + ", special pockets hit: " + Average(r => r.SpecialPocketHits).ToString("0.0"));
            int problems = 0;
            foreach (SimulatedRun r in Runs) problems += r.Problems.Count;
            sb.AppendLine("Invariant problems: " + problems);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Plays complete runs headlessly with a simple bot, exercising every system (rules, the Game
    /// Master, events, pockets, contracts, challenges, the shop, the final encounter and the endings)
    /// and checking invariants after every step. Used by the balance tests and the editor's
    /// "Simulate runs" menu.
    /// </summary>
    public static class RunSimulator
    {
        public static SimulationReport SimulateMany(int count, ulong firstSeed, BotStyle style, MetaProgress meta = null)
        {
            var report = new SimulationReport();
            for (int i = 0; i < count; i++) report.Runs.Add(Simulate(firstSeed + (ulong)i * 7919UL, style, meta));
            return report;
        }

        public static SimulatedRun Simulate(ulong seed, BotStyle style, MetaProgress meta = null)
        {
            RunSetup setup = RunSetup.Create(seed, meta);
            setup.Config.AnnouncementSeconds = 1f;
            setup.Config.SummarySeconds = 1f;
            var run = new CasinoRun(setup, meta);
            var sim = new SimulatedRun { Seed = seed, Style = style };
            var bot = new SeededRandom(seed ^ 0xB07UL);
            run.InterventionAnnounced += n => { if (!n.Cancelled) sim.Interventions++; };
            run.Events.Started += e => sim.Events++;
            run.SpecialPocketsResolved += list => sim.SpecialPocketHits += list.Count;
            run.Abilities.Used += (a, m) => sim.AbilitiesUsed++;

            run.Start();
            int steps = 0;
            bool betThisRound = false;
            while (!run.Run.IsOver && steps++ < 20000)
            {
                RoundPhase phase = run.Run.Round.Phase;
                switch (phase)
                {
                    case RoundPhase.Idle:
                        if (run.PendingDecision == PendingDecision.FinalOffer)
                        {
                            run.AnswerFinalOffer(ChooseFinalOffer(run, style));
                            continue;
                        }
                        BetweenRounds(run, style, bot);
                        betThisRound = false;
                        if (!run.BeginRound() && run.PendingDecision == PendingDecision.None && !run.Run.IsOver)
                        {
                            sim.Problems.Add("BeginRound refused in round " + run.CurrentRound + " with no decision pending.");
                            steps = int.MaxValue - 1;
                        }
                        break;
                    case RoundPhase.Announcing:
                    case RoundPhase.Summary:
                        run.Tick(run.Run.Round.TimeRemaining + 0.01f);
                        break;
                    case RoundPhase.Betting:
                        if (!betThisRound)
                        {
                            betThisRound = true;
                            PlaceBets(run, style, bot);
                            UseAbilities(run, bot);
                        }
                        run.Tick(2f);
                        break;
                    case RoundPhase.BettingClosed:
                        run.StartSpin();
                        sim.Spins++;
                        sim.MaxPockets = Math.Max(sim.MaxPockets, run.Table.Wheel.PocketCount);
                        run.FinishSpin();
                        break;
                    default:
                        sim.Problems.Add("Unexpected phase " + phase + ".");
                        steps = int.MaxValue - 1;
                        break;
                }
                CheckInvariants(run, sim);
                sim.PeakBalance = Math.Max(sim.PeakBalance, run.Ledger.Balance + run.Ledger.Stored);
                if (sim.Problems.Count > 20) break;
            }
            if (!run.Run.IsOver) sim.Problems.Add("The run did not end (phase " + run.Run.Round.Phase + ", round " + run.CurrentRound + ").");

            sim.Outcome = run.Run.Outcome;
            sim.EndingId = run.Ending != null ? run.Ending.Id : null;
            sim.RoundsPlayed = run.Run.RoundsPlayed;
            sim.StageReached = run.Stage;
            sim.FinalDebt = run.Ledger.Debt;
            sim.FinalBalance = run.Ledger.Balance + run.Ledger.Stored;
            sim.LoopholesFound = run.Profile.TotalLoopholeExploits;
            sim.ChallengesWon = run.Challenges.Won;
            return sim;
        }

        private static void CheckInvariants(CasinoRun run, SimulatedRun sim)
        {
            Ledger l = run.Ledger;
            if (l.Balance < 0 || l.Debt < 0 || l.Stored < 0) sim.Problems.Add("Negative money: balance " + l.Balance + ", debt " + l.Debt + ".");
            long tableTotal = run.Table.Bets.Total + run.Table.DisplacedTotal;
            if (run.Run.MoneyAtRisk != tableTotal && !run.Run.IsOver) sim.Problems.Add("Money at risk " + run.Run.MoneyAtRisk + " does not match the table " + tableTotal + ".");
            if (!run.Table.Wheel.IsValid) sim.Problems.Add("The wheel became invalid.");
            if (run.Table.Modifiers.Validate().Count > 0) sim.Problems.Add("Invalid modifiers: " + run.Table.Modifiers.Validate()[0]);
            if (run.Influence.Current < 0) sim.Problems.Add("Negative Influence.");
        }

        private static FinalOfferChoice ChooseFinalOffer(CasinoRun run, BotStyle style)
        {
            long gap = run.Ledger.Debt - run.Ledger.Balance - run.Ledger.Stored;
            if (gap <= 0) return FinalOfferChoice.Refuse;
            if (style == BotStyle.Aggressive) return FinalOfferChoice.EscapeClause;
            if (style == BotStyle.Cautious && gap > run.StartingDebt / 2) return FinalOfferChoice.TheDeal;
            return gap * 4 > run.StartingDebt ? FinalOfferChoice.EscapeClause : FinalOfferChoice.Refuse;
        }

        private static void BetweenRounds(CasinoRun run, BotStyle style, IRandomSource bot)
        {
            // Pay the debt off once it is within reach; keep a playing bankroll otherwise.
            long bankroll = Math.Max(run.Run.EffectiveMinimumBet * 20, 1500);
            if (run.Ledger.Stored > 0) run.Withdraw(run.Ledger.Stored);
            if (run.Ledger.Balance >= run.Ledger.Debt) run.PayDebt(run.Ledger.Debt);
            else
            {
                Installment next = run.Payments.Next;
                long owed = run.Payments.OutstandingFor(next);
                if (next != null && owed > 0 && next.DueRound <= run.CurrentRound && run.Ledger.Balance > owed + bankroll / 2) run.PayDebt(owed);
                if (style == BotStyle.Cautious && run.Ledger.Balance > bankroll * 4) run.PayDebt(run.Ledger.Balance - bankroll * 2);
            }

            if (run.Challenges.Offer != null)
            {
                if (bot.NextInt(100) < 60) run.AcceptChallenge();
                else run.DeclineChallenge();
            }
            if (run.Contracts.Offer != null)
            {
                string id = run.Contracts.Offer.Id;
                bool take = id == "devils_bargain" || id == "insurance" || id == "strangers_wager" && run.Ledger.Balance > 4000
                            || id == "double_or_nothing" && style == BotStyle.Aggressive || id == "generous_offer" && run.Ledger.Balance < 1000;
                if (take) run.SignContract(id);
                else run.RefuseContractOffer();
            }
            if (run.Ledger.Balance > 3000 && bot.NextInt(100) < 40)
            {
                foreach (ShopOffer offer in run.Shop.Shelf)
                {
                    if (offer.Remaining > 0 && offer.Price < run.Ledger.Balance / 6)
                    {
                        run.BuyItem(offer.Item.Id);
                        break;
                    }
                }
            }
            if (bot.NextInt(100) < 30) run.PredictIntervention(Rules.RuleCategory.Wheel);
            if (bot.NextInt(100) < 20)
            {
                foreach (RecordEntry entry in run.Records.Entries) if (!run.Records.HasRead(entry.Id)) { run.ReadRecord(entry.Id); break; }
            }
            if (run.Records.CanRest(run.Stage)) run.Rest();
        }

        private static void PlaceBets(CasinoRun run, BotStyle style, IRandomSource bot)
        {
            TableGame t = run.Table;
            long min = t.Modifiers.MinimumBet;
            long balance = run.Ledger.Balance;
            if (balance < min) return;
            long debtLeft = Math.Max(0, run.Ledger.Debt - balance);
            long roundsLeft = Math.Max(1, run.Run.RoundsRemaining);
            long target;
            switch (style)
            {
                case BotStyle.Cautious: target = balance / 20; break;
                case BotStyle.Aggressive: target = Math.Max(balance / 4, debtLeft / roundsLeft / 30); break;
                case BotStyle.Bold:
                    // Classic bold play: stake just enough that one hit covers what is still needed.
                    target = debtLeft > 0 ? Math.Min(balance, Math.Max(min, (debtLeft - balance) / 30 + min)) : balance / 20;
                    break;
                default: target = Math.Max(balance / 10, debtLeft / roundsLeft / 15); break;
            }
            target = Math.Max(min, Math.Min(target, Math.Min(balance, t.Modifiers.MaximumBet)));
            target = target / 5 * 5;
            if (target < min) target = min;

            var options = new List<BetSelection>();
            switch (style)
            {
                case BotStyle.Cautious:
                    options.Add(BetSelection.Outside(BetType.Red));
                    options.Add(BetSelection.Outside(BetType.Black));
                    options.Add(BetSelection.Outside(BetType.Dozen, bot.NextInt(3)));
                    break;
                case BotStyle.Aggressive:
                    options.Add(BetSelection.Straight(1 + bot.NextInt(Math.Max(1, t.HighestNumber))));
                    options.Add(BetSelection.Outside(BetType.Column, bot.NextInt(3)));
                    break;
                case BotStyle.Bold:
                    if (debtLeft > 0)
                    {
                        // Spread across a few single numbers in case one is closed.
                        options.Add(BetSelection.Straight(1 + bot.NextInt(Math.Max(1, t.HighestNumber))));
                    }
                    else options.Add(BetSelection.Outside(BetType.Red));
                    break;
                default:
                    options.Add(BetSelection.Outside(BetType.Dozen, bot.NextInt(3)));
                    options.Add(BetSelection.Outside(BetType.Odd));
                    options.Add(BetSelection.Straight(1 + bot.NextInt(Math.Max(1, t.HighestNumber))));
                    if (t.Modifiers.SpecialBetsAllowed && bot.NextInt(100) < 25)
                    {
                        BetSelection special = t.CreateSpecialBet(BetType.Neighbor, 1 + bot.NextInt(Math.Max(1, t.HighestNumber)));
                        if (special != null) options.Add(special);
                    }
                    break;
            }

            long remaining = target;
            foreach (BetSelection option in options)
            {
                long amount = Math.Max(min, remaining / options.Count);
                if (amount > run.Ledger.Balance) break;
                ChipKind kind = ChipKind.Standard;
                foreach (KeyValuePair<ChipKind, int> chips in run.Inventory.Chips) if (chips.Value > 0) { kind = chips.Key; break; }
                PlacementResult r = run.PlaceBet(option, amount, kind);
                if (r != PlacementResult.Accepted && kind != ChipKind.Standard) r = run.PlaceBet(option, amount);
                if (r == PlacementResult.Accepted) remaining -= amount;
            }
        }

        private static void UseAbilities(CasinoRun run, IRandomSource bot)
        {
            if (bot.NextInt(100) >= 35) return;
            var usable = new List<AbilityDefinition>();
            foreach (AbilityDefinition a in run.Abilities.All)
            {
                int number = a.Target == AbilityTarget.Number ? Math.Max(1, run.Profile.FavoriteNumber) : -1;
                if (run.Abilities.Check(a, run, number) == null) usable.Add(a);
            }
            if (usable.Count == 0) return;
            AbilityDefinition pick = usable[bot.NextInt(usable.Count)];
            run.UseAbility(pick.Id, pick.Target == AbilityTarget.Number ? Math.Max(1, run.Profile.FavoriteNumber) : -1);
        }
    }
}

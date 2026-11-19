using System;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;

namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// Aggregate root for one playthrough: owns the configuration, the ledger (money and debt),
    /// the round counter, the round phase flow, the seeded random source and the run outcome.
    /// Unity-side code holds exactly one Run via GameSession; nothing else duplicates this state.
    /// </summary>
    public sealed class Run
    {
        public RunConfig Config { get; }
        public Ledger Ledger { get; }
        public RoundFlow Round { get; }
        public IRandomSource Random { get; }

        /// <summary>Number of rounds fully completed.</summary>
        public int RoundsPlayed { get; private set; }

        /// <summary>1-based number of the round currently in progress, or of the next round to start.</summary>
        public int CurrentRoundNumber => Math.Min(RoundsPlayed + 1, Config.TotalRounds);

        public int RoundsRemaining => Config.TotalRounds - RoundsPlayed;

        public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;

        public bool IsOver => RunOutcomeEvaluator.IsTerminal(Outcome);

        /// <summary>Money wagered on the table and not yet resolved. Set by the betting system (Phase 2+).</summary>
        public long MoneyAtRisk { get; private set; }

        /// <summary>Minimum bet after rule modifiers; bankruptcy is judged against this. Set by the table.</summary>
        public long EffectiveMinimumBet
        {
            get { return _effectiveMinimumBet > 0 ? _effectiveMinimumBet : Config.MinimumBet; }
            set { _effectiveMinimumBet = value; }
        }
        private long _effectiveMinimumBet;

        /// <summary>
        /// Optional extra failure check supplied by the economy (missed installment, unpayable mandatory
        /// wager). Returns true when the player can no longer meet the game's financial requirements.
        /// </summary>
        public Func<bool> ExtraBankruptcyCheck { get; set; }

        /// <summary>Optional override that decides the outcome at the deadline (e.g. the final encounter's endings).</summary>
        public Func<RunOutcome> DeadlineOverride { get; set; }

        /// <summary>Optional final say on every terminal outcome (the ending system turns Freedom into The New Owner, a missed deadline into The Loophole).</summary>
        public Func<RunOutcome, RunOutcome> OutcomeOverride { get; set; }

        /// <summary>Raised once when the run reaches a terminal outcome.</summary>
        public event Action<RunOutcome> Ended;

        /// <summary>Raised after a round finishes and the round counter advances.</summary>
        public event Action<int> RoundAdvanced;

        public Run(RunConfig config, IRandomSource random = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.ThrowIfInvalid();

            Config = config.Clone();
            if (random == null)
            {
                random = Config.Seed != 0 ? new SeededRandom(Config.Seed) : SeededRandom.FromTime();
            }
            Random = random;

            Ledger = new Ledger(Config.StartingBalance, Config.StartingDebt);
            Ledger.CurrentRound = CurrentRoundNumber;
            Ledger.Changed += OnLedgerChanged;

            Round = new RoundFlow(Config);
            Round.RoundCompleted += OnRoundCompleted;
            Round.PhaseChanged += OnRoundPhaseChanged;
        }

        /// <summary>Starts the next round if the run is still in progress.</summary>
        public bool TryBeginRound()
        {
            if (IsOver) return false;
            return Round.TryBeginRound();
        }

        /// <summary>Updates the amount of unresolved money on the table and re-checks the outcome.</summary>
        public void SetMoneyAtRisk(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            MoneyAtRisk = amount;
            EvaluateOutcome();
        }

        /// <summary>Re-evaluates win/loss conditions. Safe to call at any time.</summary>
        public RunOutcome EvaluateOutcome()
        {
            if (IsOver) return Outcome;

            RunOutcome result = RunOutcomeEvaluator.Evaluate(
                Ledger.Balance + Ledger.Stored, Ledger.Debt, MoneyAtRisk, RoundsPlayed, Config.TotalRounds, EffectiveMinimumBet);
            if (result == RunOutcome.InProgress && ExtraBankruptcyCheck != null && ExtraBankruptcyCheck()) result = RunOutcome.Bankrupt;
            if (result == RunOutcome.DeadlineMissed && DeadlineOverride != null) result = DeadlineOverride();

            if (RunOutcomeEvaluator.IsTerminal(result))
            {
                // Bankruptcy must not fire while a spin is being resolved: winnings may still arrive.
                if (result == RunOutcome.Bankrupt && IsResolvingSpin(Round.Phase))
                {
                    return Outcome;
                }

                if (OutcomeOverride != null) result = OutcomeOverride(result);
                if (!RunOutcomeEvaluator.IsTerminal(result)) return Outcome;
                Outcome = result;
                Round.Abort();
                Ended?.Invoke(result);
            }
            return Outcome;
        }

        /// <summary>Ends the run with a specific outcome (endings decided by the story, e.g. accepting the Game Master's deal).</summary>
        public void ForceOutcome(RunOutcome outcome)
        {
            if (IsOver || !RunOutcomeEvaluator.IsTerminal(outcome)) return;
            Outcome = outcome;
            Round.Abort();
            Ended?.Invoke(outcome);
        }

        /// <summary>Restores counters from a save. Only valid on a fresh run between rounds.</summary>
        public void RestoreProgress(int roundsPlayed)
        {
            if (Round.InProgress) throw new InvalidOperationException("Cannot restore during a round.");
            RoundsPlayed = Math.Max(0, Math.Min(roundsPlayed, Config.TotalRounds));
            Ledger.CurrentRound = CurrentRoundNumber;
        }

        /// <summary>Adds rounds to the deadline (final encounter, contracts).</summary>
        public void ExtendDeadline(int rounds)
        {
            if (rounds <= 0) return;
            Config.TotalRounds += rounds;
        }

        private static bool IsResolvingSpin(RoundPhase phase)
        {
            return phase == RoundPhase.BettingClosed || phase == RoundPhase.Spinning || phase == RoundPhase.Resolving;
        }

        private void OnLedgerChanged(Transaction transaction)
        {
            EvaluateOutcome();
        }

        private void OnRoundPhaseChanged(RoundPhase previous, RoundPhase next)
        {
            // Outcome checks deferred during the spin happen as soon as resolution is done.
            if (next == RoundPhase.Summary) EvaluateOutcome();
        }

        private void OnRoundCompleted()
        {
            RoundsPlayed++;
            Ledger.CurrentRound = CurrentRoundNumber;
            RoundAdvanced?.Invoke(RoundsPlayed);
            EvaluateOutcome();
        }
    }
}

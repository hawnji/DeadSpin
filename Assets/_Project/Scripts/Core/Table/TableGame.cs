using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Table
{
    /// <summary>
    /// The roulette table's rules for one run: placing and removing wagers, choosing the result when
    /// betting closes, and settling bets once the ball has visibly landed. Money always moves through
    /// the run's <see cref="Ledger"/>; phases always move through the run's <see cref="RoundFlow"/>.
    ///
    /// Spin protocol (driven by the Unity table controller):
    ///  1. Betting closes (timer or <see cref="TryCloseBetting"/>).
    ///  2. <see cref="StartSpin"/> picks the winning pocket and returns its index for the animation.
    ///  3. The animation plays; the result is not revealed (<see cref="LastResult"/> is unchanged).
    ///  4. <see cref="FinishSpin"/> settles every bet and publishes the <see cref="RoundResult"/>.
    /// </summary>
    public sealed class TableGame
    {
        private readonly BetBook _bets = new BetBook();
        private int _pendingPocketIndex = -1;

        public Run Run { get; }
        public StandardLayout Layout { get; }
        public PayoutPolicy Payouts { get; }
        public WheelConfiguration Wheel { get; private set; }

        public BetBook Bets => _bets;

        /// <summary>The most recent settled round, or null before the first spin.</summary>
        public RoundResult LastResult { get; private set; }

        /// <summary>Winning numbers, most recent first.</summary>
        public IReadOnlyList<int> History => _history;
        private readonly List<int> _history = new List<int>();

        public bool IsSpinPending => _pendingPocketIndex >= 0;

        public event Action BetsChanged;
        public event Action<RoundResult> RoundResolved;
        public event Action<WheelConfiguration> WheelChanged;

        public TableGame(Run run, WheelConfiguration wheel, PayoutPolicy payouts = null, StandardLayout layout = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            wheel.ThrowIfInvalid();
            Run = run;
            Wheel = wheel;
            Payouts = payouts ?? PayoutPolicy.Standard;
            Layout = layout ?? new StandardLayout();
        }

        public int HighestNumber => StandardLayout.HighestNumber;

        /// <summary>Starts the next round (announcement, then betting).</summary>
        public bool TryBeginRound()
        {
            return !IsSpinPending && Run.TryBeginRound();
        }

        /// <summary>Profit ratio ("to one") this selection would pay on the current wheel.</summary>
        public decimal PayoutRatio(BetSelection selection)
        {
            return Payouts.PayoutRatio(Wheel, selection, HighestNumber);
        }

        /// <summary>Exact chance this selection wins on the current wheel.</summary>
        public double WinProbability(BetSelection selection)
        {
            return WheelOdds.Probability(Wheel, p => selection.Covers(p, HighestNumber));
        }

        /// <summary>Checks a placement without changing anything.</summary>
        public PlacementResult CanPlace(BetSelection selection, long amount)
        {
            if (amount <= 0) return PlacementResult.InvalidAmount;
            if (!Run.Round.IsBettingOpen || Run.IsOver) return PlacementResult.BettingClosed;
            if (!Layout.IsLegal(selection)) return PlacementResult.IllegalBet;
            if (PayoutPolicy.WinningWeight(Wheel, selection, HighestNumber) <= 0) return PlacementResult.CannotWin;

            long newTotal = _bets.AmountOn(selection) + amount;
            if (newTotal < Run.Config.MinimumBet) return PlacementResult.BelowMinimum;
            if (newTotal > Run.Config.MaximumBet) return PlacementResult.AboveMaximum;
            if (!Run.Ledger.CanAfford(amount)) return PlacementResult.InsufficientFunds;
            return PlacementResult.Accepted;
        }

        /// <summary>Adds chips to a spot, taking the money from the balance.</summary>
        public PlacementResult PlaceBet(BetSelection selection, long amount)
        {
            PlacementResult check = CanPlace(selection, amount);
            if (check != PlacementResult.Accepted) return check;

            // Mark the money as at risk before it leaves the balance so the bankruptcy check
            // never sees an empty balance with nothing on the table.
            Run.SetMoneyAtRisk(_bets.Total + amount);
            if (!Run.Ledger.TryDebit(amount, TransactionKind.WagerPlaced, selection.DisplayName))
            {
                Run.SetMoneyAtRisk(_bets.Total);
                return PlacementResult.InsufficientFunds;
            }
            _bets.Add(selection, amount);
            BetsChanged?.Invoke();
            return PlacementResult.Accepted;
        }

        /// <summary>Takes chips back off a spot while betting is open. Removing below the minimum clears the spot.</summary>
        public PlacementResult RemoveBet(BetSelection selection, long amount)
        {
            if (!Run.Round.IsBettingOpen) return PlacementResult.BettingClosed;
            long current = _bets.AmountOn(selection);
            if (current <= 0 || amount <= 0) return PlacementResult.NothingToRemove;

            long remaining = current - Math.Min(current, amount);
            long toRemove = remaining > 0 && remaining < Run.Config.MinimumBet ? current : Math.Min(current, amount);
            long removed = _bets.Remove(selection, toRemove);
            Run.Ledger.Credit(removed, TransactionKind.WagerRefunded, selection.DisplayName);
            Run.SetMoneyAtRisk(_bets.Total);
            BetsChanged?.Invoke();
            return PlacementResult.Accepted;
        }

        /// <summary>Returns every chip on the table to the player while betting is open.</summary>
        public bool ClearBets()
        {
            if (!Run.Round.IsBettingOpen || _bets.Count == 0) return false;
            long total = _bets.Total;
            _bets.Clear();
            Run.Ledger.Credit(total, TransactionKind.WagerRefunded, "Bets cleared");
            Run.SetMoneyAtRisk(0);
            BetsChanged?.Invoke();
            return true;
        }

        public bool TryCloseBetting()
        {
            return Run.Round.TryCloseBettingEarly();
        }

        /// <summary>
        /// Chooses the winning pocket from the current wheel with the run's seeded RNG and starts the
        /// spin phase. Returns the pocket index the animation must land on.
        /// </summary>
        public int StartSpin()
        {
            if (Run.Round.Phase != RoundPhase.BettingClosed) throw new InvalidOperationException("Spins start only after betting closes (phase is " + Run.Round.Phase + ").");
            _pendingPocketIndex = OutcomeSelector.SelectPocketIndex(Wheel, Run.Random);
            Run.Round.NotifySpinStarted();
            return _pendingPocketIndex;
        }

        /// <summary>Called once the ball has visibly settled. Settles all bets and reveals the result.</summary>
        public RoundResult FinishSpin()
        {
            if (!IsSpinPending || Run.Round.Phase != RoundPhase.Spinning) throw new InvalidOperationException("No spin is in progress.");

            int index = _pendingPocketIndex;
            _pendingPocketIndex = -1;
            Pocket pocket = Wheel.Pockets[index];
            int round = Run.CurrentRoundNumber;
            Run.Round.NotifySpinFinished();

            var outcomes = new List<BetOutcome>();
            foreach (KeyValuePair<BetSelection, long> entry in _bets.Entries)
            {
                decimal ratio = PayoutRatio(entry.Key);
                bool won = entry.Key.Covers(pocket, HighestNumber);
                long profit = won ? Payouts.Profit(entry.Value, ratio) : 0;
                outcomes.Add(new BetOutcome(entry.Key, entry.Value, won, profit, ratio));
            }
            _bets.Clear();
            Run.SetMoneyAtRisk(0);

            foreach (BetOutcome outcome in outcomes)
            {
                string label = outcome.Selection.DisplayName + " on " + pocket.Number;
                if (outcome.Won)
                {
                    Run.Ledger.Credit(outcome.Stake, TransactionKind.WagerReturned, label);
                    if (outcome.Profit > 0) Run.Ledger.Credit(outcome.Profit, TransactionKind.Winnings, label);
                }
                else
                {
                    Run.Ledger.RecordWagerLost(outcome.Stake, label);
                }
            }

            var result = new RoundResult(round, index, pocket, outcomes);
            LastResult = result;
            _history.Insert(0, pocket.Number);
            BetsChanged?.Invoke();
            RoundResolved?.Invoke(result);

            Run.Round.NotifyResolutionFinished();
            return result;
        }

        /// <summary>Replaces the wheel between rounds (Phase 3 transformations). Rejected while chips are down or a spin is pending.</summary>
        public bool TrySetWheel(WheelConfiguration wheel)
        {
            if (wheel == null || !wheel.IsValid) return false;
            if (IsSpinPending || _bets.Count > 0) return false;
            Wheel = wheel;
            WheelChanged?.Invoke(wheel);
            return true;
        }
    }
}

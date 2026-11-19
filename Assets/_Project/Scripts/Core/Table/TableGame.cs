using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Table
{
    /// <summary>
    /// The roulette table's rules for one run: placing, moving and removing wagers, the wheel and
    /// layout in use, physical table manipulation, choosing the result when betting closes and
    /// settling bets once the ball has visibly landed. Money always moves through the run's
    /// <see cref="Ledger"/>; phases always move through the run's <see cref="RoundFlow"/>; rule
    /// effects arrive as <see cref="TableModifiers"/> from the rule engine.
    ///
    /// Spin protocol (driven by the Unity table controller or the headless simulator):
    ///  1. Betting closes (timer or <see cref="TryCloseBetting"/>).
    ///  2. <see cref="StartSpin"/> picks every ball's pocket (and the ring segment) and returns a <see cref="SpinPlan"/>.
    ///  3. The animation plays; the result is not revealed (<see cref="LastResult"/> is unchanged).
    ///  4. <see cref="FinishSpin"/> settles every bet and publishes the <see cref="RoundResult"/>.
    /// </summary>
    public sealed class TableGame
    {
        public const int HistoryLength = 50;
        public const int SurvivorWindow = 10;
        public const int NeighborRadius = 2;
        public const int MysteryCount = 4;
        public const long JackpotMaximumStake = 100;

        private readonly BetBook _bets = new BetBook();
        private readonly List<DisplacedChips> _displaced = new List<DisplacedChips>();
        private readonly List<int> _history = new List<int>();
        private SpinPlan _pending;

        public Run Run { get; }
        public BettingLayout Layout { get; private set; }
        public PayoutPolicy Payouts { get; }
        public WheelConfiguration Wheel { get; private set; }
        public TableModifiers Modifiers { get; private set; }

        public BetBook Bets => _bets;

        /// <summary>Chips knocked onto the rail this round. Recall them or lose them at the spin.</summary>
        public IReadOnlyList<DisplacedChips> Displaced => _displaced;

        public long DisplacedTotal
        {
            get { long t = 0; foreach (DisplacedChips d in _displaced) t += d.Amount; return t; }
        }

        /// <summary>The most recent settled round, or null before the first spin.</summary>
        public RoundResult LastResult { get; private set; }

        /// <summary>Winning numbers (first ball), most recent first.</summary>
        public IReadOnlyList<int> History => _history;

        public bool IsSpinPending => _pending != null;
        public SpinPlan PendingSpin => _pending;

        public int HighestNumber => Layout.HighestNumber;

        public event Action BetsChanged;
        public event Action<RoundResult> RoundResolved;
        /// <summary>Raised after the wheel changes; carries before/after and the pocket diff for the animation.</summary>
        public event Action<WheelChange> WheelChanged;
        public event Action<ShakeReport> TableShaken;
        public event Action<TableModifiers> ModifiersChanged;
        /// <summary>Raised when bets are refunded because the wheel or the rules changed under them.</summary>
        public event Action<BetSelection, long> BetRefunded;

        public TableGame(Run run, WheelConfiguration wheel, PayoutPolicy payouts = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            wheel.ThrowIfInvalid();
            Run = run;
            Wheel = wheel;
            Payouts = payouts ?? PayoutPolicy.Standard;
            Layout = new BettingLayout(Math.Max(1, wheel.HighestNumber));
            Modifiers = TableModifiers.FromConfig(run.Config);
            Run.EffectiveMinimumBet = Modifiers.MinimumBet;
        }

        // ------------------------------------------------------------------ rounds and odds

        /// <summary>Starts the next round (announcement, then betting).</summary>
        public bool TryBeginRound()
        {
            return !IsSpinPending && Run.TryBeginRound();
        }

        /// <summary>True-odds profit ratio ("to one") for this selection on the current wheel and ball count, before rule modifiers.</summary>
        public decimal PayoutRatio(BetSelection selection)
        {
            return Payouts.PayoutRatio(Wheel, selection, HighestNumber, Modifiers.BallCount);
        }

        /// <summary>Exact chance this selection wins on the current wheel with the current number of balls.</summary>
        public double WinProbability(BetSelection selection)
        {
            return PayoutPolicy.WinProbability(Wheel, selection, HighestNumber, Modifiers.BallCount);
        }

        /// <summary>Payout modifiers that could change this bet's profit, optionally including hidden ones.</summary>
        public List<PayoutModifier> ModifiersFor(BetSelection selection, bool includeHidden)
        {
            var list = new List<PayoutModifier>();
            foreach (PayoutModifier m in Modifiers.PayoutModifiers)
            {
                if (m.Hidden && !includeHidden) continue;
                foreach (Pocket p in Wheel.Pockets)
                {
                    if (p.EffectiveWeight > 0 && selection.Covers(p, HighestNumber) && m.AppliesTo(selection, p))
                    {
                        list.Add(m);
                        break;
                    }
                }
            }
            return list;
        }

        // ------------------------------------------------------------------ special bets

        /// <summary>
        /// Builds a special bet from the current table state (wheel order, history), or null when it
        /// is not available. <paramref name="anchor"/> is the chosen number for neighbour bets and the
        /// colour/parity group for combinations.
        /// </summary>
        public BetSelection CreateSpecialBet(BetType type, int anchor, IRandomSource random = null)
        {
            switch (type)
            {
                case BetType.Neighbor:
                {
                    if (!Wheel.Contains(anchor)) return null;
                    var numbers = new HashSet<int>();
                    int n = Wheel.PocketCount;
                    foreach (int index in Wheel.IndicesOfNumber(anchor))
                    {
                        for (int d = -NeighborRadius; d <= NeighborRadius; d++)
                        {
                            numbers.Add(Wheel.Pockets[((index + d) % n + n) % n].Number);
                        }
                    }
                    return BetSelection.Special(BetType.Neighbor, anchor, ToArray(numbers));
                }
                case BetType.Sequence:
                {
                    if (_history.Count == 0) return null;
                    int last = _history[0];
                    var numbers = new List<int>();
                    if (last - 1 >= 1 && Wheel.Contains(last - 1)) numbers.Add(last - 1);
                    if (Wheel.Contains(last + 1)) numbers.Add(last + 1);
                    return numbers.Count == 0 ? null : BetSelection.Special(BetType.Sequence, last, numbers.ToArray());
                }
                case BetType.Repeat:
                {
                    if (_history.Count == 0 || !Wheel.Contains(_history[0])) return null;
                    return BetSelection.Special(BetType.Repeat, _history[0], _history[0]);
                }
                case BetType.Survivor:
                {
                    if (_history.Count < 3) return null;
                    var recent = new HashSet<int>();
                    for (int i = 0; i < Math.Min(SurvivorWindow, _history.Count); i++) recent.Add(_history[i]);
                    var numbers = new HashSet<int>();
                    foreach (Pocket p in Wheel.Pockets) if (!p.IsZero && !recent.Contains(p.Number)) numbers.Add(p.Number);
                    return numbers.Count == 0 ? null : BetSelection.Special(BetType.Survivor, -1, ToArray(numbers));
                }
                case BetType.Mystery:
                {
                    if (random == null) random = Run.Random;
                    var pool = new List<int>();
                    foreach (Pocket p in Wheel.Pockets) if (!p.IsZero && !pool.Contains(p.Number)) pool.Add(p.Number);
                    if (pool.Count < MysteryCount + 1) return null;
                    var picked = new List<int>();
                    while (picked.Count < MysteryCount)
                    {
                        int i = random.NextInt(pool.Count);
                        picked.Add(pool[i]);
                        pool.RemoveAt(i);
                    }
                    return BetSelection.Special(BetType.Mystery, -1, picked.ToArray());
                }
                case BetType.Combination:
                    return anchor >= 0 && anchor <= 3 ? BetSelection.Combination(anchor) : null;
                case BetType.Jackpot:
                    return Wheel.HasSpecial(SpecialPocketType.Jackpot) ? BetSelection.Jackpot() : null;
                default:
                    return null;
            }
        }

        private static int[] ToArray(HashSet<int> set)
        {
            var array = new int[set.Count];
            set.CopyTo(array);
            return array;
        }

        /// <summary>Whether a selection is a legal bet right now: on the layout, or a special bet the rules allow.</summary>
        public bool IsLegal(BetSelection selection)
        {
            if (selection == null) return false;
            if (selection.IsSpecial) return Modifiers.SpecialBetsAllowed;
            return Layout.IsLegal(selection);
        }

        // ------------------------------------------------------------------ placing chips

        /// <summary>Checks a placement without changing anything.</summary>
        public PlacementResult CanPlace(BetSelection selection, long amount)
        {
            if (amount <= 0) return PlacementResult.InvalidAmount;
            if (!Run.Round.IsBettingOpen || Run.IsOver) return PlacementResult.BettingClosed;
            if (!IsLegal(selection)) return PlacementResult.IllegalBet;
            if (Modifiers.IsClosed(selection)) return PlacementResult.SectionClosed;
            if (PayoutPolicy.WinningWeight(Wheel, selection, HighestNumber) <= 0) return PlacementResult.CannotWin;

            long newTotal = _bets.AmountOn(selection) + amount;
            if (newTotal < Modifiers.MinimumBet) return PlacementResult.BelowMinimum;
            long maximum = selection.Type == BetType.Jackpot ? Math.Min(Modifiers.MaximumBet, JackpotMaximumStake) : Modifiers.MaximumBet;
            if (newTotal > maximum) return PlacementResult.AboveMaximum;
            if (!Run.Ledger.CanAfford(amount)) return PlacementResult.InsufficientFunds;
            return PlacementResult.Accepted;
        }

        /// <summary>Adds chips to a spot, taking the money from the balance.</summary>
        public PlacementResult PlaceBet(BetSelection selection, long amount, ChipKind kind = ChipKind.Standard)
        {
            PlacementResult check = CanPlace(selection, amount);
            if (check != PlacementResult.Accepted) return check;

            // Mark the money as at risk before it leaves the balance so the bankruptcy check
            // never sees an empty balance with nothing on the table.
            Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal + amount);
            if (!Run.Ledger.TryDebit(amount, TransactionKind.WagerPlaced, selection.DisplayName))
            {
                Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal);
                return PlacementResult.InsufficientFunds;
            }
            Wager wager = _bets.Add(selection, amount, kind);
            if (kind == ChipKind.Protected) wager.Locked = true;
            BetsChanged?.Invoke();
            return PlacementResult.Accepted;
        }

        /// <summary>Takes chips back off a spot while betting is open. Removing below the minimum clears the spot.</summary>
        public PlacementResult RemoveBet(BetSelection selection, long amount)
        {
            if (!Run.Round.IsBettingOpen) return PlacementResult.BettingClosed;
            Wager wager = _bets.Get(selection);
            if (wager == null || amount <= 0) return PlacementResult.NothingToRemove;
            if (Modifiers.NoTakeBacks || !ChipProperties.Of(wager.Kind).CanBeRemoved) return PlacementResult.ChipsStuck;

            long current = wager.Amount;
            long remaining = current - Math.Min(current, amount);
            long toRemove = remaining > 0 && remaining < Modifiers.MinimumBet ? current : Math.Min(current, amount);
            long removed = _bets.Remove(selection, toRemove);
            Run.Ledger.Credit(removed, TransactionKind.WagerRefunded, selection.DisplayName);
            Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal);
            BetsChanged?.Invoke();
            return PlacementResult.Accepted;
        }

        /// <summary>Picks chips up from one spot and puts them on another in one move (no money changes hands).</summary>
        public PlacementResult MoveBet(BetSelection from, BetSelection to, long amount)
        {
            if (!Run.Round.IsBettingOpen) return PlacementResult.BettingClosed;
            Wager wager = _bets.Get(from);
            if (wager == null || amount <= 0) return PlacementResult.NothingToRemove;
            if (from.Equals(to)) return PlacementResult.Accepted;
            if (Modifiers.NoTakeBacks || !ChipProperties.Of(wager.Kind).CanBeRemoved) return PlacementResult.ChipsStuck;

            long moving = Math.Min(amount, wager.Amount);
            long left = wager.Amount - moving;
            if (left > 0 && left < Modifiers.MinimumBet) moving = wager.Amount;

            if (!IsLegal(to)) return PlacementResult.IllegalBet;
            if (Modifiers.IsClosed(to)) return PlacementResult.SectionClosed;
            if (PayoutPolicy.WinningWeight(Wheel, to, HighestNumber) <= 0) return PlacementResult.CannotWin;
            long newTotal = _bets.AmountOn(to) + moving;
            if (newTotal < Modifiers.MinimumBet) return PlacementResult.BelowMinimum;
            if (newTotal > Modifiers.MaximumBet) return PlacementResult.AboveMaximum;

            ChipKind kind = wager.Kind;
            _bets.Remove(from, moving);
            _bets.Add(to, moving, kind);
            BetsChanged?.Invoke();
            return PlacementResult.Accepted;
        }

        /// <summary>Returns every removable chip on the table to the player while betting is open.</summary>
        public bool ClearBets()
        {
            if (!Run.Round.IsBettingOpen || _bets.Count == 0 || Modifiers.NoTakeBacks) return false;
            long total = 0;
            foreach (Wager wager in _bets.Wagers)
            {
                if (!ChipProperties.Of(wager.Kind).CanBeRemoved) continue;
                total += _bets.Remove(wager.Selection, wager.Amount);
            }
            if (total == 0) return false;
            Run.Ledger.Credit(total, TransactionKind.WagerRefunded, "Bets cleared");
            Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal);
            BetsChanged?.Invoke();
            return true;
        }

        /// <summary>Locks (or unlocks) every stack so table manipulation cannot move it (Chip Anchor).</summary>
        public void LockAll(bool locked)
        {
            foreach (Wager wager in _bets.Wagers) wager.Locked = locked || wager.Kind == ChipKind.Protected;
            BetsChanged?.Invoke();
        }

        public void SetLocked(BetSelection selection, bool locked)
        {
            _bets.SetLocked(selection, locked);
            BetsChanged?.Invoke();
        }

        /// <summary>Insures a bet: <paramref name="percent"/> of the stake comes back if it loses.</summary>
        public bool Insure(BetSelection selection, int percent)
        {
            Wager wager = _bets.Get(selection);
            if (wager == null || IsSpinPending) return false;
            _bets.SetInsured(selection, Math.Max(wager.InsuredPercent, percent));
            BetsChanged?.Invoke();
            return true;
        }

        /// <summary>The wager with the largest stake, or null.</summary>
        public Wager LargestWager()
        {
            Wager best = null;
            foreach (Wager w in _bets.Wagers) if (best == null || w.Amount > best.Amount) best = w;
            return best;
        }

        /// <summary>Double Down: doubles a stake after betting closes but before the spin. Ignores the table maximum, not the balance.</summary>
        public bool TryDoubleDown(BetSelection selection)
        {
            if (IsSpinPending) return false;
            RoundPhase phase = Run.Round.Phase;
            if (phase != RoundPhase.Betting && phase != RoundPhase.BettingClosed) return false;
            Wager wager = _bets.Get(selection);
            if (wager == null) return false;
            long extra = wager.Amount;
            if (!Run.Ledger.CanAfford(extra)) return false;
            Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal + extra);
            if (!Run.Ledger.TryDebit(extra, TransactionKind.WagerPlaced, "Double down: " + selection.DisplayName)) return false;
            _bets.Add(selection, extra, wager.Kind);
            BetsChanged?.Invoke();
            return true;
        }

        public bool TryCloseBetting()
        {
            return Run.Round.TryCloseBettingEarly();
        }

        // ------------------------------------------------------------------ table manipulation

        /// <summary>
        /// The Game Master shakes (or tilts) the table. Each unlocked stack may slide to a neighbouring
        /// spot or fall onto the rail, with a chance set by the shake strength and the chip's stability.
        /// Fragile chips that fall shatter. Deterministic for a given RNG state.
        /// </summary>
        public ShakeReport Shake(int strengthPercent, IRandomSource random, int tiltX = 0, int tiltY = 0)
        {
            if (random == null) random = Run.Random;
            strengthPercent = Math.Max(0, Math.Min(100, strengthPercent));
            var moves = new List<ChipDisplacement>();
            if (IsSpinPending) return new ShakeReport(moves, strengthPercent, tiltX != 0 || tiltY != 0);

            long shattered = 0;
            foreach (Wager wager in _bets.Wagers)
            {
                if (wager.IsImmovable) continue;
                ChipProperties props = ChipProperties.Of(wager.Kind);
                double chance = strengthPercent / 100.0 * (1.0 - props.Stability);
                if (random.NextDouble() >= chance) continue;

                BetSelection destination = PickNeighbour(wager.Selection, random, tiltX, tiltY);
                bool fallsOff = destination == null || random.NextDouble() < 0.35;
                long amount = wager.Amount;
                ChipKind kind = wager.Kind;
                _bets.Remove(wager.Selection, amount);
                if (fallsOff)
                {
                    bool breaks = kind == ChipKind.Fragile;
                    if (breaks) shattered += amount;
                    else _displaced.Add(new DisplacedChips(amount, kind, wager.Selection));
                    moves.Add(new ChipDisplacement(wager.Selection, null, amount, kind, breaks));
                }
                else
                {
                    _bets.Add(destination, amount, kind);
                    moves.Add(new ChipDisplacement(wager.Selection, destination, amount, kind, false));
                }
            }

            if (shattered > 0) Run.Ledger.RecordWagerLost(shattered, "Fragile chips shattered");
            Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal);
            var report = new ShakeReport(moves, strengthPercent, tiltX != 0 || tiltY != 0);
            if (moves.Count > 0) BetsChanged?.Invoke();
            TableShaken?.Invoke(report);
            return report;
        }

        private BetSelection PickNeighbour(BetSelection from, IRandomSource random, int tiltX, int tiltY)
        {
            BetSpot origin;
            if (!Layout.TryGetSpot(from, out origin)) return null;
            var candidates = new List<BetSelection>();
            foreach (BetSpot spot in Layout.Spots)
            {
                if (spot.Selection.Equals(from) || spot.IsBorderSpot) continue;
                float dx = spot.CenterX - origin.CenterX;
                float dy = spot.CenterY - origin.CenterY;
                if (dx * dx + dy * dy > 2.6f) continue;
                if (tiltX != 0 && Math.Sign(dx) != Math.Sign(tiltX)) continue;
                if (tiltY != 0 && Math.Sign(dy) != Math.Sign(tiltY)) continue;
                if (Modifiers.IsClosed(spot.Selection)) continue;
                candidates.Add(spot.Selection);
            }
            return candidates.Count == 0 ? null : candidates[random.NextInt(candidates.Count)];
        }

        /// <summary>Chip Recall: returns every displaced chip to the player's balance. Returns the amount recovered.</summary>
        public long RecallDisplaced()
        {
            long total = DisplacedTotal;
            if (total <= 0) return 0;
            _displaced.Clear();
            Run.Ledger.Credit(total, TransactionKind.WagerRefunded, "Chip recall");
            Run.SetMoneyAtRisk(_bets.Total);
            BetsChanged?.Invoke();
            return total;
        }

        // ------------------------------------------------------------------ wheel and rules

        /// <summary>
        /// Puts a validated wheel change into effect. Allowed between rounds and during betting (a live
        /// transformation), never while a spin is pending. Bets that are no longer on the layout or can
        /// no longer win are refunded.
        /// </summary>
        public bool ApplyWheelChange(WheelChange change)
        {
            if (change == null || !change.Succeeded || IsSpinPending) return false;
            if (Run.Round.Phase == RoundPhase.BettingClosed || Run.Round.Phase == RoundPhase.Spinning || Run.Round.Phase == RoundPhase.Resolving) return false;
            Wheel = change.After;
            if (Layout.HighestNumber != Math.Max(1, Wheel.HighestNumber)) Layout = new BettingLayout(Math.Max(1, Wheel.HighestNumber));
            RefundInvalidBets();
            WheelChanged?.Invoke(change);
            return true;
        }

        /// <summary>Replaces the wheel outright (new run, debug menu). Same rules as <see cref="ApplyWheelChange"/>.</summary>
        public bool TrySetWheel(WheelConfiguration wheel)
        {
            if (wheel == null || !wheel.IsValid) return false;
            return ApplyWheelChange(new WheelChange(Wheel, wheel, WheelChangeKind.Replace, "The wheel is replaced."));
        }

        /// <summary>Applies the rule engine's modifiers for this round. Refunds bets the new rules forbid.</summary>
        public bool SetModifiers(TableModifiers modifiers)
        {
            if (modifiers == null || modifiers.Validate().Count > 0) return false;
            Modifiers = modifiers;
            Run.EffectiveMinimumBet = modifiers.MinimumBet;
            RefundInvalidBets();
            ModifiersChanged?.Invoke(modifiers);
            return true;
        }

        private void RefundInvalidBets()
        {
            bool changed = false;
            foreach (Wager wager in _bets.Wagers)
            {
                BetSelection s = wager.Selection;
                bool valid = IsLegal(s) && !Modifiers.IsClosed(s) && PayoutPolicy.WinningWeight(Wheel, s, HighestNumber) > 0;
                if (valid) continue;
                long amount = _bets.Remove(s, wager.Amount);
                if (amount <= 0) continue;
                Run.Ledger.Credit(amount, TransactionKind.WagerRefunded, "Refund: " + s.DisplayName + " is no longer on the table");
                BetRefunded?.Invoke(s, amount);
                changed = true;
            }
            if (changed)
            {
                Run.SetMoneyAtRisk(_bets.Total + DisplacedTotal);
                BetsChanged?.Invoke();
            }
        }

        // ------------------------------------------------------------------ spinning

        /// <summary>
        /// Chooses every ball's pocket (and the ring segment) from the current wheel with the run's
        /// seeded RNG and starts the spin phase. Returns what the animation must show.
        /// </summary>
        public SpinPlan StartSpin()
        {
            if (Run.Round.Phase != RoundPhase.BettingClosed) throw new InvalidOperationException("Spins start only after betting closes (phase is " + Run.Round.Phase + ").");
            var indices = new int[Modifiers.BallCount];
            for (int b = 0; b < indices.Length; b++) indices[b] = OutcomeSelector.SelectPocketIndex(Wheel, Run.Random);
            int ring = Wheel.Ring != null ? OutcomeSelector.SelectRingSegment(Wheel.Ring, Run.Random) : -1;
            _pending = new SpinPlan(indices, ring, Modifiers.ReverseRotation, Modifiers.SpinSpeed);
            Run.Round.NotifySpinStarted();
            return _pending;
        }

        /// <summary>
        /// Called once the ball has visibly settled. Settles all bets and reveals the result. With
        /// <paramref name="completeResolution"/> false the round stays in Resolving so follow-up effects
        /// (special pockets, a double spin) can run; call <see cref="CompleteResolution"/> afterwards.
        /// </summary>
        public RoundResult FinishSpin(bool completeResolution = true)
        {
            if (!IsSpinPending || Run.Round.Phase != RoundPhase.Spinning) throw new InvalidOperationException("No spin is in progress.");

            SpinPlan plan = _pending;
            _pending = null;
            var pockets = new List<Pocket>(plan.PocketIndices.Count);
            foreach (int index in plan.PocketIndices) pockets.Add(Wheel.Pockets[index]);
            RingSegment segment = plan.RingSegmentIndex >= 0 ? Wheel.Ring.Segments[plan.RingSegmentIndex] : null;
            int round = Run.CurrentRoundNumber;
            Run.Round.NotifySpinFinished();

            var outcomes = new List<BetOutcome>();
            var loopholes = new List<string>();
            foreach (Wager wager in _bets.Wagers)
            {
                outcomes.Add(Settle(wager, pockets, segment, loopholes));
            }

            long forfeited = DisplacedTotal;
            _displaced.Clear();
            _bets.Clear();
            Run.SetMoneyAtRisk(0);

            foreach (BetOutcome outcome in outcomes)
            {
                string label = outcome.Selection.DisplayName + " on " + pockets[0].Number;
                if (outcome.Won)
                {
                    Run.Ledger.Credit(outcome.Stake, TransactionKind.WagerReturned, label);
                    if (outcome.Profit > 0) Run.Ledger.Credit(outcome.Profit, TransactionKind.Winnings, label);
                }
                else
                {
                    Run.Ledger.RecordWagerLost(outcome.Stake, label);
                    if (outcome.InsuranceRefund > 0) Run.Ledger.Credit(outcome.InsuranceRefund, TransactionKind.WagerRefunded, "Insurance: " + label);
                }
            }
            if (forfeited > 0) Run.Ledger.RecordWagerLost(forfeited, "Displaced chips kept by the house");

            var result = new RoundResult(round, plan.PocketIndices, pockets, outcomes, segment, plan.RingSegmentIndex, forfeited, loopholes);
            LastResult = result;
            foreach (Pocket p in pockets)
            {
                _history.Insert(0, p.Number);
            }
            while (_history.Count > HistoryLength) _history.RemoveAt(_history.Count - 1);
            BetsChanged?.Invoke();
            RoundResolved?.Invoke(result);
            if (completeResolution) CompleteResolution();
            return result;
        }

        /// <summary>Ends resolution once every follow-up effect (special pockets, events) has been applied.</summary>
        public void CompleteResolution()
        {
            if (Run.Round.Phase == RoundPhase.Resolving) Run.Round.NotifyResolutionFinished();
        }

        private BetOutcome Settle(Wager wager, List<Pocket> pockets, RingSegment segment, List<string> loopholes)
        {
            BetSelection selection = wager.Selection;
            decimal ratio = PayoutRatio(selection);
            Pocket winner = null;
            foreach (Pocket p in pockets)
            {
                if (selection.Covers(p, HighestNumber)) { winner = p; break; }
            }

            if (winner == null)
            {
                long refund = wager.InsuredPercent > 0 ? wager.Amount * wager.InsuredPercent / 100 : 0;
                var notes = new List<string>();
                if (refund > 0) notes.Add("Insured " + wager.InsuredPercent + "%");
                if (Modifiers.NeighborConsolationPercent > 0 && selection.Type == BetType.Straight && IsWheelNeighbour(selection.Numbers[0], pockets))
                {
                    long consolation = wager.Amount * Modifiers.NeighborConsolationPercent / 100;
                    if (consolation > refund)
                    {
                        refund = consolation;
                        notes.Add("Neighbourhood consolation " + Modifiers.NeighborConsolationPercent + "%");
                    }
                }
                return new BetOutcome(selection, wager.Amount, false, 0, ratio, refund, wager.Kind, notes);
            }

            var adjustments = new List<string>();
            decimal profit = wager.Amount * ratio;
            foreach (PayoutModifier modifier in Modifiers.PayoutModifiers)
            {
                if (!modifier.AppliesTo(selection, winner)) continue;
                if (modifier.Loophole != null && modifier.Loophole(selection, winner))
                {
                    if (!loopholes.Contains(modifier.SourceId)) loopholes.Add(modifier.SourceId);
                    adjustments.Add("Loophole: " + modifier.Description + " did not apply");
                    continue;
                }
                profit = profit * modifier.Percent / 100m;
                adjustments.Add(modifier.Description);
            }
            if (winner.PayoutPercent != 100)
            {
                profit = profit * winner.PayoutPercent / 100m;
                adjustments.Add("Pocket pays " + winner.PayoutPercent + "%");
            }
            if (segment != null && segment.MultiplierPercent != 100)
            {
                profit = profit * segment.MultiplierPercent / 100m;
                adjustments.Add("Outer ring " + segment.Label);
            }
            int bonus = ChipProperties.Of(wager.Kind).ProfitBonusPercent;
            if (bonus != 0)
            {
                profit = profit * (100 + bonus) / 100m;
                adjustments.Add(wager.Kind + " chips +" + bonus + "%");
            }
            long paid = profit <= 0 ? 0 : (long)decimal.Floor(profit);
            return new BetOutcome(selection, wager.Amount, true, paid, ratio, 0, wager.Kind, adjustments);
        }

        private bool IsWheelNeighbour(int number, List<Pocket> winners)
        {
            int n = Wheel.PocketCount;
            foreach (Pocket w in winners)
            {
                int index = Wheel.IndexOfId(w.Id);
                if (index < 0) continue;
                if (Wheel.Pockets[(index + 1) % n].Number == number || Wheel.Pockets[(index + n - 1) % n].Number == number) return true;
            }
            return false;
        }

        /// <summary>
        /// The Double Spin: after a resolved spin, the same bets are placed again (as far as the balance
        /// allows) and the round goes straight back to "betting closed" for a second spin. Returns the
        /// amount re-wagered, or -1 if a respin is not possible.
        /// </summary>
        public long PrepareRespin(IReadOnlyList<BetOutcome> previous)
        {
            if (Run.Round.Phase != RoundPhase.Resolving || IsSpinPending) return -1;
            long total = 0;
            foreach (BetOutcome outcome in previous)
            {
                BetSelection s = outcome.Selection;
                if (!IsLegal(s) || Modifiers.IsClosed(s) || PayoutPolicy.WinningWeight(Wheel, s, HighestNumber) <= 0) continue;
                long amount = outcome.Stake;
                if (!Run.Ledger.CanAfford(amount)) continue;
                Run.SetMoneyAtRisk(_bets.Total + amount);
                if (!Run.Ledger.TryDebit(amount, TransactionKind.WagerPlaced, "Double spin: " + s.DisplayName)) continue;
                _bets.Add(s, amount, outcome.Kind);
                total += amount;
            }
            Run.Round.RestartSpin();
            BetsChanged?.Invoke();
            return total;
        }

        // ------------------------------------------------------------------ persistence

        /// <summary>Restores history and wheel from a save (between rounds only).</summary>
        public void Restore(WheelConfiguration wheel, IEnumerable<int> history)
        {
            if (wheel == null || !wheel.IsValid) throw new ArgumentException("Saved wheel is invalid.", nameof(wheel));
            Wheel = wheel;
            Layout = new BettingLayout(Math.Max(1, wheel.HighestNumber));
            _history.Clear();
            if (history != null) _history.AddRange(history);
            _bets.Clear();
            _displaced.Clear();
        }
    }
}

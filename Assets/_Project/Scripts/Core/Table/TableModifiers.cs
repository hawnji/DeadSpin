using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Table
{
    /// <summary>
    /// A payout change from a rule ("Red Tax: red numbers pay 50% less"). Applied to a winning bet's
    /// profit when <see cref="AppliesTo"/> matches the bet and the winning pocket. A rule may leave a
    /// deliberate gap (<see cref="Loophole"/>): when the main condition matches but the loophole
    /// does too, the modifier is skipped and the table reports a loophole exploit.
    /// </summary>
    public sealed class PayoutModifier
    {
        public string SourceId { get; }
        public string Description { get; }
        /// <summary>Percentage of the profit that is paid (50 = half, 150 = +50%).</summary>
        public int Percent { get; }
        /// <summary>Hidden modifiers are not shown in bet tooltips until a House Audit reveals them.</summary>
        public bool Hidden { get; }
        public Func<BetSelection, Pocket, bool> AppliesTo { get; }
        public Func<BetSelection, Pocket, bool> Loophole { get; }
        /// <summary>Plain-language description of the gap, revealed by audits and the Records Room.</summary>
        public string LoopholeHint { get; }

        public PayoutModifier(string sourceId, string description, int percent, Func<BetSelection, Pocket, bool> appliesTo,
                              bool hidden = false, Func<BetSelection, Pocket, bool> loophole = null, string loopholeHint = null)
        {
            if (percent < 0) throw new ArgumentOutOfRangeException(nameof(percent));
            SourceId = sourceId ?? "modifier";
            Description = description ?? SourceId;
            Percent = percent;
            AppliesTo = appliesTo ?? throw new ArgumentNullException(nameof(appliesTo));
            Hidden = hidden;
            Loophole = loophole;
            LoopholeHint = loopholeHint;
        }
    }

    /// <summary>
    /// Everything about the table that rules can change for a round. Built by the rule engine from
    /// the active rules; the table only reads it. Defaults reproduce the plain game.
    /// </summary>
    public sealed class TableModifiers
    {
        public long MinimumBet = 10;
        public long MaximumBet = 10000;

        /// <summary>Balls released per spin (Double Trouble = 2).</summary>
        public int BallCount = 1;

        /// <summary>Wheel turns the other way (presentation; the result is unaffected).</summary>
        public bool ReverseRotation;

        /// <summary>Spin duration multiplier for the animation.</summary>
        public float SpinSpeed = 1f;

        /// <summary>Added to the configured betting time (may be negative; the table keeps at least 8 s).</summary>
        public float BettingSecondsDelta;

        public readonly HashSet<BetType> ClosedBetTypes = new HashSet<BetType>();

        /// <summary>Numbers whose sections are closed: no numbered bet may cover them.</summary>
        public readonly HashSet<int> ClosedNumbers = new HashSet<int>();

        /// <summary>Individual spots closed by key ("Dozen:0", "Red").</summary>
        public readonly HashSet<string> ClosedKeys = new HashSet<string>();

        public readonly List<PayoutModifier> PayoutModifiers = new List<PayoutModifier>();

        /// <summary>Chips cannot be removed or moved once placed.</summary>
        public bool NoTakeBacks;

        /// <summary>Special bets are only offered from stage 2 or by rule.</summary>
        public bool SpecialBetsAllowed = true;

        /// <summary>Lights out during betting (presentation).</summary>
        public bool Blackout;

        /// <summary>No announcements or countdown (presentation), used by The Silent Spin.</summary>
        public bool SilentSpin;

        /// <summary>The layout is drawn rotated 180 degrees (presentation).</summary>
        public bool LayoutRotated;

        /// <summary>Percent applied to ability costs (No Safety Net = 200).</summary>
        public int AbilityCostPercent = 100;

        /// <summary>Percent applied to insurance fees.</summary>
        public int InsuranceCostPercent = 100;

        /// <summary>Debt cannot increase this round (Debt Freeze).</summary>
        public bool DebtFrozen;

        /// <summary>Minimum total that must be wagered this round, or 0 (Mandatory Wager).</summary>
        public long MandatoryWager;

        /// <summary>Percent of a losing straight bet's stake refunded when its number sits next to the winning pocket on the wheel (Neighborhood Watch).</summary>
        public int NeighborConsolationPercent;

        public static TableModifiers FromConfig(GameState.RunConfig config)
        {
            return new TableModifiers { MinimumBet = config.MinimumBet, MaximumBet = config.MaximumBet };
        }

        public TableModifiers Clone()
        {
            var fresh = new TableModifiers
            {
                MinimumBet = MinimumBet, MaximumBet = MaximumBet, BallCount = BallCount, ReverseRotation = ReverseRotation,
                SpinSpeed = SpinSpeed, BettingSecondsDelta = BettingSecondsDelta, NoTakeBacks = NoTakeBacks,
                SpecialBetsAllowed = SpecialBetsAllowed, Blackout = Blackout, SilentSpin = SilentSpin, LayoutRotated = LayoutRotated,
                AbilityCostPercent = AbilityCostPercent, InsuranceCostPercent = InsuranceCostPercent, DebtFrozen = DebtFrozen,
                MandatoryWager = MandatoryWager, NeighborConsolationPercent = NeighborConsolationPercent
            };
            fresh.ClosedBetTypes.UnionWith(ClosedBetTypes);
            fresh.ClosedNumbers.UnionWith(ClosedNumbers);
            fresh.ClosedKeys.UnionWith(ClosedKeys);
            fresh.PayoutModifiers.AddRange(PayoutModifiers);
            return fresh;
        }

        /// <summary>Whether rules close this selection's spot.</summary>
        public bool IsClosed(BetSelection selection)
        {
            if (ClosedBetTypes.Contains(selection.Type)) return true;
            if (ClosedKeys.Contains(selection.Key)) return true;
            if (selection.IsNumbered && ClosedNumbers.Count > 0 && !selection.IsHidden)
            {
                foreach (int n in selection.Numbers) if (ClosedNumbers.Contains(n)) return true;
            }
            return false;
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (MinimumBet <= 0) errors.Add("Minimum bet must be positive.");
            if (MaximumBet < MinimumBet) errors.Add("Maximum bet is below the minimum bet.");
            if (BallCount < 1 || BallCount > PayoutPolicy.MaxBalls) errors.Add("Ball count must be 1-" + PayoutPolicy.MaxBalls + ".");
            if (SpinSpeed <= 0.2f || SpinSpeed > 3f) errors.Add("Spin speed out of range.");
            if (AbilityCostPercent < 0 || InsuranceCostPercent < 0) errors.Add("Cost multipliers cannot be negative.");
            if (MandatoryWager < 0) errors.Add("Mandatory wager cannot be negative.");
            return errors;
        }
    }
}

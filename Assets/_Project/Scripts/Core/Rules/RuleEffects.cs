using System;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Rules
{
    /// <summary>Changes the wheel through one of the validated <see cref="WheelTransforms"/>.</summary>
    public sealed class WheelEffect : RuleEffect
    {
        private readonly Func<WheelConfiguration, RuleContext, WheelChange> _transform;
        private readonly string _description;

        public WheelEffect(string description, Func<WheelConfiguration, RuleContext, WheelChange> transform)
        {
            _description = description;
            _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        public override WheelChange ApplyToWheel(WheelConfiguration wheel, RuleContext context)
        {
            return _transform(wheel, context);
        }

        public override string Describe(RuleContext context) { return _description; }
    }

    /// <summary>Raises (or lowers) the minimum bet by a percentage, never below the configured minimum.</summary>
    public sealed class MinimumBetEffect : RuleEffect
    {
        public int Percent { get; }
        public MinimumBetEffect(int percent) { Percent = percent; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            long raised = modifiers.MinimumBet * Percent / 100;
            modifiers.MinimumBet = Math.Max(context.ConfiguredMinimumBet, raised);
            if (modifiers.MaximumBet < modifiers.MinimumBet) modifiers.MaximumBet = modifiers.MinimumBet;
        }

        public override string Describe(RuleContext context) { return "Minimum bet x" + (Percent / 100m).ToString("0.##") + "."; }
    }

    /// <summary>Scales the maximum bet per spot.</summary>
    public sealed class MaximumBetEffect : RuleEffect
    {
        public int Percent { get; }
        public MaximumBetEffect(int percent) { Percent = percent; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            modifiers.MaximumBet = Math.Max(modifiers.MinimumBet, modifiers.MaximumBet * Percent / 100);
        }

        public override string Describe(RuleContext context) { return "Maximum bet x" + (Percent / 100m).ToString("0.##") + "."; }
    }

    /// <summary>Releases more than one ball. Payouts follow the true odds of "any ball hits".</summary>
    public sealed class BallCountEffect : RuleEffect
    {
        public int Balls { get; }
        public BallCountEffect(int balls) { Balls = balls; }
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context) { modifiers.BallCount = Math.Max(modifiers.BallCount, Balls); }
        public override string Describe(RuleContext context) { return Balls + " balls are released. A bet wins if any ball lands on it; payouts shrink to match."; }
    }

    /// <summary>Adds a payout modifier (tax, bonus) with an optional deliberate loophole.</summary>
    public sealed class PayoutEffect : RuleEffect
    {
        private readonly string _description;
        private readonly int _percent;
        private readonly Func<BetSelection, Pocket, bool> _appliesTo;
        private readonly Func<BetSelection, Pocket, bool> _loophole;
        private readonly string _loopholeHint;
        private readonly bool _hidden;

        public PayoutEffect(string description, int percent, Func<BetSelection, Pocket, bool> appliesTo,
                            Func<BetSelection, Pocket, bool> loophole = null, string loopholeHint = null, bool hidden = false)
        {
            _description = description;
            _percent = percent;
            _appliesTo = appliesTo;
            _loophole = loophole;
            _loopholeHint = loopholeHint;
            _hidden = hidden;
        }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            modifiers.PayoutModifiers.Add(new PayoutModifier(context.Instance.Id, _description, _percent, _appliesTo,
                _hidden || context.Instance.Definition.Hidden && !context.Instance.Revealed, _loophole, _loopholeHint));
        }

        public override string Describe(RuleContext context) { return _description; }
    }

    /// <summary>Closes whole bet types (e.g. "no straight bets").</summary>
    public sealed class CloseBetTypesEffect : RuleEffect
    {
        private readonly BetType[] _types;
        public CloseBetTypesEffect(params BetType[] types) { _types = types ?? new BetType[0]; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            foreach (BetType t in _types) modifiers.ClosedBetTypes.Add(t);
        }

        public override string Describe(RuleContext context) { return "Closed: " + string.Join(", ", _types) + "."; }
    }

    /// <summary>Closes one third ("dozen") of the layout: its box and every number in it. The instance parameter picks which (default: the player's favourite).</summary>
    public sealed class CloseDozenEffect : RuleEffect
    {
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            int group = Dozen(context);
            modifiers.ClosedKeys.Add(BetSelection.Outside(BetType.Dozen, group).Key);
            int third = Math.Max(1, context.HighestNumber / 3);
            int start = group * third + 1;
            int end = group == 2 ? context.HighestNumber : (group + 1) * third;
            for (int n = start; n <= end; n++) modifiers.ClosedNumbers.Add(n);
        }

        private static int Dozen(RuleContext context)
        {
            int p = context.Instance.Parameter;
            return p >= 0 && p <= 2 ? p : context.FavoriteDozen;
        }

        public override string Describe(RuleContext context) { return "The " + (Dozen(context) + 1) + Suffix(Dozen(context) + 1) + " third of the layout is closed."; }

        private static string Suffix(int n) { return n == 1 ? "st" : n == 2 ? "nd" : "rd"; }
    }

    /// <summary>Closes betting on the player's favourite number (every bet covering it).</summary>
    public sealed class CloseNumberEffect : RuleEffect
    {
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            int n = Target(context);
            if (n > 0) modifiers.ClosedNumbers.Add(n);
        }

        private static int Target(RuleContext context)
        {
            return context.Instance.Parameter > 0 ? context.Instance.Parameter : context.FavoriteNumber;
        }

        public override string Describe(RuleContext context)
        {
            int n = Target(context);
            return n > 0 ? "No bet may cover " + n + "." : "Your favourite number is off limits.";
        }
    }

    public sealed class BettingTimeEffect : RuleEffect
    {
        public float Seconds { get; }
        public BettingTimeEffect(float seconds) { Seconds = seconds; }
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context) { modifiers.BettingSecondsDelta += Seconds; }
        public override string Describe(RuleContext context) { return (Seconds < 0 ? "Betting time cut by " : "Betting time extended by ") + Math.Abs(Seconds).ToString("0") + " seconds."; }
    }

    public sealed class NoTakeBacksEffect : RuleEffect
    {
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context) { modifiers.NoTakeBacks = true; }
        public override string Describe(RuleContext context) { return "Chips cannot be taken back or moved once placed."; }
    }

    /// <summary>Lights, announcements and the layout's orientation.</summary>
    public sealed class PresentationEffect : RuleEffect
    {
        private readonly bool _blackout, _silent, _rotated;
        public PresentationEffect(bool blackout = false, bool silent = false, bool rotated = false) { _blackout = blackout; _silent = silent; _rotated = rotated; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            modifiers.Blackout |= _blackout;
            modifiers.SilentSpin |= _silent;
            modifiers.LayoutRotated |= _rotated;
        }

        public override string Describe(RuleContext context)
        {
            if (_blackout) return "The lights go out while you bet.";
            if (_silent) return "No announcements, no countdown.";
            return "The layout is turned around.";
        }
    }

    public sealed class SpinEffect : RuleEffect
    {
        private readonly bool _reverse;
        private readonly float _speed;
        public SpinEffect(bool reverse, float speed = 1f) { _reverse = reverse; _speed = speed; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            modifiers.ReverseRotation ^= _reverse;
            modifiers.SpinSpeed *= _speed;
        }

        public override string Describe(RuleContext context) { return _reverse ? "The wheel spins the other way." : "The wheel spins at a different speed."; }
    }

    public sealed class AbilityCostEffect : RuleEffect
    {
        private readonly int _abilityPercent, _insurancePercent;
        public AbilityCostEffect(int abilityPercent, int insurancePercent) { _abilityPercent = abilityPercent; _insurancePercent = insurancePercent; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            modifiers.AbilityCostPercent = modifiers.AbilityCostPercent * _abilityPercent / 100;
            modifiers.InsuranceCostPercent = modifiers.InsuranceCostPercent * _insurancePercent / 100;
        }

        public override string Describe(RuleContext context)
        {
            return _insurancePercent != 100 && _abilityPercent == 100 ? "Insurance costs x" + (_insurancePercent / 100m).ToString("0.##") + "." : "Abilities cost x" + (_abilityPercent / 100m).ToString("0.##") + ".";
        }
    }

    public sealed class SpecialBetsEffect : RuleEffect
    {
        private readonly bool _allowed;
        public SpecialBetsEffect(bool allowed) { _allowed = allowed; }
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context) { modifiers.SpecialBetsAllowed = _allowed; }
        public override string Describe(RuleContext context) { return _allowed ? "Special bets are open." : "Special bets are closed."; }
    }

    /// <summary>The player must wager at least this many minimum bets each round.</summary>
    public sealed class MandatoryWagerEffect : RuleEffect
    {
        private readonly int _minimumBets;
        public MandatoryWagerEffect(int minimumBets) { _minimumBets = minimumBets; }

        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            modifiers.MandatoryWager = Math.Max(modifiers.MandatoryWager, modifiers.MinimumBet * _minimumBets);
        }

        public override string Describe(RuleContext context) { return "You must wager at least " + _minimumBets + " minimum bets each round."; }
    }

    public sealed class NeighborConsolationEffect : RuleEffect
    {
        private readonly int _percent;
        public NeighborConsolationEffect(int percent) { _percent = percent; }
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context) { modifiers.NeighborConsolationPercent = Math.Max(modifiers.NeighborConsolationPercent, _percent); }
        public override string Describe(RuleContext context) { return "A losing straight bet next to the winning pocket gets " + _percent + "% back."; }
    }

    public sealed class DebtFreezeEffect : RuleEffect
    {
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context) { modifiers.DebtFrozen = true; }
        public override string Describe(RuleContext context) { return "Debt cannot increase this round."; }
    }

    /// <summary>Closes the bet type named by the instance parameter (The Inspection closes the player's favourite type).</summary>
    public sealed class CloseParameterBetTypeEffect : RuleEffect
    {
        public override void ApplyToTable(TableModifiers modifiers, RuleContext context)
        {
            int p = context.Instance.Parameter;
            if (p >= 0 && Enum.IsDefined(typeof(BetType), p)) modifiers.ClosedBetTypes.Add((BetType)p);
        }

        public override string Describe(RuleContext context)
        {
            int p = context.Instance.Parameter;
            return p >= 0 && Enum.IsDefined(typeof(BetType), p) ? ((BetType)p) + " bets are closed." : "Your favourite bet is closed.";
        }
    }
}

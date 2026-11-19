using System.Collections.Generic;
using DeadSpin.Core.Common;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Rules
{
    /// <summary>
    /// What a rule may look at while it applies its effects: the wheel it is changing, the active
    /// instance's parameters, the run's stage and the player's habits. Rules never reach into
    /// other systems directly; everything they change goes through their return values.
    /// </summary>
    public sealed class RuleContext
    {
        public RuleInstance Instance { get; }
        public int Stage { get; }
        public long Balance { get; }
        public long ConfiguredMinimumBet { get; }
        /// <summary>The player's most-bet number this run, or -1 (for rules that target habits).</summary>
        public int FavoriteNumber { get; }
        public IReadOnlyList<int> History { get; }
        /// <summary>Highest number on the (current effective) wheel, for section rules.</summary>
        public int HighestNumber { get; }
        /// <summary>The dozen (0-2) the player has bet on most this run, or 0.</summary>
        public int FavoriteDozen { get; }

        public RuleContext(RuleInstance instance, int stage, long balance, long configuredMinimumBet, int favoriteNumber, IReadOnlyList<int> history,
                           int highestNumber = 36, int favoriteDozen = 0)
        {
            HighestNumber = highestNumber;
            FavoriteDozen = favoriteDozen;
            Instance = instance;
            Stage = stage;
            Balance = balance;
            ConfiguredMinimumBet = configuredMinimumBet;
            FavoriteNumber = favoriteNumber;
            History = history ?? new int[0];
        }

        /// <summary>A deterministic random source for this instance (same answer every time the rule re-applies).</summary>
        public IRandomSource Random => new SeededRandom(Instance.Seed);
    }

    /// <summary>
    /// One piece of what a rule does. A rule is a list of effects; effects are small, reusable and
    /// independent, so new rules are mostly new combinations of existing effects. Wheel effects
    /// return a transformation of the wheel; table effects edit the round's <see cref="TableModifiers"/>.
    /// </summary>
    public abstract class RuleEffect
    {
        /// <summary>Changes the wheel, or returns null to leave it alone.</summary>
        public virtual WheelChange ApplyToWheel(WheelConfiguration wheel, RuleContext context) { return null; }

        /// <summary>Changes betting, payouts, abilities and presentation for the round.</summary>
        public virtual void ApplyToTable(TableModifiers modifiers, RuleContext context) { }

        /// <summary>Plain-language summary of this effect for the rules panel.</summary>
        public abstract string Describe(RuleContext context);
    }
}

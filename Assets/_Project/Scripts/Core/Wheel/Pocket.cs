using System;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// One physical pocket on the wheel. Several pockets may show the same number (clones); every
    /// pocket counts separately in the odds. Immutable: wheel changes produce new pockets.
    /// The pocket's physical position is its index in <see cref="WheelConfiguration.Pockets"/>.
    /// </summary>
    public sealed class Pocket
    {
        /// <summary>Unique id within a wheel configuration. Survives moves, so the view can animate a pocket from its old slot to its new one.</summary>
        public int Id { get; }

        /// <summary>The number printed on the pocket. 0 is the house number.</summary>
        public int Number { get; }

        public PocketColor Color { get; }

        /// <summary>
        /// Relative chance of the ball settling here. 1 is a normal pocket; 2 is twice as likely
        /// (a "magnetic" pocket). Integer weights keep probabilities exact rational numbers.
        /// </summary>
        public int Weight { get; }

        /// <summary>Inactive pockets are on the wheel but can never win (closed by a rule).</summary>
        public bool IsActive { get; }

        /// <summary>Extra effect when the ball lands here.</summary>
        public SpecialPocketType Special { get; }

        /// <summary>Percentage applied to winning bets' profit when the ball lands here. 100 is normal.</summary>
        public int PayoutPercent { get; }

        public Pocket(int id, int number, PocketColor color, int weight = 1, bool isActive = true,
                      SpecialPocketType special = SpecialPocketType.None, int payoutPercent = 100)
        {
            if (number < 0) throw new ArgumentOutOfRangeException(nameof(number), "Pocket numbers cannot be negative.");
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight), "Pocket weight cannot be negative.");
            if (payoutPercent < 0) throw new ArgumentOutOfRangeException(nameof(payoutPercent), "Payout percent cannot be negative.");
            Id = id;
            Number = number;
            Color = color;
            Weight = weight;
            IsActive = isActive;
            Special = special;
            PayoutPercent = payoutPercent;
        }

        /// <summary>Weight that actually takes part in the draw (0 when inactive).</summary>
        public int EffectiveWeight => IsActive ? Weight : 0;

        public bool IsZero => Number == 0;

        public Pocket WithId(int id) { return new Pocket(id, Number, Color, Weight, IsActive, Special, PayoutPercent); }
        public Pocket WithNumber(int number, PocketColor color) { return new Pocket(Id, number, color, Weight, IsActive, Special, PayoutPercent); }
        public Pocket WithWeight(int weight) { return new Pocket(Id, Number, Color, weight, IsActive, Special, PayoutPercent); }
        public Pocket WithActive(bool active) { return new Pocket(Id, Number, Color, Weight, active, Special, PayoutPercent); }
        public Pocket WithColor(PocketColor color) { return new Pocket(Id, Number, color, Weight, IsActive, Special, PayoutPercent); }
        public Pocket WithSpecial(SpecialPocketType special) { return new Pocket(Id, Number, Color, Weight, IsActive, special, PayoutPercent); }
        public Pocket WithPayoutPercent(int percent) { return new Pocket(Id, Number, Color, Weight, IsActive, Special, percent); }

        public override string ToString()
        {
            return Number + " " + Color
                + (Weight != 1 ? " x" + Weight : "")
                + (Special != SpecialPocketType.None ? " [" + Special + "]" : "")
                + (PayoutPercent != 100 ? " pays " + PayoutPercent + "%" : "")
                + (IsActive ? "" : " (closed)");
        }
    }
}

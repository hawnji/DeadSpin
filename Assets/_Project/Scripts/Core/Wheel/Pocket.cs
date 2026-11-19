using System;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// One physical pocket on the wheel. Several pockets may show the same number (clones); every
    /// pocket counts separately in the odds. Immutable: wheel changes produce new pockets.
    /// </summary>
    public sealed class Pocket
    {
        /// <summary>Unique id within a wheel configuration, stable across rebuilds of the same layout.</summary>
        public int Id { get; }

        /// <summary>The number printed on the pocket. 0 is the house number.</summary>
        public int Number { get; }

        public PocketColor Color { get; }

        /// <summary>
        /// Relative chance of the ball settling here. 1 is a normal pocket; 2 is twice as likely.
        /// Integer weights keep probabilities exact rational numbers.
        /// </summary>
        public int Weight { get; }

        /// <summary>Inactive pockets are on the wheel but can never win (closed by a rule).</summary>
        public bool IsActive { get; }

        public Pocket(int id, int number, PocketColor color, int weight = 1, bool isActive = true)
        {
            if (number < 0) throw new ArgumentOutOfRangeException(nameof(number), "Pocket numbers cannot be negative.");
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight), "Pocket weight cannot be negative.");
            Id = id;
            Number = number;
            Color = color;
            Weight = weight;
            IsActive = isActive;
        }

        /// <summary>Weight that actually takes part in the draw (0 when inactive).</summary>
        public int EffectiveWeight => IsActive ? Weight : 0;

        public bool IsZero => Number == 0;

        public Pocket WithWeight(int weight) { return new Pocket(Id, Number, Color, weight, IsActive); }
        public Pocket WithActive(bool active) { return new Pocket(Id, Number, Color, Weight, active); }
        public Pocket WithColor(PocketColor color) { return new Pocket(Id, Number, color, Weight, IsActive); }

        public override string ToString()
        {
            return Number + " " + Color + (Weight != 1 ? " x" + Weight : "") + (IsActive ? "" : " (closed)");
        }
    }
}

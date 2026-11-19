using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Wheel
{
    /// <summary>One segment of an outer bonus ring: its multiplier and relative size.</summary>
    public sealed class RingSegment
    {
        /// <summary>Percentage applied to the profit of every winning bet when the pointer stops here. 100 = x1.</summary>
        public int MultiplierPercent { get; }

        /// <summary>Relative chance of the pointer stopping on this segment (its visible width).</summary>
        public int Weight { get; }

        public RingSegment(int multiplierPercent, int weight = 1)
        {
            if (multiplierPercent < 0) throw new ArgumentOutOfRangeException(nameof(multiplierPercent));
            if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
            MultiplierPercent = multiplierPercent;
            Weight = weight;
        }

        public string Label => "x" + (MultiplierPercent / 100m).ToString("0.##");
    }

    /// <summary>
    /// An independently rotating outer ring around the pocket ring. When a ring is active the spin
    /// also draws one segment (weighted by segment width, visible to the player) whose multiplier
    /// scales winning profits. The draw is independent of the pocket draw and uses the same seeded RNG.
    /// </summary>
    public sealed class WheelRing
    {
        private readonly RingSegment[] _segments;

        public IReadOnlyList<RingSegment> Segments => _segments;
        public int TotalWeight { get; }

        public WheelRing(IEnumerable<RingSegment> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            _segments = new List<RingSegment>(segments).ToArray();
            if (_segments.Length < 2) throw new ArgumentException("A ring needs at least two segments.", nameof(segments));
            int total = 0;
            foreach (RingSegment s in _segments) total += s.Weight;
            TotalWeight = total;
        }

        /// <summary>Average multiplier, as a fraction (1.0 = neutral on average).</summary>
        public double ExpectedMultiplier
        {
            get
            {
                double sum = 0;
                foreach (RingSegment s in _segments) sum += s.MultiplierPercent / 100.0 * s.Weight;
                return sum / TotalWeight;
            }
        }

        /// <summary>The standard bonus ring: mostly x1, with x2, x3 and x0.5 and x0 segments. Averages x0.94, so the ring adds swing, not value.</summary>
        public static WheelRing Standard()
        {
            return new WheelRing(new[]
            {
                new RingSegment(100, 3), new RingSegment(200, 1), new RingSegment(100, 3), new RingSegment(50, 2),
                new RingSegment(0, 1), new RingSegment(100, 3), new RingSegment(300, 1), new RingSegment(100, 1),
                new RingSegment(50, 2), new RingSegment(0, 1)
            });
        }
    }
}

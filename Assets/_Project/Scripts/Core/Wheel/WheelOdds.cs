using System;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// Exact odds read straight from the active configuration. There is no other source of
    /// probability in the game: the outcome selector draws with these same weights, so what the
    /// player can inspect is what actually happens.
    /// </summary>
    public static class WheelOdds
    {
        /// <summary>Probability of one specific pocket.</summary>
        public static double PocketProbability(WheelConfiguration wheel, int pocketIndex)
        {
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            if (wheel.TotalWeight <= 0) return 0;
            return (double)wheel.Pockets[pocketIndex].EffectiveWeight / wheel.TotalWeight;
        }

        /// <summary>Combined weight of all pockets matching the predicate (e.g. every pocket showing 13).</summary>
        public static long Weight(WheelConfiguration wheel, Func<Pocket, bool> predicate)
        {
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            long sum = 0;
            foreach (Pocket pocket in wheel.Pockets)
            {
                if (pocket.EffectiveWeight > 0 && predicate(pocket)) sum += pocket.EffectiveWeight;
            }
            return sum;
        }

        public static double Probability(WheelConfiguration wheel, Func<Pocket, bool> predicate)
        {
            long total = wheel.TotalWeight;
            return total <= 0 ? 0 : (double)Weight(wheel, predicate) / total;
        }

        /// <summary>Probability that the displayed number comes up, counting every pocket that shows it.</summary>
        public static double NumberProbability(WheelConfiguration wheel, int number)
        {
            return Probability(wheel, p => p.Number == number);
        }
    }
}

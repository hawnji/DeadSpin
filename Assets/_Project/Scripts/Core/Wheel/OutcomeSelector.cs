using System;
using DeadSpin.Core.Common;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// Picks the winning pocket before any animation runs: one weighted draw over the active pockets
    /// with the seeded RNG. The ball is then animated to land on this pocket, so physics can never
    /// change a payout. There is no hidden adjustment: weights are exactly those in the configuration.
    /// </summary>
    public static class OutcomeSelector
    {
        /// <returns>Index of the winning pocket in <see cref="WheelConfiguration.Pockets"/>.</returns>
        public static int SelectPocketIndex(WheelConfiguration wheel, IRandomSource random)
        {
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            if (random == null) throw new ArgumentNullException(nameof(random));
            wheel.ThrowIfInvalid();
            if (wheel.TotalWeight > int.MaxValue) throw new InvalidOperationException("Total wheel weight is too large to draw from.");

            int roll = random.NextInt((int)wheel.TotalWeight);
            for (int i = 0; i < wheel.PocketCount; i++)
            {
                int weight = wheel.Pockets[i].EffectiveWeight;
                if (roll < weight) return i;
                roll -= weight;
            }
            // Unreachable when TotalWeight matches the pockets; fail loudly rather than bias the result.
            throw new InvalidOperationException("Weighted draw fell outside the wheel.");
        }
    }
}

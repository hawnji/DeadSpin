using System;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// Derives payouts from the true odds of the active wheel instead of a fixed table, so payouts
    /// stay honest when pockets are added, cloned or removed. A bet that wins with chance p pays
    /// (1 - houseEdge) / p - 1 to one, rounded to cents of ratio. With the default edge of 1/37 on a
    /// standard wheel this reproduces the classic payouts exactly (35, 17, 11, 8, 5, 2, 1 to 1).
    /// Arithmetic is exact rational math on integer weights.
    /// </summary>
    public sealed class PayoutPolicy
    {
        /// <summary>House edge as a fraction: EdgeNumerator / EdgeDenominator.</summary>
        public int EdgeNumerator { get; }
        public int EdgeDenominator { get; }

        public PayoutPolicy(int edgeNumerator = 1, int edgeDenominator = 37)
        {
            if (edgeDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(edgeDenominator));
            if (edgeNumerator < 0 || edgeNumerator >= edgeDenominator) throw new ArgumentOutOfRangeException(nameof(edgeNumerator), "House edge must be in [0, 1).");
            EdgeNumerator = edgeNumerator;
            EdgeDenominator = edgeDenominator;
        }

        public static PayoutPolicy Standard => new PayoutPolicy(1, 37);

        public double HouseEdge => (double)EdgeNumerator / EdgeDenominator;

        /// <summary>Total weight of the pockets that win this selection.</summary>
        public static long WinningWeight(WheelConfiguration wheel, BetSelection selection, int highestNumber)
        {
            return WheelOdds.Weight(wheel, p => selection.Covers(p, highestNumber));
        }

        /// <summary>Profit paid per dollar staked ("to one"). 0 when the bet cannot win or would pay nothing.</summary>
        public decimal PayoutRatio(WheelConfiguration wheel, BetSelection selection, int highestNumber)
        {
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            if (selection == null) throw new ArgumentNullException(nameof(selection));

            long win = WinningWeight(wheel, selection, highestNumber);
            long total = wheel.TotalWeight;
            if (win <= 0 || total <= 0) return 0m;

            // ((den - num) * total) / (den * win) - 1, one division, then rounded to cents of ratio.
            decimal numerator = (decimal)(EdgeDenominator - EdgeNumerator) * total;
            decimal denominator = (decimal)EdgeDenominator * win;
            decimal ratio = Math.Round(numerator / denominator - 1m, 2, MidpointRounding.AwayFromZero);
            return ratio < 0m ? 0m : ratio;
        }

        /// <summary>Profit for a winning stake, in whole dollars, rounded down in the house's favour.</summary>
        public long Profit(long stake, decimal ratio)
        {
            if (stake <= 0) throw new ArgumentOutOfRangeException(nameof(stake));
            if (ratio <= 0m) return 0;
            return (long)decimal.Floor(stake * ratio);
        }
    }
}

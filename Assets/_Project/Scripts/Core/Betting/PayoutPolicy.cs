using System;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// Derives payouts from the true odds of the active wheel instead of a fixed table, so payouts
    /// stay honest when pockets are added, cloned or removed, or when several balls are in play.
    /// A bet that wins with chance p pays (1 - houseEdge) / p - 1 to one, rounded to cents of ratio.
    /// With the default edge of 1/37 on a standard wheel and one ball this reproduces the classic
    /// payouts exactly (35, 17, 11, 8, 5, 2, 1 to 1). Arithmetic is exact rational math on integer
    /// weights: with b balls a bet wins when at least one ball lands on a covered pocket, so
    /// p = (T^b - (T - W)^b) / T^b where T is the total weight and W the covered weight.
    /// Rule modifiers (taxes, bonuses) are applied on top by the table, never hidden in here.
    /// </summary>
    public sealed class PayoutPolicy
    {
        public const int MaxBalls = 3;

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

        /// <summary>Exact win chance as numerator / denominator for <paramref name="balls"/> independent balls.</summary>
        public static void WinChance(long winWeight, long totalWeight, int balls, out decimal numerator, out decimal denominator)
        {
            if (balls < 1 || balls > MaxBalls) throw new ArgumentOutOfRangeException(nameof(balls));
            decimal t = totalWeight, lose = totalWeight - winWeight;
            decimal tb = 1m, lb = 1m;
            for (int i = 0; i < balls; i++) { tb *= t; lb *= lose; }
            numerator = tb - lb;
            denominator = tb;
        }

        public static double WinProbability(WheelConfiguration wheel, BetSelection selection, int highestNumber, int balls = 1)
        {
            long win = WinningWeight(wheel, selection, highestNumber);
            long total = wheel.TotalWeight;
            if (win <= 0 || total <= 0) return 0;
            decimal n, d;
            WinChance(win, total, balls, out n, out d);
            return (double)(n / d);
        }

        /// <summary>Profit paid per dollar staked ("to one"). 0 when the bet cannot win or would pay nothing.</summary>
        public decimal PayoutRatio(WheelConfiguration wheel, BetSelection selection, int highestNumber, int balls = 1)
        {
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            if (selection == null) throw new ArgumentNullException(nameof(selection));

            long win = WinningWeight(wheel, selection, highestNumber);
            long total = wheel.TotalWeight;
            if (win <= 0 || total <= 0) return 0m;

            decimal chanceNumerator, chanceDenominator;
            WinChance(win, total, balls, out chanceNumerator, out chanceDenominator);

            // ((den - num) * chanceDen) / (den * chanceNum) - 1, one division, then rounded to cents of ratio.
            decimal numerator = (EdgeDenominator - EdgeNumerator) * chanceDenominator;
            decimal denominator = EdgeDenominator * chanceNumerator;
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

using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Table
{
    /// <summary>The settled outcome of one bet.</summary>
    public readonly struct BetOutcome
    {
        public readonly BetSelection Selection;
        public readonly long Stake;
        public readonly bool Won;
        /// <summary>Profit on a win (stake not included); 0 on a loss.</summary>
        public readonly long Profit;
        public readonly decimal Ratio;

        public BetOutcome(BetSelection selection, long stake, bool won, long profit, decimal ratio)
        {
            Selection = selection;
            Stake = stake;
            Won = won;
            Profit = profit;
            Ratio = ratio;
        }

        /// <summary>Money returned to the player for this bet (stake plus profit on a win).</summary>
        public long Returned => Won ? Stake + Profit : 0;
    }

    /// <summary>Everything that happened in one resolved spin.</summary>
    public sealed class RoundResult
    {
        public int RoundNumber { get; }
        public int PocketIndex { get; }
        public Pocket Pocket { get; }
        public IReadOnlyList<BetOutcome> Bets { get; }
        public long TotalStaked { get; }
        public long TotalReturned { get; }

        /// <summary>Returned minus staked: positive means the player came out ahead this round.</summary>
        public long Net => TotalReturned - TotalStaked;

        public RoundResult(int roundNumber, int pocketIndex, Pocket pocket, IReadOnlyList<BetOutcome> bets)
        {
            RoundNumber = roundNumber;
            PocketIndex = pocketIndex;
            Pocket = pocket;
            Bets = bets;
            long staked = 0, returned = 0;
            foreach (BetOutcome bet in bets)
            {
                staked += bet.Stake;
                returned += bet.Returned;
            }
            TotalStaked = staked;
            TotalReturned = returned;
        }
    }
}

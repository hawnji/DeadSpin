using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Table
{
    /// <summary>The settled outcome of one bet.</summary>
    public sealed class BetOutcome
    {
        public BetSelection Selection { get; }
        public long Stake { get; }
        public bool Won { get; }
        /// <summary>Profit on a win (stake not included) after every modifier; 0 on a loss.</summary>
        public long Profit { get; }
        /// <summary>Base true-odds ratio before modifiers.</summary>
        public decimal Ratio { get; }
        /// <summary>Money refunded by insurance on a loss.</summary>
        public long InsuranceRefund { get; }
        public ChipKind Kind { get; }
        /// <summary>Descriptions of every payout change applied (taxes, ring multiplier, pocket percent).</summary>
        public IReadOnlyList<string> Adjustments { get; }

        public BetOutcome(BetSelection selection, long stake, bool won, long profit, decimal ratio, long insuranceRefund = 0,
                          ChipKind kind = ChipKind.Standard, IReadOnlyList<string> adjustments = null)
        {
            Selection = selection;
            Stake = stake;
            Won = won;
            Profit = profit;
            Ratio = ratio;
            InsuranceRefund = insuranceRefund;
            Kind = kind;
            Adjustments = adjustments ?? new string[0];
        }

        /// <summary>Money returned to the player for this bet (stake plus profit on a win, or the insurance refund).</summary>
        public long Returned => Won ? Stake + Profit : InsuranceRefund;
    }

    /// <summary>Everything that happened in one resolved spin.</summary>
    public sealed class RoundResult
    {
        public int RoundNumber { get; }
        /// <summary>Index of the first ball's pocket.</summary>
        public int PocketIndex { get; }
        /// <summary>First ball's pocket.</summary>
        public Pocket Pocket { get; }
        /// <summary>Every ball's pocket index (one per ball).</summary>
        public IReadOnlyList<int> PocketIndices { get; }
        public IReadOnlyList<Pocket> Pockets { get; }
        /// <summary>Outer-ring segment the pointer stopped on, or null without a ring.</summary>
        public RingSegment RingSegment { get; }
        public int RingSegmentIndex { get; }
        public IReadOnlyList<BetOutcome> Bets { get; }
        public long TotalStaked { get; }
        public long TotalReturned { get; }
        /// <summary>Displaced chips that were never recalled and went to the house.</summary>
        public long Forfeited { get; }
        /// <summary>Source ids of rule loopholes the player's winning bets slipped through.</summary>
        public IReadOnlyList<string> LoopholesExploited { get; }
        /// <summary>The bet wagered the most this round, for the Game Master's analysis.</summary>
        public BetSelection BiggestBet { get; }

        /// <summary>Returned minus staked: positive means the player came out ahead this round.</summary>
        public long Net => TotalReturned - TotalStaked - Forfeited;

        public RoundResult(int roundNumber, IReadOnlyList<int> pocketIndices, IReadOnlyList<Pocket> pockets, IReadOnlyList<BetOutcome> bets,
                           RingSegment ringSegment = null, int ringSegmentIndex = -1, long forfeited = 0, IReadOnlyList<string> loopholes = null)
        {
            RoundNumber = roundNumber;
            PocketIndices = pocketIndices;
            Pockets = pockets;
            PocketIndex = pocketIndices[0];
            Pocket = pockets[0];
            Bets = bets;
            RingSegment = ringSegment;
            RingSegmentIndex = ringSegmentIndex;
            Forfeited = forfeited;
            LoopholesExploited = loopholes ?? new string[0];
            long staked = 0, returned = 0, biggest = 0;
            foreach (BetOutcome bet in bets)
            {
                staked += bet.Stake;
                returned += bet.Returned;
                if (bet.Stake > biggest) { biggest = bet.Stake; BiggestBet = bet.Selection; }
            }
            TotalStaked = staked;
            TotalReturned = returned;
        }

        /// <summary>Single-ball convenience constructor.</summary>
        public RoundResult(int roundNumber, int pocketIndex, Pocket pocket, IReadOnlyList<BetOutcome> bets)
            : this(roundNumber, new[] { pocketIndex }, new[] { pocket }, bets)
        {
        }

        public bool AnyPocketIs(SpecialPocketType type)
        {
            foreach (Pocket p in Pockets) if (p.Special == type) return true;
            return false;
        }
    }

    /// <summary>What the spin animation must show: where each ball lands, where the ring stops, which way the wheel turns.</summary>
    public sealed class SpinPlan
    {
        public IReadOnlyList<int> PocketIndices { get; }
        public int RingSegmentIndex { get; }
        public bool Reverse { get; }
        public float Speed { get; }

        public SpinPlan(IReadOnlyList<int> pocketIndices, int ringSegmentIndex, bool reverse, float speed)
        {
            PocketIndices = pocketIndices;
            RingSegmentIndex = ringSegmentIndex;
            Reverse = reverse;
            Speed = speed;
        }
    }

    /// <summary>One chip stack moved by table manipulation.</summary>
    public readonly struct ChipDisplacement
    {
        public readonly BetSelection From;
        /// <summary>The spot the chips slid onto, or null if they fell off the layout onto the rail.</summary>
        public readonly BetSelection To;
        public readonly long Amount;
        public readonly ChipKind Kind;
        /// <summary>Fragile chips that fell off shattered and are lost.</summary>
        public readonly bool Shattered;

        public ChipDisplacement(BetSelection from, BetSelection to, long amount, ChipKind kind, bool shattered)
        {
            From = from;
            To = to;
            Amount = amount;
            Kind = kind;
            Shattered = shattered;
        }
    }

    /// <summary>The result of a shake or tilt.</summary>
    public sealed class ShakeReport
    {
        public IReadOnlyList<ChipDisplacement> Displacements { get; }
        public int StrengthPercent { get; }
        public bool IsTilt { get; }
        public long AmountMoved { get; }

        public ShakeReport(IReadOnlyList<ChipDisplacement> displacements, int strengthPercent, bool isTilt)
        {
            Displacements = displacements;
            StrengthPercent = strengthPercent;
            IsTilt = isTilt;
            long total = 0;
            foreach (ChipDisplacement d in displacements) total += d.Amount;
            AmountMoved = total;
        }
    }

    /// <summary>Chips knocked off the layout and lying on the rail. Recall them before the spin or the house keeps them.</summary>
    public sealed class DisplacedChips
    {
        public long Amount { get; }
        public ChipKind Kind { get; }
        public BetSelection From { get; }

        public DisplacedChips(long amount, ChipKind kind, BetSelection from)
        {
            Amount = amount;
            Kind = kind;
            From = from;
        }
    }
}

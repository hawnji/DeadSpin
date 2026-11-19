namespace DeadSpin.Core.Betting
{
    /// <summary>Every bet the table understands: the thirteen traditional bets and the seven special bets.</summary>
    public enum BetType
    {
        Straight,
        Split,
        Street,
        Corner,
        SixLine,
        Red,
        Black,
        Odd,
        Even,
        Low,
        High,
        Dozen,
        Column,

        // Special bets. Numbered specials resolve the numbers they cover when placed.
        /// <summary>A number and its wheel neighbours (two pockets either side of every pocket showing it).</summary>
        Neighbor,
        /// <summary>The number one above or below the previous winning number.</summary>
        Sequence,
        /// <summary>The previous winning number comes up again.</summary>
        Repeat,
        /// <summary>Any number that has not won in the last ten spins.</summary>
        Survivor,
        /// <summary>A hidden set of numbers, revealed after the spin.</summary>
        Mystery,
        /// <summary>A colour and a parity at once (group 0 red-odd, 1 red-even, 2 black-odd, 3 black-even).</summary>
        Combination,
        /// <summary>Wins only when the ball lands in a Jackpot pocket.</summary>
        Jackpot
    }
}

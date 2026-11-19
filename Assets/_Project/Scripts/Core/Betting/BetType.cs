namespace DeadSpin.Core.Betting
{
    /// <summary>Traditional roulette bets. Special bets (neighbour, sequence, jackpot...) arrive in Phase 7.</summary>
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
        Column
    }
}

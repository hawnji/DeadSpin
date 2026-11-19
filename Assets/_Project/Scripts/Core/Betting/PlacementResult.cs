namespace DeadSpin.Core.Betting
{
    /// <summary>Why a chip placement or removal succeeded or failed. The HUD turns these into messages.</summary>
    public enum PlacementResult
    {
        Accepted,
        BettingClosed,
        IllegalBet,
        CannotWin,
        BelowMinimum,
        AboveMaximum,
        InsufficientFunds,
        NothingToRemove,
        InvalidAmount
    }
}

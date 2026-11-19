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
        InvalidAmount,
        /// <summary>A rule has closed this section of the layout.</summary>
        SectionClosed,
        /// <summary>The chips are sticky or a rule forbids taking bets back.</summary>
        ChipsStuck,
        /// <summary>The special bet's conditions are not met (e.g. no previous result for a Repeat bet).</summary>
        NotEligible
    }
}

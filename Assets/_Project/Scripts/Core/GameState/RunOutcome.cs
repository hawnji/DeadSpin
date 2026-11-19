namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// How a run currently stands. The ending architecture (Phase 9) maps terminal outcomes to
    /// endings; new endings add values here and rules to <see cref="RunOutcomeEvaluator"/>.
    /// </summary>
    public enum RunOutcome
    {
        InProgress,
        /// <summary>Debt fully paid: the player earns freedom.</summary>
        Freedom,
        /// <summary>Balance can no longer cover the minimum bet and no money is on the table.</summary>
        Bankrupt,
        /// <summary>All rounds were played and debt remains.</summary>
        DeadlineMissed,
        /// <summary>Debt paid and the Game Master beaten at his own table: the player takes over the casino.</summary>
        NewOwner,
        /// <summary>The player accepted the Game Master's final offer and now works for the house.</summary>
        TheDeal,
        /// <summary>The player proved the house broke its own rules and walked out without paying in full.</summary>
        Loophole
    }
}

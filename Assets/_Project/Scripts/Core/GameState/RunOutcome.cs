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
        DeadlineMissed
    }
}

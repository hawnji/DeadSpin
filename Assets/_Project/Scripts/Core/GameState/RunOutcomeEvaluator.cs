namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// Pure rules that decide whether a run has been won or lost. Kept separate from the run so the
    /// rules are trivially testable and future rule modifiers (e.g. "Higher Stakes") can feed in an
    /// effective minimum bet instead of the configured one.
    /// </summary>
    public static class RunOutcomeEvaluator
    {
        /// <param name="balance">Current player balance.</param>
        /// <param name="debt">Outstanding debt.</param>
        /// <param name="moneyAtRisk">Money currently wagered on the table and not yet resolved.</param>
        /// <param name="roundsPlayed">Completed rounds.</param>
        /// <param name="totalRounds">Rounds allowed in this run.</param>
        /// <param name="effectiveMinimumBet">Minimum bet after rule modifiers.</param>
        public static RunOutcome Evaluate(long balance, long debt, long moneyAtRisk, int roundsPlayed, int totalRounds, long effectiveMinimumBet)
        {
            if (debt <= 0) return RunOutcome.Freedom;

            // Money on the table may still come back, so the player is not bankrupt yet.
            if (moneyAtRisk <= 0 && balance < effectiveMinimumBet) return RunOutcome.Bankrupt;

            if (roundsPlayed >= totalRounds) return RunOutcome.DeadlineMissed;

            return RunOutcome.InProgress;
        }

        public static bool IsTerminal(RunOutcome outcome)
        {
            return outcome != RunOutcome.InProgress;
        }
    }
}

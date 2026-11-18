namespace DeadSpin.Core.GameState
{
    /// <summary>Phases of a single roulette round (master prompt section 3).</summary>
    public enum RoundPhase
    {
        /// <summary>No round in progress; waiting for the player to signal readiness.</summary>
        Idle,
        /// <summary>Game Master announces the active rules. Timed.</summary>
        Announcing,
        /// <summary>Wagers may be placed and adjusted. Timed countdown.</summary>
        Betting,
        /// <summary>Betting is closed; waiting for the spin system to start the wheel.</summary>
        BettingClosed,
        /// <summary>Wheel and ball are animating towards the pre-validated result.</summary>
        Spinning,
        /// <summary>Payouts, penalties and special effects are being applied.</summary>
        Resolving,
        /// <summary>Results are shown. Timed; then the round ends.</summary>
        Summary
    }
}

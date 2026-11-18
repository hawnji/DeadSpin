namespace DeadSpin.Core.GameState
{
    /// <summary>Top-level mode of the game. Controls input routing, cursor and which UI is shown.</summary>
    public enum GameMode
    {
        /// <summary>Systems are being created; nothing is interactive yet.</summary>
        Boot,
        /// <summary>Free first-person movement around the casino.</summary>
        Exploring,
        /// <summary>Seated at the roulette table; movement locked, table interaction enabled.</summary>
        AtTable,
        /// <summary>Game paused; time-driven systems must not advance.</summary>
        Paused,
        /// <summary>The run has ended (freedom or ruin). Only end-screen actions remain.</summary>
        RunEnded
    }
}

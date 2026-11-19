namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// Top-level mode of the game. The whole game is played seated at the roulette table from a
    /// top-down camera, so there is no free-roaming mode.
    /// </summary>
    public enum GameMode
    {
        /// <summary>Systems are being created; nothing is interactive yet.</summary>
        Boot,
        /// <summary>Seated at the table: camera views, pointing, inspecting and (later) betting.</summary>
        AtTable,
        /// <summary>Game paused; time-driven systems must not advance.</summary>
        Paused,
        /// <summary>The run has ended (freedom or ruin). Only end-screen actions remain.</summary>
        RunEnded
    }
}

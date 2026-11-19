using System;
using System.Collections.Generic;

namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// Immutable-by-convention configuration for a single run. In Unity this is produced by
    /// RunConfigAsset (a ScriptableObject) so designers can tune values without code changes.
    /// All money values are whole in-game dollars (fictional currency).
    /// </summary>
    [Serializable]
    public sealed class RunConfig
    {
        public long StartingBalance = 2500;
        public long StartingDebt = 50000;
        public int TotalRounds = 18;
        public long MinimumBet = 10;

        /// <summary>Largest total allowed on a single betting spot.</summary>
        public long MaximumBet = 10000;

        /// <summary>Seconds the Game Master spends announcing rules before betting opens.</summary>
        public float AnnouncementSeconds = 3f;

        /// <summary>Length of the betting countdown.</summary>
        public float BettingSeconds = 30f;

        /// <summary>Pause after the result before the next round can start.</summary>
        public float SummarySeconds = 4f;

        /// <summary>0 means "pick a seed from the clock when the run starts".</summary>
        public ulong Seed;

        public static RunConfig CreateDefault()
        {
            return new RunConfig();
        }

        public RunConfig Clone()
        {
            return (RunConfig)MemberwiseClone();
        }

        /// <summary>Returns every problem with this configuration; an empty list means it is valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (StartingBalance <= 0) errors.Add("Starting balance must be positive.");
            if (StartingDebt < 0) errors.Add("Starting debt cannot be negative.");
            if (TotalRounds <= 0) errors.Add("Total rounds must be at least 1.");
            if (MinimumBet <= 0) errors.Add("Minimum bet must be positive.");
            if (MaximumBet < MinimumBet) errors.Add("Maximum bet cannot be below the minimum bet.");
            if (MinimumBet > StartingBalance) errors.Add("Minimum bet cannot exceed the starting balance, or the run is lost before it starts.");
            if (AnnouncementSeconds < 0f) errors.Add("Announcement time cannot be negative.");
            if (BettingSeconds <= 0f) errors.Add("Betting time must be positive.");
            if (SummarySeconds < 0f) errors.Add("Summary time cannot be negative.");
            return errors;
        }

        public void ThrowIfInvalid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Invalid RunConfig: " + string.Join(" ", errors));
            }
        }
    }
}

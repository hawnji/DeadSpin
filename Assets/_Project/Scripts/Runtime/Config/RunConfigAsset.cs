using DeadSpin.Core.GameState;
using UnityEngine;

namespace DeadSpin.Runtime.Config
{
    /// <summary>
    /// Designer-editable run settings. Create via Assets > Create > Dead Spin > Run Config and
    /// assign it to GameSession. When no asset is assigned, GameSession uses RunConfig defaults.
    /// </summary>
    [CreateAssetMenu(fileName = "RunConfig", menuName = "Dead Spin/Run Config", order = 0)]
    public sealed class RunConfigAsset : ScriptableObject
    {
        [Header("Money (in-game dollars, fictional)")]
        [Min(1)] [SerializeField] private long startingBalance = 2500;
        [Min(0)] [SerializeField] private long startingDebt = 50000;
        [Min(1)] [SerializeField] private long minimumBet = 10;

        [Header("Rounds")]
        [Min(1)] [SerializeField] private int totalRounds = 18;
        [Min(0f)] [SerializeField] private float announcementSeconds = 3f;
        [Min(1f)] [SerializeField] private float bettingSeconds = 30f;
        [Min(0f)] [SerializeField] private float summarySeconds = 4f;

        [Header("Randomness")]
        [Tooltip("0 = new seed every run. Any other value makes runs reproducible.")]
        [SerializeField] private ulong seed;

        public RunConfig ToRunConfig()
        {
            return new RunConfig
            {
                StartingBalance = startingBalance,
                StartingDebt = startingDebt,
                MinimumBet = minimumBet,
                TotalRounds = totalRounds,
                AnnouncementSeconds = announcementSeconds,
                BettingSeconds = bettingSeconds,
                SummarySeconds = summarySeconds,
                Seed = seed
            };
        }

        private void OnValidate()
        {
            foreach (string error in ToRunConfig().Validate())
            {
                Debug.LogWarning("[RunConfigAsset] " + name + ": " + error, this);
            }
        }
    }
}

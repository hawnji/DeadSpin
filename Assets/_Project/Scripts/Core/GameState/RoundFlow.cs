using System;

namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// Drives the phase sequence of one roulette round. Timed phases advance via <see cref="Tick"/>;
    /// phases that depend on other systems (spin animation, payout resolution) advance only when
    /// those systems call <see cref="NotifySpinStarted"/>, <see cref="NotifySpinFinished"/> and
    /// <see cref="NotifyResolutionFinished"/>. This keeps visual timing out of the financial logic.
    /// </summary>
    public sealed class RoundFlow
    {
        private readonly RunConfig _config;

        public RoundPhase Phase { get; private set; } = RoundPhase.Idle;

        /// <summary>Seconds left in the current timed phase (0 for untimed phases).</summary>
        public float TimeRemaining { get; private set; }

        /// <summary>Extra seconds granted to the current betting window (abilities, hourglass pockets).</summary>
        public float BonusBettingSeconds { get; private set; }

        /// <summary>Added to the configured betting time for the next betting window (rules). The window never drops below <see cref="MinimumBettingSeconds"/>.</summary>
        public float BettingSecondsDelta { get; set; }

        public const float MinimumBettingSeconds = 8f;

        /// <summary>Length the next (or current) betting window starts with.</summary>
        public float BettingWindowSeconds => Math.Max(MinimumBettingSeconds, _config.BettingSeconds + BettingSecondsDelta);

        public bool IsBettingOpen => Phase == RoundPhase.Betting;
        public bool InProgress => Phase != RoundPhase.Idle;

        /// <summary>Raised with (previous, next) after every phase change.</summary>
        public event Action<RoundPhase, RoundPhase> PhaseChanged;

        /// <summary>Raised when the summary phase finishes and the round is complete.</summary>
        public event Action RoundCompleted;

        public RoundFlow(RunConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config;
        }

        /// <summary>Starts a new round. Only valid from <see cref="RoundPhase.Idle"/>.</summary>
        public bool TryBeginRound()
        {
            if (Phase != RoundPhase.Idle) return false;
            BonusBettingSeconds = 0f;
            if (_config.AnnouncementSeconds > 0f)
            {
                SetPhase(RoundPhase.Announcing, _config.AnnouncementSeconds);
            }
            else
            {
                SetPhase(RoundPhase.Betting, BettingWindowSeconds);
            }
            return true;
        }

        /// <summary>Advances timed phases. Call once per frame with unscaled-or-scaled delta while not paused.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            float remaining = deltaSeconds;

            // Loop so a large delta can carry through several zero-length or short phases deterministically.
            while (remaining > 0f && IsTimedPhase(Phase))
            {
                if (TimeRemaining > remaining)
                {
                    TimeRemaining -= remaining;
                    return;
                }

                remaining -= TimeRemaining;
                TimeRemaining = 0f;
                AdvanceTimedPhase();
            }
        }

        /// <summary>Adds betting time. Only valid while betting is open.</summary>
        public bool TryExtendBetting(float seconds)
        {
            if (Phase != RoundPhase.Betting || seconds <= 0f) return false;
            TimeRemaining += seconds;
            BonusBettingSeconds += seconds;
            return true;
        }

        /// <summary>Ends betting early (player declares "no more bets" or a rule closes the table).</summary>
        public bool TryCloseBettingEarly()
        {
            if (Phase != RoundPhase.Betting) return false;
            SetPhase(RoundPhase.BettingClosed, 0f);
            return true;
        }

        /// <summary>Shortens or lengthens the current betting window (hourglass, rules). Never below one second.</summary>
        public bool TryAdjustBettingTime(float seconds)
        {
            if (Phase != RoundPhase.Betting) return false;
            TimeRemaining = Math.Max(1f, TimeRemaining + seconds);
            if (seconds > 0f) BonusBettingSeconds += seconds;
            return true;
        }

        /// <summary>The Double Spin: goes from resolution straight back to "betting closed" for another spin.</summary>
        public bool RestartSpin()
        {
            if (Phase != RoundPhase.Resolving) return false;
            SetPhase(RoundPhase.BettingClosed, 0f);
            return true;
        }

        public bool NotifySpinStarted()
        {
            if (Phase != RoundPhase.BettingClosed) return false;
            SetPhase(RoundPhase.Spinning, 0f);
            return true;
        }

        public bool NotifySpinFinished()
        {
            if (Phase != RoundPhase.Spinning) return false;
            SetPhase(RoundPhase.Resolving, 0f);
            return true;
        }

        public bool NotifyResolutionFinished()
        {
            if (Phase != RoundPhase.Resolving) return false;
            if (_config.SummarySeconds > 0f)
            {
                SetPhase(RoundPhase.Summary, _config.SummarySeconds);
            }
            else
            {
                CompleteRound();
            }
            return true;
        }

        /// <summary>Aborts the current round without resolution (run ended, debug reset).</summary>
        public void Abort()
        {
            if (Phase == RoundPhase.Idle) return;
            SetPhase(RoundPhase.Idle, 0f);
        }

        private static bool IsTimedPhase(RoundPhase phase)
        {
            return phase == RoundPhase.Announcing || phase == RoundPhase.Betting || phase == RoundPhase.Summary;
        }

        private void AdvanceTimedPhase()
        {
            switch (Phase)
            {
                case RoundPhase.Announcing:
                    SetPhase(RoundPhase.Betting, BettingWindowSeconds);
                    break;
                case RoundPhase.Betting:
                    SetPhase(RoundPhase.BettingClosed, 0f);
                    break;
                case RoundPhase.Summary:
                    CompleteRound();
                    break;
                default:
                    throw new InvalidOperationException("Phase " + Phase + " is not timed.");
            }
        }

        private void CompleteRound()
        {
            SetPhase(RoundPhase.Idle, 0f);
            RoundCompleted?.Invoke();
        }

        private void SetPhase(RoundPhase next, float duration)
        {
            RoundPhase previous = Phase;
            Phase = next;
            TimeRemaining = duration;
            PhaseChanged?.Invoke(previous, next);
        }
    }
}

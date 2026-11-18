using System;
using System.Collections.Generic;

namespace DeadSpin.Core.GameState
{
    /// <summary>
    /// Explicit transition table for the top-level <see cref="GameMode"/>. Illegal transitions are
    /// rejected instead of silently applied, which keeps input, camera and UI from drifting into
    /// inconsistent states. Pause remembers the mode it interrupted.
    /// </summary>
    public sealed class GameStateMachine
    {
        private static readonly Dictionary<GameMode, GameMode[]> Allowed = new Dictionary<GameMode, GameMode[]>
        {
            { GameMode.Boot, new[] { GameMode.Exploring } },
            { GameMode.Exploring, new[] { GameMode.AtTable, GameMode.Paused, GameMode.RunEnded } },
            { GameMode.AtTable, new[] { GameMode.Exploring, GameMode.Paused, GameMode.RunEnded } },
            { GameMode.Paused, new[] { GameMode.Exploring, GameMode.AtTable, GameMode.RunEnded } },
            { GameMode.RunEnded, new GameMode[0] }
        };

        private GameMode _modeBeforePause = GameMode.Exploring;

        public GameMode Current { get; private set; } = GameMode.Boot;

        public bool IsPaused => Current == GameMode.Paused;

        /// <summary>Raised with (previous, next) after a successful transition.</summary>
        public event Action<GameMode, GameMode> ModeChanged;

        public bool CanTransition(GameMode next)
        {
            if (next == Current) return false;
            GameMode[] targets;
            if (!Allowed.TryGetValue(Current, out targets)) return false;
            if (Array.IndexOf(targets, next) < 0) return false;
            // Leaving pause may only return to the mode that was interrupted (or end the run).
            if (Current == GameMode.Paused && next != GameMode.RunEnded && next != _modeBeforePause) return false;
            return true;
        }

        public bool TryTransition(GameMode next)
        {
            if (!CanTransition(next)) return false;
            GameMode previous = Current;
            if (next == GameMode.Paused) _modeBeforePause = previous;
            Current = next;
            ModeChanged?.Invoke(previous, next);
            return true;
        }

        public bool TryPause()
        {
            return TryTransition(GameMode.Paused);
        }

        public bool TryResume()
        {
            return Current == GameMode.Paused && TryTransition(_modeBeforePause);
        }
    }
}

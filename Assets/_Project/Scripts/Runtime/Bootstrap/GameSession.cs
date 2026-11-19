using System;
using DeadSpin.Core.GameState;
using DeadSpin.Runtime.Config;
using DeadSpin.Runtime.Interaction;
using DeadSpin.Runtime.Player;
using UnityEngine;

namespace DeadSpin.Runtime.Bootstrap
{
    /// <summary>
    /// Scene-level owner of the current <see cref="Run"/> and the top-level <see cref="GameStateMachine"/>.
    /// It is the only place that changes game mode, and it applies each mode to the table camera,
    /// interaction and time scale. Exactly one may exist per scene.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameSession : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Optional. When empty, the default run settings are used ($2,500 balance, $50,000 debt, 18 rounds).")]
        [SerializeField] private RunConfigAsset runConfig;

        [Header("Player (auto-found when empty)")]
        [SerializeField] private PlayerInputReader playerInput;
        [SerializeField] private TableCameraRig cameraRig;
        [SerializeField] private Interactor interactor;

        private GameStateMachine _stateMachine;

        /// <summary>The session in the loaded scene, or null before it has awoken.</summary>
        public static GameSession Active { get; private set; }

        public Run Run { get; private set; }
        public GameMode Mode => _stateMachine != null ? _stateMachine.Current : GameMode.Boot;
        public Interactor Interactor => interactor;
        public PlayerInputReader PlayerInput => playerInput;
        public TableCameraRig CameraRig => cameraRig;

        /// <summary>Raised with (previous, next) whenever the game mode changes.</summary>
        public event Action<GameMode, GameMode> ModeChanged;

        /// <summary>Raised whenever a new run begins (including restarts).</summary>
        public event Action<Run> RunStarted;

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogError("[GameSession] A second GameSession was found in the scene. Only one may exist; disabling the duplicate.", this);
                enabled = false;
                return;
            }
            Active = this;

            ResolveReferences();
            CreateStateMachine();
            StartNewRun();
        }

        private void Start()
        {
            if (!enabled) return;
            _stateMachine.TryTransition(GameMode.AtTable);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (Run != null) Run.Ended -= OnRunEnded;
            Time.timeScale = 1f;
        }

        private void Update()
        {
            if (playerInput != null && playerInput.Current.BackPressed)
            {
                if (Mode == GameMode.AtTable) TryPause();
                else if (Mode == GameMode.Paused) TryResume();
            }

            if (!_stateMachine.IsPaused && !Run.IsOver)
            {
                Run.Round.Tick(Time.deltaTime);
            }
        }

        public bool TryPause()
        {
            return _stateMachine.TryPause();
        }

        public bool TryResume()
        {
            return _stateMachine.TryResume();
        }

        /// <summary>Throws away the current run and starts a fresh one without reloading the scene.</summary>
        public void RestartRun()
        {
            CreateStateMachine();
            StartNewRun();
            if (cameraRig != null) cameraRig.ResetToView(0, false);
            _stateMachine.TryTransition(GameMode.AtTable);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void CreateStateMachine()
        {
            if (_stateMachine != null) _stateMachine.ModeChanged -= OnModeChanged;
            _stateMachine = new GameStateMachine();
            _stateMachine.ModeChanged += OnModeChanged;
        }

        private void StartNewRun()
        {
            if (Run != null) Run.Ended -= OnRunEnded;
            RunConfig config = runConfig != null ? runConfig.ToRunConfig() : RunConfig.CreateDefault();
            try
            {
                Run = new Run(config);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError("[GameSession] " + exception.Message + " Falling back to default run settings.", this);
                Run = new Run(RunConfig.CreateDefault());
            }
            Run.Ended += OnRunEnded;
            Debug.Log("[GameSession] New run started. Seed " + Run.Random.Seed + ", balance $" + Run.Ledger.Balance + ", debt $" + Run.Ledger.Debt + ".", this);
            RunStarted?.Invoke(Run);
        }

        private void OnRunEnded(RunOutcome outcome)
        {
            Debug.Log("[GameSession] Run ended: " + outcome, this);
            _stateMachine.TryTransition(GameMode.RunEnded);
        }

        private void OnModeChanged(GameMode previous, GameMode next)
        {
            ApplyMode(next);
            ModeChanged?.Invoke(previous, next);
        }

        /// <summary>Single place that maps a game mode onto controls, cursor and time.</summary>
        private void ApplyMode(GameMode mode)
        {
            bool playing = mode == GameMode.AtTable;

            Time.timeScale = mode == GameMode.Paused ? 0f : 1f;
            if (cameraRig != null) cameraRig.ControlsEnabled = playing;
            if (interactor != null) interactor.enabled = playing;

            // The cursor is the player's hand at the table, so it is always visible.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ResolveReferences()
        {
            if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInputReader>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<TableCameraRig>();
            if (interactor == null) interactor = FindFirstObjectByType<Interactor>();

            if (playerInput == null || cameraRig == null || interactor == null)
            {
                Debug.LogError("[GameSession] Table rig is incomplete. Expected PlayerInputReader, TableCameraRig and Interactor. Use Dead Spin > Build Prototype Scene to create a correct scene.", this);
            }
        }
    }
}

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
    /// It is the only place that changes game mode, and it applies each mode to the player
    /// (movement, look, cursor, camera focus, interaction aim). Exactly one may exist per scene.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameSession : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Optional. When empty, the default run settings are used ($2,500 balance, $50,000 debt, 18 rounds).")]
        [SerializeField] private RunConfigAsset runConfig;

        [Header("Player (auto-found when empty)")]
        [SerializeField] private PlayerInputReader playerInput;
        [SerializeField] private FirstPersonController playerController;
        [SerializeField] private PlayerLook playerLook;
        [SerializeField] private CameraFocus cameraFocus;
        [SerializeField] private Interactor interactor;

        [Header("Spawn")]
        [SerializeField] private Transform playerSpawn;

        private GameStateMachine _stateMachine;
        private RouletteTableStation _currentStation;

        /// <summary>The session in the loaded scene, or null before it has awoken.</summary>
        public static GameSession Active { get; private set; }

        public Run Run { get; private set; }
        public GameMode Mode => _stateMachine != null ? _stateMachine.Current : GameMode.Boot;
        public Interactor Interactor => interactor;
        public PlayerInputReader PlayerInput => playerInput;

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

            ResolvePlayerReferences();
            _stateMachine = new GameStateMachine();
            _stateMachine.ModeChanged += OnModeChanged;
            StartNewRun();
        }

        private void Start()
        {
            if (!enabled) return;
            _stateMachine.TryTransition(GameMode.Exploring);
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
                HandleBack();
            }

            if (!_stateMachine.IsPaused && !Run.IsOver)
            {
                Run.Round.Tick(Time.deltaTime);
            }
        }

        /// <summary>Seats the player at the given table station.</summary>
        public bool TrySitAtTable(RouletteTableStation station)
        {
            if (station == null || station.ViewPoint == null) return false;
            if (!_stateMachine.CanTransition(GameMode.AtTable)) return false;
            _currentStation = station;
            return _stateMachine.TryTransition(GameMode.AtTable);
        }

        public bool TryStandUp()
        {
            return Mode == GameMode.AtTable && _stateMachine.TryTransition(GameMode.Exploring);
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
            _stateMachine.ModeChanged -= OnModeChanged;
            _stateMachine = new GameStateMachine();
            _stateMachine.ModeChanged += OnModeChanged;
            _currentStation = null;

            StartNewRun();
            if (playerSpawn != null && playerController != null)
            {
                playerController.Teleport(playerSpawn.position, playerSpawn.eulerAngles.y);
                if (playerLook != null) playerLook.SyncFromTransforms();
            }
            _stateMachine.TryTransition(GameMode.Exploring);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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

        private void HandleBack()
        {
            switch (Mode)
            {
                case GameMode.AtTable:
                    TryStandUp();
                    break;
                case GameMode.Exploring:
                    TryPause();
                    break;
                case GameMode.Paused:
                    TryResume();
                    break;
            }
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

        /// <summary>Single place that maps a game mode onto player controls and cursor state.</summary>
        private void ApplyMode(GameMode mode)
        {
            bool exploring = mode == GameMode.Exploring;
            bool atTable = mode == GameMode.AtTable;

            Time.timeScale = mode == GameMode.Paused ? 0f : 1f;

            if (playerController != null) playerController.MovementEnabled = exploring;
            if (playerLook != null) playerLook.LookEnabled = exploring;

            if (interactor != null)
            {
                interactor.enabled = exploring || atTable;
                interactor.Mode = atTable ? Interactor.AimMode.Pointer : Interactor.AimMode.Crosshair;
            }

            if (cameraFocus != null)
            {
                if (atTable && _currentStation != null)
                {
                    cameraFocus.Focus(_currentStation.ViewPoint);
                }
                else if (mode != GameMode.Paused)
                {
                    cameraFocus.Release();
                    _currentStation = null;
                }
            }

            bool lockCursor = exploring;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }

        private void ResolvePlayerReferences()
        {
            if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInputReader>();
            if (playerController == null) playerController = FindFirstObjectByType<FirstPersonController>();
            if (playerLook == null) playerLook = FindFirstObjectByType<PlayerLook>();
            if (cameraFocus == null) cameraFocus = FindFirstObjectByType<CameraFocus>();
            if (interactor == null) interactor = FindFirstObjectByType<Interactor>();

            if (playerInput == null || playerController == null || playerLook == null || cameraFocus == null || interactor == null)
            {
                Debug.LogError("[GameSession] Player rig is incomplete. Expected PlayerInputReader, FirstPersonController, PlayerLook, CameraFocus and Interactor. Use Dead Spin > Build Prototype Scene to create a correct scene.", this);
            }
        }

        public void SetPlayerSpawn(Transform spawn)
        {
            playerSpawn = spawn;
        }
    }
}

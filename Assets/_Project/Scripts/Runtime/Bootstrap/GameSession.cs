using System;
using System.IO;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Persistence;
using DeadSpin.Core.Progression;
using DeadSpin.Runtime.Config;
using DeadSpin.Runtime.Interaction;
using DeadSpin.Runtime.Player;
using UnityEngine;

namespace DeadSpin.Runtime.Bootstrap
{
    /// <summary>
    /// Scene-level owner of the current <see cref="CasinoRun"/>, the player's <see cref="MetaProgress"/>
    /// and the top-level <see cref="GameStateMachine"/>. It is the only place that changes game mode,
    /// it ticks the run, and it saves: the run autosaves between rounds and the meta progress is
    /// saved whenever a run ends or an unlock is bought. Exactly one may exist per scene.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameSession : MonoBehaviour
    {
        public const string RunSaveFile = "deadspin_run.json";
        public const string MetaSaveFile = "deadspin_meta.json";

        [Header("Configuration")]
        [Tooltip("Optional. When empty, the default run settings are used. Starting money and debt are randomised a little around these values each run.")]
        [SerializeField] private RunConfigAsset runConfig;
        [Tooltip("Vary the starting debt and balance between runs.")]
        [SerializeField] private bool randomizeStartingNumbers = true;
        [Tooltip("Skip the title screen and start a new run immediately (handy while testing).")]
        [SerializeField] private bool skipTitle;

        [Header("Player (auto-found when empty)")]
        [SerializeField] private PlayerInputReader playerInput;
        [SerializeField] private TableCameraRig cameraRig;
        [SerializeField] private Interactor interactor;

        private GameStateMachine _stateMachine;

        /// <summary>The session in the loaded scene, or null before it has awoken.</summary>
        public static GameSession Active { get; private set; }

        /// <summary>The current run, or null on the title screen before the first run.</summary>
        public CasinoRun Casino { get; private set; }
        public Run Run => Casino != null ? Casino.Run : null;
        public MetaProgress Meta { get; private set; }
        public GameMode Mode => _stateMachine != null ? _stateMachine.Current : GameMode.Boot;
        public Interactor Interactor => interactor;
        public PlayerInputReader PlayerInput => playerInput;
        public TableCameraRig CameraRig => cameraRig;

        /// <summary>The last save or load problem, shown on the title screen.</summary>
        public string SaveError { get; private set; }
        public bool HasSavedRun => File.Exists(SavePath(RunSaveFile));

        /// <summary>Raised with (previous, next) whenever the game mode changes.</summary>
        public event Action<GameMode, GameMode> ModeChanged;

        /// <summary>Raised whenever a run begins: new, continued or restarted.</summary>
        public event Action<CasinoRun> RunStarted;

        /// <summary>Raised when a run reaches an ending (after the meta progress is saved).</summary>
        public event Action<CasinoRun> RunFinished;

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
            Meta = LoadMeta();
            CreateStateMachine();
        }

        private void Start()
        {
            if (!enabled) return;
            ApplyMode(GameMode.Boot);
            if (skipTitle) NewRun();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            DetachRun();
            Time.timeScale = 1f;
        }

        private void OnApplicationQuit()
        {
            TrySaveRun();
        }

        private void Update()
        {
            if (playerInput != null && playerInput.Current.BackPressed)
            {
                if (Mode == GameMode.AtTable) TryPause();
                else if (Mode == GameMode.Paused) TryResume();
            }

            if (Casino != null && Mode == GameMode.AtTable && !Casino.Run.IsOver)
            {
                Casino.Tick(Time.deltaTime);
            }
        }

        // ------------------------------------------------------------------ runs

        /// <summary>Starts a brand new run (any saved run is discarded).</summary>
        public void NewRun(ulong seed = 0)
        {
            RunConfig template = runConfig != null ? runConfig.ToRunConfig() : RunConfig.CreateDefault();
            if (seed == 0) seed = template.Seed;
            CasinoRun casino;
            try
            {
                casino = new CasinoRun(RunSetup.Create(seed, Meta, template, randomizeStartingNumbers), Meta);
            }
            catch (Exception exception)
            {
                Debug.LogError("[GameSession] " + exception.Message + " Falling back to default run settings.", this);
                casino = new CasinoRun(RunSetup.Create(seed, Meta, RunConfig.CreateDefault(), randomizeStartingNumbers), Meta);
            }
            DeleteSave(RunSaveFile);
            SaveMeta();
            BeginRun(casino);
            Debug.Log("[GameSession] New run. Seed " + casino.Run.Random.Seed + ", balance $" + casino.Ledger.Balance + ", debt $" + casino.Ledger.Debt + ", Game Master: " + casino.Setup.Personality + ".", this);
        }

        /// <summary>Loads the saved run. Returns false (and sets <see cref="SaveError"/>) when there is none or it is unreadable.</summary>
        public bool ContinueRun()
        {
            SaveError = null;
            string path = SavePath(RunSaveFile);
            if (!File.Exists(path)) { SaveError = "There is no saved run."; return false; }
            try
            {
                CasinoRun casino = SaveSystem.LoadRun(File.ReadAllText(path), Meta);
                BeginRun(casino);
                Debug.Log("[GameSession] Continued the saved run at round " + casino.CurrentRound + ".", this);
                return true;
            }
            catch (Exception exception)
            {
                SaveError = "The saved run could not be loaded: " + exception.Message;
                Debug.LogError("[GameSession] " + SaveError, this);
                return false;
            }
        }

        /// <summary>Throws away the current run and starts a fresh one without reloading the scene.</summary>
        public void RestartRun()
        {
            NewRun();
        }

        /// <summary>Saves (when possible) and goes back to the title screen.</summary>
        public void ReturnToTitle()
        {
            TrySaveRun();
            DetachRun();
            Casino = null;
            CreateStateMachine();
            ApplyMode(GameMode.Boot);
            ModeChanged?.Invoke(GameMode.Paused, GameMode.Boot);
        }

        /// <summary>Saves the run if it is between rounds. Returns true when a save was written.</summary>
        public bool TrySaveRun()
        {
            if (Casino == null || !SaveSystem.CanSave(Casino)) return false;
            try
            {
                WriteSave(RunSaveFile, SaveSystem.SaveRun(Casino));
                return true;
            }
            catch (Exception exception)
            {
                SaveError = "Saving failed: " + exception.Message;
                Debug.LogError("[GameSession] " + SaveError, this);
                return false;
            }
        }

        /// <summary>Buys a permanent unlock with Marks. Returns null on success, or the reason it failed.</summary>
        public string TryUnlock(string id)
        {
            string error = Meta.TryUnlock(id);
            if (error == null) SaveMeta();
            return error;
        }

        public void QuitGame()
        {
            TrySaveRun();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public bool TryPause() { return _stateMachine.TryPause(); }

        public bool TryResume() { return _stateMachine.TryResume(); }

        private void BeginRun(CasinoRun casino)
        {
            DetachRun();
            Casino = casino;
            Casino.RoundEnded += OnRoundEnded;
            Casino.RunEnded += OnRunEnded;
            CreateStateMachine();
            if (cameraRig != null) cameraRig.ResetToView(0, false);
            RunStarted?.Invoke(Casino);
            _stateMachine.TryTransition(GameMode.AtTable);
        }

        private void DetachRun()
        {
            if (Casino == null) return;
            Casino.RoundEnded -= OnRoundEnded;
            Casino.RunEnded -= OnRunEnded;
        }

        private void OnRoundEnded(int round)
        {
            // RoundEnded fires while the round is wrapping up; save on the next frame, once it is idle.
            _saveRequested = true;
        }

        private bool _saveRequested;

        private void LateUpdate()
        {
            if (!_saveRequested || Casino == null) return;
            if (Casino.Run.IsOver) { _saveRequested = false; return; }
            if (SaveSystem.CanSave(Casino))
            {
                _saveRequested = false;
                TrySaveRun();
            }
        }

        private void OnRunEnded(EndingDefinition ending)
        {
            Debug.Log("[GameSession] Run ended: " + (ending != null ? ending.Title : "?") + ". Marks earned: " + Casino.MarksEarned + ".", this);
            DeleteSave(RunSaveFile);
            SaveMeta();
            _stateMachine.TryTransition(GameMode.RunEnded);
            RunFinished?.Invoke(Casino);
        }

        // ------------------------------------------------------------------ persistence

        private static string SavePath(string file)
        {
            return Path.Combine(Application.persistentDataPath, file);
        }

        private MetaProgress LoadMeta()
        {
            string path = SavePath(MetaSaveFile);
            if (!File.Exists(path)) return new MetaProgress();
            try
            {
                return SaveSystem.LoadMeta(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                SaveError = "Your progress file could not be read (" + exception.Message + "). A backup was kept.";
                Debug.LogError("[GameSession] " + SaveError, this);
                try { File.Copy(path, path + ".bak", true); } catch (Exception) { }
                return new MetaProgress();
            }
        }

        private void SaveMeta()
        {
            try
            {
                WriteSave(MetaSaveFile, SaveSystem.SaveMeta(Meta));
            }
            catch (Exception exception)
            {
                SaveError = "Saving progress failed: " + exception.Message;
                Debug.LogError("[GameSession] " + SaveError, this);
            }
        }

        /// <summary>Writes through a temporary file so a crash mid-write never corrupts the save.</summary>
        private static void WriteSave(string file, string json)
        {
            string path = SavePath(file);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static void DeleteSave(string file)
        {
            string path = SavePath(file);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { Debug.LogWarning("[GameSession] Could not delete " + path + ": " + exception.Message); }
        }

        // ------------------------------------------------------------------ modes

        private void CreateStateMachine()
        {
            if (_stateMachine != null) _stateMachine.ModeChanged -= OnModeChanged;
            _stateMachine = new GameStateMachine();
            _stateMachine.ModeChanged += OnModeChanged;
        }

        private void OnModeChanged(GameMode previous, GameMode next)
        {
            ApplyMode(next);
            if (next == GameMode.Paused) TrySaveRun();
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
            if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInputReader>();
            if (cameraRig == null) cameraRig = FindAnyObjectByType<TableCameraRig>();
            if (interactor == null) interactor = FindAnyObjectByType<Interactor>();

            if (playerInput == null || cameraRig == null || interactor == null)
            {
                Debug.LogError("[GameSession] Table rig is incomplete. Expected PlayerInputReader, TableCameraRig and Interactor. Use Dead Spin > Build Prototype Scene to create a correct scene.", this);
            }
        }
    }
}

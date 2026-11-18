using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using DeadSpin.Runtime.Bootstrap;
using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>
    /// Phase 1 prototype HUD drawn with IMGUI so it needs no prefabs, fonts or packages.
    /// Shows the run state (money, debt, round), the interaction prompt, inspection text,
    /// pause and end-of-run screens, and a developer panel (F1) for exercising game-state rules.
    /// Phase 8 replaces this with the final animated UI; the data it reads stays the same.
    /// </summary>
    public sealed class PrototypeHud : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;

        [SerializeField] private GameSession session;
        [Tooltip("Multiplies the resolution-based scale. 1 = default size.")]
        [Range(0.5f, 2f)] [SerializeField] private float uiScale = 1f;
        [Min(1f)] [SerializeField] private float inspectionSeconds = 6f;

        private string _inspectionTitle;
        private string _inspectionBody;
        private float _inspectionHideTime;
        private bool _showDebug;
        private bool _stylesReady;

        private GUIStyle _panel;
        private GUIStyle _label;
        private GUIStyle _value;
        private GUIStyle _title;
        private GUIStyle _prompt;
        private GUIStyle _button;
        private GUIStyle _small;
        private Texture2D _panelTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;

        private static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f);
        private static readonly Color Bone = new Color(0.90f, 0.87f, 0.80f);
        private static readonly Color Blood = new Color(0.80f, 0.22f, 0.20f);

        public float UiScale
        {
            get { return uiScale; }
            set { uiScale = Mathf.Clamp(value, 0.5f, 2f); }
        }

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            if (session == null)
            {
                Debug.LogError("[PrototypeHud] No GameSession in the scene.", this);
                enabled = false;
                return;
            }
            if (session.Interactor != null) session.Interactor.InspectionReported += OnInspection;
            session.ModeChanged += OnModeChanged;
        }

        private void OnDestroy()
        {
            if (session == null) return;
            if (session.Interactor != null) session.Interactor.InspectionReported -= OnInspection;
            session.ModeChanged -= OnModeChanged;
            DestroyTexture(_panelTexture);
            DestroyTexture(_buttonTexture);
            DestroyTexture(_buttonHoverTexture);
        }

        private void Update()
        {
            if (session != null && session.PlayerInput != null && session.PlayerInput.Current.DebugTogglePressed && Debug.isDebugBuild)
            {
                _showDebug = !_showDebug;
            }
        }

        private void OnInspection(string title, string body)
        {
            _inspectionTitle = title;
            _inspectionBody = body;
            _inspectionHideTime = Time.unscaledTime + inspectionSeconds;
        }

        private void OnModeChanged(GameMode previous, GameMode next)
        {
            if (next == GameMode.Exploring || next == GameMode.RunEnded) _inspectionHideTime = 0f;
        }

        private void OnGUI()
        {
            if (session == null || session.Run == null) return;
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight * uiScale;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            Run run = session.Run;
            DrawRunPanel(run);
            DrawModeHint(width, height);

            switch (session.Mode)
            {
                case GameMode.Exploring:
                    DrawCrosshair(width, height);
                    DrawPrompt(width, height);
                    break;
                case GameMode.AtTable:
                    DrawPrompt(width, height);
                    break;
                case GameMode.Paused:
                    DrawPauseMenu(width, height);
                    break;
                case GameMode.RunEnded:
                    DrawRunEnded(run, width, height);
                    break;
            }

            if (Time.unscaledTime < _inspectionHideTime && session.Mode != GameMode.Paused)
            {
                DrawInspection(width, height);
            }

            if (_showDebug) DrawDebugPanel(run, width);

            GUI.matrix = previousMatrix;
        }

        private void DrawRunPanel(Run run)
        {
            var rect = new Rect(24f, 24f, 340f, 176f);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 20f, rect.y + 14f, rect.width - 40f, rect.height - 28f));
            Row("BALANCE", "$" + run.Ledger.Balance.ToString("N0"), Bone);
            Row("DEBT", "$" + run.Ledger.Debt.ToString("N0"), Blood);
            Row("ROUND", run.CurrentRoundNumber + " / " + run.Config.TotalRounds, Bone);
            Row("MIN BET", "$" + run.Config.MinimumBet.ToString("N0"), Bone);
            if (run.Round.InProgress)
            {
                Row(run.Round.Phase.ToString().ToUpperInvariant(), run.Round.TimeRemaining > 0f ? Mathf.CeilToInt(run.Round.TimeRemaining) + "s" : "", Gold);
            }
            GUILayout.EndArea();
        }

        private void Row(string label, string value, Color valueColor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(130f));
            _value.normal.textColor = valueColor;
            GUILayout.Label(value, _value);
            GUILayout.EndHorizontal();
        }

        private void DrawModeHint(float width, float height)
        {
            string hint;
            switch (session.Mode)
            {
                case GameMode.Exploring: hint = "WASD move   Shift sprint   E / Click interact   Esc pause"; break;
                case GameMode.AtTable: hint = "Point and click to inspect   Esc / Tab stand up"; break;
                default: hint = null; break;
            }
            if (hint == null) return;
            if (Debug.isDebugBuild) hint += "   F1 dev panel";
            GUI.Label(new Rect(24f, height - 44f, width - 48f, 28f), hint, _small);
        }

        private void DrawCrosshair(float width, float height)
        {
            bool focused = session.Interactor != null && session.Interactor.CurrentPrompt != null;
            float size = focused ? 8f : 4f;
            Color previous = GUI.color;
            GUI.color = focused ? Gold : new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(width * 0.5f - size * 0.5f, height * 0.5f - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawPrompt(float width, float height)
        {
            string prompt = session.Interactor != null ? session.Interactor.CurrentPrompt : null;
            if (string.IsNullOrEmpty(prompt)) return;
            string key = session.Mode == GameMode.AtTable ? "[Click]  " : "[E]  ";
            GUI.Label(new Rect(0f, height * 0.5f + 40f, width, 40f), key + prompt, _prompt);
        }

        private void DrawInspection(float width, float height)
        {
            var rect = new Rect(width * 0.5f - 320f, height - 250f, 640f, 170f);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 24f, rect.y + 16f, rect.width - 48f, rect.height - 32f));
            GUILayout.Label(_inspectionTitle, _title);
            GUILayout.Label(_inspectionBody, _small);
            GUILayout.EndArea();
        }

        private void DrawPauseMenu(float width, float height)
        {
            DrawDim(width, height);
            var rect = new Rect(width * 0.5f - 200f, height * 0.5f - 150f, 400f, 300f);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 30f, rect.y + 24f, rect.width - 60f, rect.height - 48f));
            GUILayout.Label("PAUSED", _title);
            GUILayout.Space(16f);
            if (GUILayout.Button("Resume", _button, GUILayout.Height(48f))) session.TryResume();
            GUILayout.Space(8f);
            if (GUILayout.Button("Restart run", _button, GUILayout.Height(48f))) session.RestartRun();
            GUILayout.Space(8f);
            if (GUILayout.Button("Quit", _button, GUILayout.Height(48f))) session.QuitGame();
            GUILayout.EndArea();
        }

        private void DrawRunEnded(Run run, float width, float height)
        {
            DrawDim(width, height);
            string heading;
            string body;
            switch (run.Outcome)
            {
                case RunOutcome.Freedom:
                    heading = "FREEDOM";
                    body = "The debt is paid. The door at the top of the stairs is unlocked... for now.";
                    break;
                case RunOutcome.Bankrupt:
                    heading = "THE HOUSE ALWAYS WINS";
                    body = "You cannot cover the minimum bet. The Game Master smiles and gestures to the staff.";
                    break;
                case RunOutcome.DeadlineMissed:
                    heading = "TIME IS UP";
                    body = "The final round has passed with $" + run.Ledger.Debt.ToString("N0") + " still owed.";
                    break;
                default:
                    heading = "RUN OVER";
                    body = string.Empty;
                    break;
            }

            var rect = new Rect(width * 0.5f - 300f, height * 0.5f - 170f, 600f, 340f);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 30f, rect.y + 24f, rect.width - 60f, rect.height - 48f));
            GUILayout.Label(heading, _title);
            GUILayout.Label(body, _small);
            GUILayout.Space(10f);
            GUILayout.Label("Rounds played: " + run.RoundsPlayed + "    Won: $" + run.Ledger.TotalWinnings.ToString("N0") + "    Lost: $" + run.Ledger.TotalLosses.ToString("N0"), _small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New run", _button, GUILayout.Height(48f))) session.RestartRun();
            GUILayout.Space(8f);
            if (GUILayout.Button("Quit", _button, GUILayout.Height(48f))) session.QuitGame();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Developer controls for verifying game-state rules in the Editor before the roulette and
        /// economy phases exist. Only available in the Editor and development builds.
        /// </summary>
        private void DrawDebugPanel(Run run, float width)
        {
            var rect = new Rect(width - 344f, 24f, 320f, 420f);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, rect.height - 24f));
            GUILayout.Label("DEV PANEL", _title);
            GUILayout.Label("Mode: " + session.Mode + "   Seed: " + run.Random.Seed, _small);
            GUI.enabled = !run.IsOver;
            if (GUILayout.Button("+ $1,000 reward", _button)) run.Ledger.Credit(1000, TransactionKind.Reward, "Dev panel");
            if (GUILayout.Button("- $1,000 penalty", _button)) run.Ledger.ForceDebit(1000, TransactionKind.Penalty, "Dev panel");
            if (GUILayout.Button("Pay $10,000 of debt", _button))
            {
                if (run.Ledger.PayDebt(10000, "Dev panel") == 0) Debug.Log("[Dev] Nothing paid: no balance or no debt.");
            }
            if (GUILayout.Button("Forgive all debt", _button) && run.Ledger.Debt > 0) run.Ledger.ForgiveDebt(run.Ledger.Debt, "Dev panel");
            if (GUILayout.Button("Play an empty round", _button)) PlayEmptyRound(run);
            GUI.enabled = true;
            GUILayout.Space(6f);
            if (GUILayout.Button("Restart run", _button)) session.RestartRun();
            GUILayout.Label("History: " + run.Ledger.History.Count + " transactions", _small);
            GUILayout.EndArea();
        }

        /// <summary>Runs one round through every phase with no bets, as the spin system will in Phase 2.</summary>
        private static void PlayEmptyRound(Run run)
        {
            if (!run.Round.InProgress && !run.TryBeginRound()) return;
            if (run.Round.Phase == RoundPhase.Announcing) run.Round.Tick(run.Round.TimeRemaining);
            run.Round.TryCloseBettingEarly();
            run.Round.NotifySpinStarted();
            run.Round.NotifySpinFinished();
            run.Round.NotifyResolutionFinished();
            if (run.Round.Phase == RoundPhase.Summary) run.Round.Tick(run.Round.TimeRemaining);
        }

        private void DrawDim(float width, float height)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _panelTexture = SolidTexture(new Color(0.05f, 0.03f, 0.03f, 0.82f));
            _buttonTexture = SolidTexture(new Color(0.22f, 0.07f, 0.07f, 0.95f));
            _buttonHoverTexture = SolidTexture(new Color(0.38f, 0.12f, 0.10f, 0.95f));

            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = _panelTexture;
            _panel.border = new RectOffset(0, 0, 0, 0);

            _label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Gold;

            _value = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleRight };
            _value.normal.textColor = Bone;

            _title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            _title.normal.textColor = Gold;

            _small = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            _small.normal.textColor = Bone;

            _prompt = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _prompt.normal.textColor = Gold;

            _button = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            _button.normal.background = _buttonTexture;
            _button.hover.background = _buttonHoverTexture;
            _button.active.background = _buttonHoverTexture;
            _button.normal.textColor = Bone;
            _button.hover.textColor = Gold;
            _button.active.textColor = Gold;
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null) Destroy(texture);
        }
    }
}

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
        private HudSkin _skin;

        private static readonly Color Gold = HudSkin.Gold;
        private static readonly Color Bone = HudSkin.Bone;
        private static readonly Color Blood = HudSkin.Blood;

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
            if (_skin != null) _skin.Dispose();
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
            if (next == GameMode.RunEnded) _inspectionHideTime = 0f;
        }

        private void OnGUI()
        {
            if (session == null || session.Run == null) return;
            if (_skin == null) _skin = new HudSkin();

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
                case GameMode.AtTable:
                    DrawViewBar(width);
                    DrawPrompt(scale);
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
            GUI.Box(rect, GUIContent.none, _skin.Panel);
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
            GUILayout.Label(label, _skin.Label, GUILayout.Width(130f));
            _skin.Value.normal.textColor = valueColor;
            GUILayout.Label(value, _skin.Value);
            GUILayout.EndHorizontal();
        }

        private void DrawModeHint(float width, float height)
        {
            if (session.Mode != GameMode.AtTable) return;
            string hint = "Click inspect   WASD / right-drag pan   Scroll zoom   1-4 / Tab views   Esc pause";
            if (Debug.isDebugBuild) hint += "   F1 dev panel";
            GUI.Label(new Rect(24f, height - 44f, width - 48f, 28f), hint, _skin.Small);
        }

        /// <summary>Camera view tabs along the top centre; the active one is highlighted.</summary>
        private void DrawViewBar(float width)
        {
            var rig = session.CameraRig;
            if (rig == null || rig.Navigator == null) return;

            const float tabWidth = 170f;
            const float tabHeight = 34f;
            float total = rig.ViewCount * tabWidth;
            float x = width * 0.5f - total * 0.5f;
            for (int i = 0; i < rig.ViewCount; i++)
            {
                bool active = i == rig.Navigator.CurrentView;
                var rect = new Rect(x + i * tabWidth + 4f, 24f, tabWidth - 8f, tabHeight);
                GUIStyle style = active ? _skin.TabActive : _skin.Tab;
                if (GUI.Button(rect, (i + 1) + "  " + rig.GetViewName(i), style)) rig.Navigator.SelectView(i);
            }
        }

        /// <summary>Shows the prompt for the hovered object just below the cursor.</summary>
        private void DrawPrompt(float scale)
        {
            string prompt = session.Interactor != null ? session.Interactor.CurrentPrompt : null;
            if (string.IsNullOrEmpty(prompt) || session.PlayerInput == null) return;
            Vector2 pointer = session.PlayerInput.Current.PointerPosition;
            // Screen pixels (origin bottom-left) to scaled GUI space (origin top-left).
            float gx = pointer.x / scale;
            float gy = (Screen.height - pointer.y) / scale;
            var rect = new Rect(gx + 18f, gy + 18f, 420f, 32f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), prompt, _skin.PromptShadow);
            GUI.Label(rect, prompt, _skin.Prompt);
        }

        private void DrawInspection(float width, float height)
        {
            var rect = new Rect(width * 0.5f - 320f, height - 400f, 640f, 170f);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 24f, rect.y + 16f, rect.width - 48f, rect.height - 32f));
            GUILayout.Label(_inspectionTitle, _skin.Title);
            GUILayout.Label(_inspectionBody, _skin.Small);
            GUILayout.EndArea();
        }

        private void DrawPauseMenu(float width, float height)
        {
            DrawDim(width, height);
            var rect = new Rect(width * 0.5f - 200f, height * 0.5f - 150f, 400f, 300f);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 30f, rect.y + 24f, rect.width - 60f, rect.height - 48f));
            GUILayout.Label("PAUSED", _skin.Title);
            GUILayout.Space(16f);
            if (GUILayout.Button("Resume", _skin.Button, GUILayout.Height(48f))) session.TryResume();
            GUILayout.Space(8f);
            if (GUILayout.Button("Restart run", _skin.Button, GUILayout.Height(48f))) session.RestartRun();
            GUILayout.Space(8f);
            if (GUILayout.Button("Quit", _skin.Button, GUILayout.Height(48f))) session.QuitGame();
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
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 30f, rect.y + 24f, rect.width - 60f, rect.height - 48f));
            GUILayout.Label(heading, _skin.Title);
            GUILayout.Label(body, _skin.Small);
            GUILayout.Space(10f);
            GUILayout.Label("Rounds played: " + run.RoundsPlayed + "    Won: $" + run.Ledger.TotalWinnings.ToString("N0") + "    Lost: $" + run.Ledger.TotalLosses.ToString("N0"), _skin.Small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New run", _skin.Button, GUILayout.Height(48f))) session.RestartRun();
            GUILayout.Space(8f);
            if (GUILayout.Button("Quit", _skin.Button, GUILayout.Height(48f))) session.QuitGame();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Developer controls for verifying game-state rules in the Editor before the roulette and
        /// economy phases exist. Only available in the Editor and development builds.
        /// </summary>
        private void DrawDebugPanel(Run run, float width)
        {
            var rect = new Rect(width - 344f, 80f, 320f, 360f);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, rect.height - 24f));
            GUILayout.Label("DEV PANEL", _skin.Title);
            GUILayout.Label("Mode: " + session.Mode + "   Seed: " + run.Random.Seed, _skin.Small);
            GUI.enabled = !run.IsOver;
            if (GUILayout.Button("+ $1,000 reward", _skin.Button)) run.Ledger.Credit(1000, TransactionKind.Reward, "Dev panel");
            if (GUILayout.Button("- $1,000 penalty", _skin.Button)) run.Ledger.ForceDebit(1000, TransactionKind.Penalty, "Dev panel");
            if (GUILayout.Button("Pay $10,000 of debt", _skin.Button))
            {
                if (run.Ledger.PayDebt(10000, "Dev panel") == 0) Debug.Log("[Dev] Nothing paid: no balance or no debt.");
            }
            if (GUILayout.Button("Forgive all debt", _skin.Button) && run.Ledger.Debt > 0) run.Ledger.ForgiveDebt(run.Ledger.Debt, "Dev panel");
            GUI.enabled = true;
            GUILayout.Space(6f);
            if (GUILayout.Button("Restart run", _skin.Button)) session.RestartRun();
            GUILayout.Label("History: " + run.Ledger.History.Count + " transactions", _skin.Small);
            GUILayout.EndArea();
        }

        private void DrawDim(float width, float height)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}

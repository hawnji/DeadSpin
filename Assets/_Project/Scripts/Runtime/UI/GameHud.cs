using System.Collections.Generic;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Roulette;
using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>
    /// The game's whole interface, drawn with IMGUI so it needs no prefabs, fonts or packages:
    /// the title and unlock screens, the table HUD (money, debt, Influence, rules, timer, chips,
    /// abilities, the Game Master's lines), offers and events, the casino's rooms as panels, the
    /// final offer, pause and the endings. It only reads the run and calls its public API; every
    /// rule lives in the core. Split across partial files: table (this file), rooms, screens.
    /// </summary>
    public sealed partial class GameHud : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private RouletteTableController table;
        [Tooltip("Multiplies the resolution-based scale. 1 = default size.")]
        [Range(0.5f, 2f)] [SerializeField] private float uiScale = 1f;
        [Min(1f)] [SerializeField] private float resultBannerSeconds = 6f;
        [Min(1f)] [SerializeField] private float inspectionSeconds = 6f;

        private HudSkin _skin;
        private float _scale = 1f;
        private float _width;
        private float _height;

        private string _inspectionTitle;
        private string _inspectionBody;
        private float _inspectionHideTime;
        private bool _showDebug;

        public float UiScale
        {
            get { return uiScale; }
            set { uiScale = Mathf.Clamp(value, 0.5f, 2f); }
        }

        private CasinoRun Casino => session != null ? session.Casino : null;

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            if (table == null) table = FindFirstObjectByType<RouletteTableController>();
            if (session == null)
            {
                Debug.LogError("[GameHud] No GameSession in the scene.", this);
                enabled = false;
                return;
            }
            if (table == null) Debug.LogWarning("[GameHud] No RouletteTableController found: the table controls will be hidden.", this);
            if (session.Interactor != null) session.Interactor.InspectionReported += OnInspection;
            session.RunStarted += OnRunStarted;
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                if (session.Interactor != null) session.Interactor.InspectionReported -= OnInspection;
                session.RunStarted -= OnRunStarted;
            }
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

        private void OnRunStarted(CasinoRun casino)
        {
            _room = null;
            _recordShown = null;
            _roomText = null;
            _screen = HudScreen.None;
            _bids.Clear();
        }

        // ------------------------------------------------------------------ frame

        private void OnGUI()
        {
            if (session == null) return;
            if (_skin == null) _skin = new HudSkin();

            _scale = HudSkin.Scale(uiScale);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            _width = Screen.width / _scale;
            _height = Screen.height / _scale;

            switch (session.Mode)
            {
                case GameMode.Boot:
                    DrawTitle();
                    break;
                case GameMode.AtTable:
                    if (Casino != null) DrawTable();
                    break;
                case GameMode.Paused:
                    if (Casino != null) DrawStatus(Casino);
                    DrawPause();
                    break;
                case GameMode.RunEnded:
                    DrawEnding();
                    break;
            }

            GUI.matrix = previous;
        }

        private void DrawTable()
        {
            CasinoRun casino = Casino;
            DrawStatus(casino);
            DrawRoomButtons(casino);
            DrawFeed(casino);
            DrawViewBar();
            DrawPhase(casino);
            DrawRules(casino);
            if (table != null)
            {
                DrawChipPanel(casino);
                DrawAbilities(casino);
                DrawAnnouncement();
                DrawNotice();
                DrawCombinationPicker();
            }
            DrawHistory(casino);
            DrawOffers(casino);
            DrawRoom(casino);
            if (casino.PendingDecision == PendingDecision.FinalOffer) DrawFinalOffer(casino);
            if (Time.unscaledTime < _inspectionHideTime) DrawInspection();
            DrawPrompt();
            if (_showDebug) DrawDebug(casino);
        }

        // ------------------------------------------------------------------ status

        private void DrawStatus(CasinoRun casino)
        {
            var rect = new Rect(24f, 24f, 340f, 318f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            StageDefinition stage = casino.CurrentStage;
            GUILayout.Label(stage.Name.ToUpperInvariant(), _skin.Label);
            GUILayout.Label(stage.Subtitle, _skin.Small);
            GUILayout.Space(4f);
            Row("BALANCE", "$" + casino.Ledger.Balance.ToString("N0"), HudSkin.Bone);
            if (casino.Ledger.Stored > 0) Row("IN THE VAULT", "$" + casino.Ledger.Stored.ToString("N0"), HudSkin.Bone);
            Row("DEBT", "$" + casino.Ledger.Debt.ToString("N0"), HudSkin.Blood);
            Row("INFLUENCE", casino.Influence.Current.ToString(), HudSkin.Gold);
            int round = Mathf.Min(casino.CurrentRound, casino.Run.Config.TotalRounds);
            Row("ROUND", round + " / " + casino.Run.Config.TotalRounds, HudSkin.Bone);
            Row("BETS", "$" + casino.Run.EffectiveMinimumBet.ToString("N0") + " - $" + casino.Table.Modifiers.MaximumBet.ToString("N0"), HudSkin.Bone);
            if (casino.Table.Modifiers.MandatoryWager > 0) Row("MUST WAGER", "$" + casino.Table.Modifiers.MandatoryWager.ToString("N0"), HudSkin.Blood);

            var next = casino.Payments.Next;
            if (next != null)
            {
                long owed = casino.Payments.OutstandingFor(next);
                if (owed > 0) Row("DUE R" + next.DueRound, "$" + owed.ToString("N0"), casino.Payments.MissedCount > 0 ? HudSkin.Blood : HudSkin.Bone);
            }
            GUILayout.Label("He seems " + casino.Mood.ToString().ToLowerInvariant() + ".", _skin.Small);
            GUILayout.EndArea();
        }

        private void Row(string label, string value, Color valueColor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _skin.Label, GUILayout.Width(150f));
            _skin.Value.normal.textColor = valueColor;
            GUILayout.Label(value, _skin.Value);
            _skin.Value.normal.textColor = HudSkin.Bone;
            GUILayout.EndHorizontal();
        }

        private void DrawFeed(CasinoRun casino)
        {
            float y = 600f;
            List<Core.Economy.FinancialWarning> warnings = casino.Warnings();
            foreach (var warning in warnings)
            {
                Color color = warning.Level >= Core.Economy.WarningLevel.Danger ? HudSkin.Blood : HudSkin.Gold;
                var rect = new Rect(24f, y, 340f, 0f);
                rect.height = _skin.Small.CalcHeight(new GUIContent(warning.Text), rect.width - 16f) + 8f;
                HudSkin.DrawSolid(rect, new Color(color.r * 0.3f, color.g * 0.1f, color.b * 0.1f, 0.85f));
                GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, rect.height), warning.Text, _skin.Small);
                y += rect.height + 4f;
                if (y > _height - 300f) return;
            }

            if (table == null) return;
            var feed = table.Feed;
            for (int i = feed.Count - 1; i >= 0 && y < _height - 300f; i--)
            {
                float age = Time.unscaledTime - feed[i].Time;
                if (age > 20f) continue;
                float alpha = Mathf.Clamp01(2f - age / 10f);
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                var rect = new Rect(24f, y, 340f, 0f);
                rect.height = _skin.Small.CalcHeight(new GUIContent(feed[i].Text), rect.width - 16f) + 6f;
                GUI.Box(rect, GUIContent.none, _skin.Panel);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height), feed[i].Text, _skin.Small);
                GUI.color = previous;
                y += rect.height + 4f;
            }
        }

        // ------------------------------------------------------------------ phase, result and timer

        private void DrawPhase(CasinoRun casino)
        {
            var area = new Rect(_width * 0.5f - 320f, 70f, 640f, 54f);
            RoundPhase phase = casino.Run.Round.Phase;
            string text = null;
            Color color = HudSkin.Gold;
            switch (phase)
            {
                case RoundPhase.Idle:
                    if (casino.CurrentFinalPhase != FinalPhase.None) text = FinalPhaseName(casino.CurrentFinalPhase);
                    break;
                case RoundPhase.Announcing: text = "ROUND " + casino.CurrentRound; break;
                case RoundPhase.Betting:
                    text = casino.Table.Modifiers.SilentSpin ? "PLACE YOUR BETS   ..." : "PLACE YOUR BETS   " + Mathf.CeilToInt(casino.Run.Round.TimeRemaining);
                    if (!casino.Table.Modifiers.SilentSpin && casino.Run.Round.TimeRemaining < 6f) color = HudSkin.Blood;
                    break;
                case RoundPhase.BettingClosed:
                case RoundPhase.Spinning: text = casino.IsRespinning ? "THE BALL GOES AGAIN" : "NO MORE BETS"; color = HudSkin.Bone; break;
            }

            RoundResult result = table != null ? table.LastResult : casino.LastResult;
            float resultAge = table != null ? Time.unscaledTime - table.LastResultTime : 0f;
            if (result != null && resultAge < resultBannerSeconds && (phase == RoundPhase.Summary || phase == RoundPhase.Idle || phase == RoundPhase.Resolving))
            {
                DrawResult(casino, result, area);
                return;
            }

            if (text == null) return;
            _skin.Banner.normal.textColor = color;
            GUI.Label(area, text, _skin.Banner);
            _skin.Banner.normal.textColor = HudSkin.Gold;
        }

        private void DrawResult(CasinoRun casino, RoundResult result, Rect area)
        {
            float x = _width * 0.5f - 260f;
            for (int i = 0; i < result.Pockets.Count; i++)
            {
                Pocket pocket = result.Pockets[i];
                var badge = new Rect(x, area.y, 64f, 54f);
                HudSkin.DrawSolid(badge, PocketColour(pocket.Color));
                GUI.Label(badge, pocket.Number.ToString(), _skin.Banner);
                x += 70f;
            }
            string outcome;
            Color color;
            if (result.TotalStaked == 0) { outcome = "No bets"; color = HudSkin.Bone; }
            else if (result.Net > 0) { outcome = "+$" + result.Net.ToString("N0"); color = HudSkin.Win; }
            else if (result.Net == 0) { outcome = "Even"; color = HudSkin.Bone; }
            else { outcome = "-$" + (-result.Net).ToString("N0"); color = HudSkin.Blood; }
            _skin.Banner.normal.textColor = color;
            GUI.Label(new Rect(x + 10f, area.y, 380f, 54f), outcome, _skin.Banner);
            _skin.Banner.normal.textColor = HudSkin.Gold;

            float y = area.yMax + 6f;
            foreach (var special in casino.LastSpecialOutcomes)
            {
                var rect = new Rect(_width * 0.5f - 320f, y, 640f, 30f);
                HudSkin.DrawSolid(rect, new Color(0.2f, 0.05f, 0.25f, 0.85f));
                GUI.Label(rect, special.Definition.Name.ToUpperInvariant() + ": " + special.Message, _skin.SmallCentered);
                y += 34f;
            }
            foreach (string loophole in result.LoopholesExploited)
            {
                var rect = new Rect(_width * 0.5f - 320f, y, 640f, 30f);
                HudSkin.DrawSolid(rect, new Color(0.1f, 0.25f, 0.1f, 0.85f));
                GUI.Label(rect, "LOOPHOLE: you slipped past " + loophole.Replace('_', ' ') + ".", _skin.SmallCentered);
                y += 34f;
            }
        }

        private static string FinalPhaseName(FinalPhase phase)
        {
            switch (phase)
            {
                case FinalPhase.HisHouse: return "THE FINAL TABLE: HIS HOUSE";
                case FinalPhase.HisGame: return "THE FINAL TABLE: HIS GAME";
                case FinalPhase.HisFinalOffer: return "HIS FINAL OFFER";
                case FinalPhase.TheLastSpin: return "THE LAST SPIN";
                default: return null;
            }
        }

        public static Color PocketColour(PocketColor color)
        {
            return color == PocketColor.Red ? HudSkin.PocketRed : color == PocketColor.Black ? HudSkin.PocketBlack : HudSkin.PocketGreen;
        }

        // ------------------------------------------------------------------ rules, events, contracts

        private void DrawRules(CasinoRun casino)
        {
            float w = 400f;
            var rect = new Rect(_width - w - 24f, 70f, w, 0f);
            var lines = new List<KeyValuePair<string, Color>>();
            int hidden = 0;
            bool reveal = casino.Insight.RevealWheel || casino.Insight.AuditPayouts;
            foreach (Core.Rules.RuleInstance rule in casino.Rules.Active)
            {
                if (!rule.IsVisible && !reveal) { hidden++; continue; }
                string length = rule.IsPermanentLength ? "permanent" : rule.RoundsRemaining + " round" + (rule.RoundsRemaining == 1 ? "" : "s");
                string text = (rule.IsVisible ? "" : "[hidden] ") + rule.Definition.Name + "  (" + length + ")\n" + rule.Definition.Description;
                lines.Add(new KeyValuePair<string, Color>(text, rule.Definition.Negative ? HudSkin.Bone : HudSkin.Win));
            }
            if (hidden > 0) lines.Add(new KeyValuePair<string, Color>("Something else is different tonight. (" + hidden + " hidden)", HudSkin.Blood));
            foreach (var ev in casino.Events.Active)
            {
                lines.Add(new KeyValuePair<string, Color>("EVENT: " + ev.Definition.Name + "\n" + ev.Definition.Description, HudSkin.Gold));
            }
            foreach (var contract in casino.Contracts.Active)
            {
                lines.Add(new KeyValuePair<string, Color>("CONTRACT: " + contract.Definition.Name + "\n" + contract.Definition.Terms(casino), HudSkin.Gold));
            }
            var challenge = casino.Challenges.Active;
            if (challenge != null)
            {
                lines.Add(new KeyValuePair<string, Color>("CHALLENGE: " + challenge.Definition.Name + "  (" + challenge.RoundsLeft + " left)\n" + challenge.Definition.Describe(casino), HudSkin.Gold));
            }
            if (!string.IsNullOrEmpty(casino.Insight.PeekedIntervention))
            {
                lines.Add(new KeyValuePair<string, Color>("RULE PEEK: " + casino.Insight.PeekedIntervention, HudSkin.Win));
            }
            if (casino.Insight.RevealWheel) lines.Add(new KeyValuePair<string, Color>(DescribeWheel(casino.Table.Wheel), HudSkin.Win));
            if (lines.Count == 0) return;

            float height = 40f;
            foreach (var line in lines) height += _skin.Small.CalcHeight(new GUIContent(line.Key), w - 32f) + 8f;
            rect.height = Mathf.Min(height, _height * 0.5f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            GUILayout.Label("HOUSE RULES", _skin.Label);
            foreach (var line in lines)
            {
                _skin.Small.normal.textColor = line.Value;
                GUILayout.Label(line.Key, _skin.Small);
                GUILayout.Space(4f);
            }
            _skin.Small.normal.textColor = HudSkin.Bone;
            GUILayout.EndArea();
            _rulesBottom = rect.yMax;
        }

        private float _rulesBottom = 70f;

        private static string DescribeWheel(WheelConfiguration wheel)
        {
            string text = "WHEEL: " + wheel.PocketCount + " pockets";
            var specials = new List<string>();
            foreach (Pocket pocket in wheel.Pockets)
            {
                if (pocket.Special != SpecialPocketType.None) specials.Add(pocket.Number + " " + pocket.Special);
            }
            if (specials.Count > 0) text += "\nSpecial: " + string.Join(", ", specials.ToArray());
            if (wheel.Ring != null) text += "\nOuter ring: " + wheel.Ring.Segments.Count + " segments";
            return text;
        }

        // ------------------------------------------------------------------ chips

        private static readonly ChipKind[] Kinds = { ChipKind.Standard, ChipKind.Magnetic, ChipKind.Sticky, ChipKind.Weighted, ChipKind.Fragile, ChipKind.Protected };

        private void DrawChipPanel(CasinoRun casino)
        {
            var rect = new Rect(24f, _height - 290f, 380f, 240f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));

            GUILayout.Label("CHIP   (Q / E)", _skin.Label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < RouletteTableController.ChipValues.Length; i++)
            {
                bool selected = i == table.SelectedChipIndex;
                if (GUILayout.Button("$" + RouletteTableController.ChipValues[i], selected ? _skin.TabActive : _skin.Tab, GUILayout.Height(34f))) table.SelectChip(i);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            foreach (ChipKind kind in Kinds)
            {
                int count = kind == ChipKind.Standard ? -1 : casino.Inventory.ChipsOf(kind);
                if (count == 0) continue;
                string label = kind == ChipKind.Standard ? "Plain" : kind + " x" + count;
                if (GUILayout.Button(label, table.SelectedKind == kind ? _skin.TabActive : _skin.Tab, GUILayout.Height(28f))) table.SelectKind(kind);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ON THE TABLE", _skin.Label, GUILayout.Width(150f));
            GUILayout.Label("$" + casino.Table.Bets.Total.ToString("N0"), _skin.Value);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string action = table.PrimaryActionLabel;
            GUI.enabled = action != null;
            if (GUILayout.Button((action ?? "...") + "   [Space]", _skin.Button, GUILayout.Height(40f))) table.PrimaryAction();
            GUI.enabled = casino.Run.Round.IsBettingOpen && casino.Table.Bets.Count > 0;
            if (GUILayout.Button("Clear", _skin.Button, GUILayout.Width(80f), GUILayout.Height(40f))) table.ClearBets();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (casino.Run.Round.Phase == RoundPhase.Idle && casino.PendingDecision == PendingDecision.None) DrawPrediction(casino);
            GUILayout.EndArea();
        }

        private static readonly Core.Rules.RuleCategory[] PredictCategories =
        {
            Core.Rules.RuleCategory.Wheel, Core.Rules.RuleCategory.Payout, Core.Rules.RuleCategory.Betting, Core.Rules.RuleCategory.Table, Core.Rules.RuleCategory.Economy
        };

        private void DrawPrediction(CasinoRun casino)
        {
            GUILayout.Label(casino.HasPrediction ? "Prediction made. Change it?" : "Predict his next move (+Influence):", _skin.Small);
            GUILayout.BeginHorizontal();
            foreach (var category in PredictCategories)
            {
                if (GUILayout.Button(category.ToString(), _skin.Tab, GUILayout.Height(24f))) { casino.PredictIntervention(category); table.ShowNotice("You predict a " + category.ToString().ToLowerInvariant() + " rule."); }
            }
            if (GUILayout.Button("None", _skin.Tab, GUILayout.Height(24f))) { casino.PredictIntervention(null); table.ShowNotice("You predict he does nothing."); }
            GUILayout.EndHorizontal();
        }

        private void DrawCombinationPicker()
        {
            if (!table.CombinationPickerOpen) return;
            var rect = new Rect(_width * 0.5f - 260f, _height * 0.5f - 70f, 520f, 140f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            GUILayout.Label("COMBINATION BET", _skin.Label);
            GUILayout.BeginHorizontal();
            for (int group = 0; group < 4; group++)
            {
                if (GUILayout.Button(Core.Betting.BetSelection.CombinationName(group), _skin.Button, GUILayout.Height(40f)))
                {
                    table.CombinationPickerOpen = false;
                    table.PlaceCombination(group);
                }
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Cancel", _skin.Tab)) table.CombinationPickerOpen = false;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ abilities

        private void DrawAbilities(CasinoRun casino)
        {
            var owned = new List<Core.Specials.AbilityDefinition>();
            foreach (var ability in casino.Abilities.All) if (casino.Abilities.Owns(ability, casino.Inventory)) owned.Add(ability);
            if (owned.Count == 0) return;

            const float w = 150f;
            const float h = 62f;
            float total = owned.Count * (w + 6f);
            float x = Mathf.Max(420f, _width * 0.5f - total * 0.5f);
            float y = _height - 96f;
            Rect bar = new Rect(x - 6f, y - 6f, total + 6f, h + 12f);
            Panel(bar);
            string hover = null;
            for (int i = 0; i < owned.Count; i++)
            {
                var ability = owned[i];
                var rect = new Rect(x + i * (w + 6f), y, w, h);
                string problem = casino.Abilities.Check(ability, casino, ability.Target == Core.Specials.AbilityTarget.Number ? 1 : -1, null);
                int cost = Core.Specials.AbilitySystem.CostOf(ability, casino);
                string costText = ability.Kind == Core.Specials.AbilityKind.Consumable ? "x" + casino.Inventory.ChargesOf(ability.Id) : cost + " Inf";
                bool targeting = table.PendingAbility == ability.Id;
                GUI.enabled = problem == null || targeting;
                GUIStyle style = targeting ? _skin.TabActive : _skin.Button;
                if (GUI.Button(rect, new GUIContent(ability.Name + "\n" + costText, ability.Description), style))
                {
                    if (targeting) table.CancelTargeting();
                    else if (Event.current.button == 1 && ability.Target == Core.Specials.AbilityTarget.Bet) table.UseAbilityOnChosenBet(ability.Id);
                    else table.UseAbility(ability.Id);
                }
                GUI.enabled = true;
                if (rect.Contains(Event.current.mousePosition)) hover = ability.Name + ": " + ability.Description + (problem != null ? "\n" + problem : "") +
                    (ability.Target == Core.Specials.AbilityTarget.Bet ? "\nRight-click to choose the bet (default: your largest)." : "");
            }
            if (hover != null)
            {
                var tip = new Rect(bar.x, bar.y - 80f, Mathf.Max(bar.width, 520f), 74f);
                HudSkin.DrawSolid(tip, new Color(0f, 0f, 0f, 0.85f));
                GUI.Label(new Rect(tip.x + 8f, tip.y + 4f, tip.width - 16f, tip.height - 8f), hover, _skin.Small);
            }
        }

        // ------------------------------------------------------------------ the Game Master's line, notices, history

        private void DrawAnnouncement()
        {
            if (string.IsNullOrEmpty(table.Announcement)) return;
            float age = Time.unscaledTime - table.AnnouncementTime;
            if (age > table.AnnouncementSeconds + 1f) return;
            float alpha = Mathf.Clamp01(Mathf.Min(age / 0.25f, table.AnnouncementSeconds + 1f - age));
            var content = new GUIContent("“" + table.Announcement + "”");
            float w = 900f;
            float h = _skin.SmallCentered.CalcHeight(content, w - 24f) + 16f;
            var rect = new Rect(_width * 0.5f - w * 0.5f, _height - 150f - h, w, h);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            _skin.SmallCentered.normal.textColor = MoodColour(table.AnnouncementMood);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f), content, _skin.SmallCentered);
            _skin.SmallCentered.normal.textColor = HudSkin.Bone;
            GUI.color = previous;
        }

        private static Color MoodColour(GameMasterMood mood)
        {
            switch (mood)
            {
                case GameMasterMood.Amused: return HudSkin.Gold;
                case GameMasterMood.Irritated: return new Color(0.95f, 0.6f, 0.4f);
                case GameMasterMood.Suspicious: return new Color(0.7f, 0.75f, 0.95f);
                case GameMasterMood.Angry: return HudSkin.Blood;
                case GameMasterMood.Desperate: return new Color(0.85f, 0.4f, 0.85f);
                default: return HudSkin.Bone;
            }
        }

        private void DrawNotice()
        {
            if (string.IsNullOrEmpty(table.Notice) || Time.unscaledTime - table.NoticeTime > 3f) return;
            var rect = new Rect(_width * 0.5f - 320f, 140f, 640f, 36f);
            HudSkin.DrawSolid(rect, new Color(0.25f, 0.04f, 0.04f, 0.9f));
            GUI.Label(rect, table.Notice, _skin.SmallCentered);
        }

        private void DrawHistory(CasinoRun casino)
        {
            int count = Mathf.Min(casino.Table.History.Count, 12);
            if (count == 0) return;
            const float size = 34f;
            float x = _width - 24f - count * (size + 4f);
            float y = _height - 180f;
            GUI.Label(new Rect(x, y - 26f, 300f, 24f), "LAST NUMBERS", _skin.Label);
            WheelConfiguration wheel = casino.Table.Wheel;
            for (int i = 0; i < count; i++)
            {
                int number = casino.Table.History[i];
                PocketColor color = StandardWheels.TraditionalColor(number);
                foreach (Pocket pocket in wheel.Pockets) if (pocket.Number == number) { color = pocket.Color; break; }
                var cell = new Rect(x + i * (size + 4f), y, size, size);
                HudSkin.DrawSolid(cell, PocketColour(color));
                GUI.Label(cell, number.ToString(), _skin.Badge);
            }
        }

        // ------------------------------------------------------------------ camera tabs, prompt, inspection

        private void DrawViewBar()
        {
            var rig = session.CameraRig;
            if (rig == null || rig.Navigator == null) return;
            const float tabWidth = 150f;
            const float tabHeight = 34f;
            float total = rig.ViewCount * tabWidth;
            float x = _width * 0.5f - total * 0.5f;
            UiPointerBlocker.Block(new Rect(x, 24f, total, tabHeight), _scale);
            for (int i = 0; i < rig.ViewCount; i++)
            {
                bool active = i == rig.Navigator.CurrentView;
                var rect = new Rect(x + i * tabWidth + 4f, 24f, tabWidth - 8f, tabHeight);
                if (GUI.Button(rect, (i + 1) + "  " + rig.GetViewName(i), active ? _skin.TabActive : _skin.Tab)) rig.Navigator.SelectView(i);
            }
        }

        private void DrawPrompt()
        {
            string prompt = session.Interactor != null ? session.Interactor.CurrentPrompt : null;
            if (string.IsNullOrEmpty(prompt) || session.PlayerInput == null) return;
            Vector2 gui = HudSkin.PointerToGui(session.PlayerInput.Current.PointerPosition, _scale);
            var content = new GUIContent(prompt);
            Vector2 size = _skin.Prompt.CalcSize(content);
            float x = Mathf.Min(gui.x + 18f, _width - size.x - 12f);
            float y = Mathf.Min(gui.y + 18f, _height - size.y - 12f);
            var rect = new Rect(x, y, size.x + 8f, size.y + 4f);
            HudSkin.DrawSolid(new Rect(rect.x - 4f, rect.y - 2f, rect.width + 4f, rect.height), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(rect, content, _skin.Prompt);
        }

        private void DrawInspection()
        {
            var rect = new Rect(_width * 0.5f - 320f, _height - 430f, 640f, 170f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            GUILayout.Label(_inspectionTitle, _skin.Title);
            GUILayout.Label(_inspectionBody, _skin.Small);
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Draws a panel and stops its clicks from reaching the table.</summary>
        private void Panel(Rect rect)
        {
            UiPointerBlocker.Block(rect, _scale);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
        }

        private static Rect Inset(Rect rect, float padding = 14f)
        {
            return new Rect(rect.x + padding, rect.y + padding * 0.7f, rect.width - padding * 2f, rect.height - padding * 1.4f);
        }

        private static string Money(long amount) { return "$" + amount.ToString("N0"); }
    }
}

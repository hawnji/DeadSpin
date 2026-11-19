using DeadSpin.Core.GameState;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Roulette;
using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>
    /// Table HUD (IMGUI prototype): round phase and countdown, the Game Master's line, chip
    /// selection, money on the table, the last result and recent numbers, and paying the debt
    /// between rounds. The run panel, pause and end screens stay in <see cref="PrototypeHud"/>.
    /// </summary>
    public sealed class TableHud : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private RouletteTableController table;
        [Range(0.5f, 2f)] [SerializeField] private float uiScale = 1f;
        [Min(1f)] [SerializeField] private float resultBannerSeconds = 5f;

        private HudSkin _skin;
        private RoundResult _shownResult;
        private float _resultTime = -100f;
        private float _scale = 1f;

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            if (table == null) table = FindFirstObjectByType<RouletteTableController>();
            if (session == null || table == null)
            {
                Debug.LogError("[TableHud] Needs a GameSession and a RouletteTableController.", this);
                enabled = false;
                return;
            }
            table.RoundResolved += OnRoundResolved;
        }

        private void OnDestroy()
        {
            if (table != null) table.RoundResolved -= OnRoundResolved;
            if (_skin != null) _skin.Dispose();
        }

        private void OnRoundResolved(RoundResult result)
        {
            _shownResult = result;
            _resultTime = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (session == null || table == null || table.Game == null) return;
            if (session.Mode != GameMode.AtTable) return;
            if (_skin == null) _skin = new HudSkin();

            float scale = HudSkin.Scale(uiScale);
            _scale = scale;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            TableGame game = table.Game;
            DrawPhase(game, width);
            DrawAnnouncement(width, height);
            DrawChipPanel(game, height);
            DrawHistory(game, width, height);
            if (game.Run.Round.Phase == RoundPhase.Idle) DrawDebtPanel(game, width);
            DrawNotice(width, height);

            GUI.matrix = previous;
        }

        private void DrawPhase(TableGame game, float width)
        {
            var area = new Rect(width * 0.5f - 300f, 70f, 600f, 54f);
            RoundPhase phase = game.Run.Round.Phase;
            string text;
            Color color = HudSkin.Gold;
            switch (phase)
            {
                case RoundPhase.Announcing: text = "ROUND " + game.Run.CurrentRoundNumber; break;
                case RoundPhase.Betting: text = "PLACE YOUR BETS   " + Mathf.CeilToInt(game.Run.Round.TimeRemaining); break;
                case RoundPhase.BettingClosed:
                case RoundPhase.Spinning: text = "NO MORE BETS"; color = HudSkin.Bone; break;
                default: text = null; break;
            }

            bool showResult = _shownResult != null && Time.unscaledTime - _resultTime < resultBannerSeconds;
            if (showResult && (phase == RoundPhase.Summary || phase == RoundPhase.Idle))
            {
                Pocket pocket = _shownResult.Pocket;
                Color pocketColor = pocket.Color == PocketColor.Red ? HudSkin.PocketRed : pocket.Color == PocketColor.Black ? HudSkin.PocketBlack : HudSkin.PocketGreen;
                var badge = new Rect(width * 0.5f - 230f, area.y, 64f, 54f);
                HudSkin.DrawSolid(badge, pocketColor);
                GUI.Label(badge, pocket.Number.ToString(), _skin.Banner);
                string outcome;
                if (_shownResult.TotalStaked == 0) { outcome = "No bets"; color = HudSkin.Bone; }
                else if (_shownResult.Net > 0) { outcome = "+$" + _shownResult.Net.ToString("N0"); color = HudSkin.Win; }
                else if (_shownResult.Net == 0) { outcome = "Even"; color = HudSkin.Bone; }
                else { outcome = "-$" + (-_shownResult.Net).ToString("N0"); color = HudSkin.Blood; }
                _skin.Banner.normal.textColor = color;
                GUI.Label(new Rect(badge.xMax + 10f, area.y, 380f, 54f), outcome, _skin.Banner);
                _skin.Banner.normal.textColor = HudSkin.Gold;
                return;
            }

            if (text == null) return;
            _skin.Banner.normal.textColor = color;
            GUI.Label(area, text, _skin.Banner);
            _skin.Banner.normal.textColor = HudSkin.Gold;
        }

        private void DrawAnnouncement(float width, float height)
        {
            if (string.IsNullOrEmpty(table.Announcement)) return;
            float age = Time.unscaledTime - table.AnnouncementTime;
            if (age > table.AnnouncementSeconds + 1f) return;
            float alpha = Mathf.Clamp01(Mathf.Min(age / 0.25f, (table.AnnouncementSeconds + 1f - age)));
            var rect = new Rect(width * 0.5f - 420f, height - 150f, 840f, 44f);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUI.Label(rect, "“" + table.Announcement + "”", _skin.SmallCentered);
            GUI.color = previous;
        }

        private void DrawChipPanel(TableGame game, float height)
        {
            var rect = new Rect(24f, height - 250f, 360f, 190f);
            UiPointerBlocker.Block(rect, _scale);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 10f, rect.width - 32f, rect.height - 20f));

            GUILayout.Label("CHIP   (Q / E)", _skin.Label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < RouletteTableController.ChipValues.Length; i++)
            {
                bool selected = i == table.SelectedChipIndex;
                if (GUILayout.Button("$" + RouletteTableController.ChipValues[i], selected ? _skin.TabActive : _skin.Tab, GUILayout.Height(36f)))
                {
                    table.SelectChip(i);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ON THE TABLE", _skin.Label, GUILayout.Width(150f));
            GUILayout.Label("$" + game.Bets.Total.ToString("N0"), _skin.Value);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string action = table.PrimaryActionLabel;
            GUI.enabled = action != null;
            if (GUILayout.Button((action ?? "Spinning...") + "   [Space]", _skin.Button, GUILayout.Height(40f))) table.PrimaryAction();
            GUI.enabled = game.Run.Round.IsBettingOpen && game.Bets.Count > 0;
            if (GUILayout.Button("Clear", _skin.Button, GUILayout.Width(80f), GUILayout.Height(40f))) table.ClearBets();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawHistory(TableGame game, float width, float height)
        {
            int count = Mathf.Min(game.History.Count, 12);
            if (count == 0) return;
            const float size = 34f;
            float x = width - 24f - count * (size + 4f);
            float y = height - 96f;
            GUI.Label(new Rect(x, y - 26f, 300f, 24f), "LAST NUMBERS", _skin.Label);
            for (int i = 0; i < count; i++)
            {
                int number = game.History[i];
                PocketColor color = StandardWheels.TraditionalColor(number);
                Color fill = color == PocketColor.Red ? HudSkin.PocketRed : color == PocketColor.Black ? HudSkin.PocketBlack : HudSkin.PocketGreen;
                var cell = new Rect(x + i * (size + 4f), y, size, size);
                HudSkin.DrawSolid(cell, fill);
                GUI.Label(cell, number.ToString(), _skin.Badge);
            }
        }

        private void DrawDebtPanel(TableGame game, float width)
        {
            if (game.Run.IsOver || game.Run.Ledger.Debt <= 0) return;
            var rect = new Rect(width - 344f, 460f, 320f, 120f);
            UiPointerBlocker.Block(rect, _scale);
            GUI.Box(rect, GUIContent.none, _skin.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 10f, rect.width - 32f, rect.height - 20f));
            GUILayout.Label("PAY THE HOUSE", _skin.Label);
            GUILayout.BeginHorizontal();
            long balance = game.Run.Ledger.Balance;
            GUI.enabled = balance >= 1000;
            if (GUILayout.Button("$1,000", _skin.Button, GUILayout.Height(40f))) table.PayDebt(1000);
            GUI.enabled = balance > 0;
            if (GUILayout.Button("All I can", _skin.Button, GUILayout.Height(40f))) table.PayDebt(balance);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawNotice(float width, float height)
        {
            if (string.IsNullOrEmpty(table.Notice) || Time.unscaledTime - table.NoticeTime > 2.5f) return;
            var rect = new Rect(width * 0.5f - 260f, height - 200f, 520f, 36f);
            HudSkin.DrawSolid(rect, new Color(0.25f, 0.04f, 0.04f, 0.9f));
            GUI.Label(rect, table.Notice, _skin.SmallCentered);
        }
    }
}

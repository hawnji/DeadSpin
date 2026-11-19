using DeadSpin.Core.Campaign;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Rules;
using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>Full-screen pages: title, unlocks, pause, the ending, and the developer panel (F1).</summary>
    public sealed partial class GameHud
    {
        private enum HudScreen
        {
            None,
            Unlocks
        }

        private HudScreen _screen = HudScreen.None;
        private Vector2 _unlockScroll;
        private Vector2 _debugScroll;
        private string _unlockMessage;

        // ------------------------------------------------------------------ title

        private void DrawTitle()
        {
            HudSkin.DrawSolid(new Rect(0f, 0f, _width, _height), new Color(0f, 0f, 0f, 0.55f));
            if (_screen == HudScreen.Unlocks) { DrawUnlocks(); return; }

            var rect = new Rect(_width * 0.5f - 300f, _height * 0.5f - 300f, 600f, 600f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect, 36f));
            _skin.Banner.fontSize = 64;
            GUILayout.Label("DEAD SPIN", _skin.Banner, GUILayout.Height(90f));
            _skin.Banner.fontSize = 34;
            GUILayout.Label("You owe the house. The house sets the wheel.", _skin.SmallCentered);
            GUILayout.Space(24f);

            if (GUILayout.Button("New run", _skin.Button, GUILayout.Height(54f))) { session.NewRun(); }
            GUI.enabled = session.HasSavedRun;
            if (GUILayout.Button("Continue", _skin.Button, GUILayout.Height(54f))) { session.ContinueRun(); }
            GUI.enabled = true;
            if (GUILayout.Button("Unlocks  (" + session.Meta.Marks + " Marks)", _skin.Button, GUILayout.Height(54f))) { _screen = HudScreen.Unlocks; _unlockMessage = null; }
            if (GUILayout.Button("Quit", _skin.Button, GUILayout.Height(54f))) session.QuitGame();

            GUILayout.Space(16f);
            MetaProgress meta = session.Meta;
            GUILayout.Label("Runs: " + meta.RunsStarted + "   Finished: " + meta.RunsFinished + "   Best round: " + meta.BestRound, _skin.SmallCentered);
            GUILayout.Label("Endings found: " + meta.Endings.Count + " of " + EndingCatalog.CreateDefault().All.Count, _skin.SmallCentered);
            if (!string.IsNullOrEmpty(session.SaveError))
            {
                _skin.SmallCentered.normal.textColor = HudSkin.Blood;
                GUILayout.Label(session.SaveError, _skin.SmallCentered);
                _skin.SmallCentered.normal.textColor = HudSkin.Bone;
            }
            GUILayout.Label("All money in Dead Spin is fictional. There is no real-money gambling.", _skin.SmallCentered);
            GUILayout.EndArea();
        }

        private void DrawUnlocks()
        {
            var rect = new Rect(_width * 0.5f - 460f, 60f, 920f, _height - 120f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect, 28f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("UNLOCKS", _skin.Title);
            GUILayout.FlexibleSpace();
            GUILayout.Label(session.Meta.Marks + " Marks", _skin.Title);
            GUILayout.EndHorizontal();
            GUILayout.Label("Marks are earned at the end of every run. Unlocks are permanent and change future runs.", _skin.Small);
            if (!string.IsNullOrEmpty(_unlockMessage)) GUILayout.Label(_unlockMessage, _skin.Label);

            _unlockScroll = GUILayout.BeginScrollView(_unlockScroll);
            foreach (UnlockDefinition unlock in UnlockCatalog.All)
            {
                bool owned = session.Meta.IsUnlocked(unlock.Id);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(unlock.Name + "   (" + unlock.Category + ")", _skin.Label);
                string requirement = unlock.RequiresEnding != null ? "  Requires the ending \"" + unlock.RequiresEnding.Replace('_', ' ') + "\"." : "";
                GUILayout.Label(unlock.Description + requirement, _skin.Small);
                GUILayout.EndVertical();
                GUI.enabled = !owned;
                if (GUILayout.Button(owned ? "Owned" : unlock.Cost + " Marks", _skin.Button, GUILayout.Width(150f), GUILayout.Height(44f)))
                {
                    string error = session.TryUnlock(unlock.Id);
                    _unlockMessage = error ?? "Unlocked: " + unlock.Name + ".";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Back", _skin.Button, GUILayout.Height(44f))) _screen = HudScreen.None;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ pause

        private void DrawPause()
        {
            HudSkin.DrawSolid(new Rect(0f, 0f, _width, _height), new Color(0f, 0f, 0f, 0.6f));
            var rect = new Rect(_width * 0.5f - 240f, _height * 0.5f - 270f, 480f, 540f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect, 28f));
            GUILayout.Label("PAUSED", _skin.Title);
            GUILayout.Space(10f);
            if (GUILayout.Button("Resume", _skin.Button, GUILayout.Height(48f))) session.TryResume();
            if (GUILayout.Button("Save and return to title", _skin.Button, GUILayout.Height(48f))) session.ReturnToTitle();
            if (GUILayout.Button("Abandon and start a new run", _skin.Button, GUILayout.Height(48f))) session.RestartRun();
            if (GUILayout.Button("Quit", _skin.Button, GUILayout.Height(48f))) session.QuitGame();
            GUILayout.Space(12f);
            GUILayout.Label("UI scale  " + uiScale.ToString("0.00"), _skin.Label);
            UiScale = GUILayout.HorizontalSlider(uiScale, 0.5f, 2f);
            GUILayout.Label("Volume  " + Mathf.RoundToInt(AudioListener.volume * 100f) + "%", _skin.Label);
            AudioListener.volume = GUILayout.HorizontalSlider(AudioListener.volume, 0f, 1f);
            GUILayout.Space(8f);
            GUILayout.Label(Casino != null && Core.Persistence.SaveSystem.CanSave(Casino)
                ? "The run is saved between rounds."
                : "Mid-round: the run will be saved from the start of this round.", _skin.Small);
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ ending

        private void DrawEnding()
        {
            CasinoRun casino = Casino;
            HudSkin.DrawSolid(new Rect(0f, 0f, _width, _height), new Color(0f, 0f, 0f, 0.75f));
            if (_screen == HudScreen.Unlocks) { DrawUnlocks(); return; }
            var rect = new Rect(_width * 0.5f - 400f, _height * 0.5f - 320f, 800f, 640f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect, 32f));
            EndingDefinition ending = casino != null ? casino.Ending : null;
            if (ending != null)
            {
                _skin.Title.normal.textColor = ending.IsVictory ? HudSkin.Win : HudSkin.Blood;
                GUILayout.Label(ending.Title.ToUpperInvariant(), _skin.Title);
                _skin.Title.normal.textColor = HudSkin.Gold;
                GUILayout.Space(8f);
                GUILayout.Label(ending.Text, _skin.Small);
            }
            else
            {
                GUILayout.Label("THE NIGHT IS OVER", _skin.Title);
            }
            GUILayout.Space(16f);
            if (casino != null)
            {
                Row("ROUNDS PLAYED", casino.Run.RoundsPlayed.ToString(), HudSkin.Bone);
                Row("FINAL BALANCE", Money(casino.Ledger.Balance + casino.Ledger.Stored), HudSkin.Bone);
                Row("DEBT LEFT", Money(casino.Ledger.Debt), HudSkin.Blood);
                Row("DEBT PAID", Money(System.Math.Max(0, casino.StartingDebt - casino.Ledger.Debt)), HudSkin.Bone);
                Row("CHALLENGES WON", casino.Challenges.Won.ToString(), HudSkin.Bone);
                Row("SECRETS FOUND", casino.Records.SecretsFound.ToString(), HudSkin.Bone);
                Row("LOOPHOLES", casino.Profile.TotalLoopholeExploits.ToString(), HudSkin.Bone);
                Row("MARKS EARNED", "+" + casino.MarksEarned, HudSkin.Gold);
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New run", _skin.Button, GUILayout.Height(50f))) session.NewRun();
            if (GUILayout.Button("Unlocks", _skin.Button, GUILayout.Height(50f))) { _screen = HudScreen.Unlocks; _unlockMessage = null; }
            if (GUILayout.Button("Title", _skin.Button, GUILayout.Height(50f))) session.ReturnToTitle();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ developer panel

        private void DrawDebug(CasinoRun casino)
        {
            var rect = new Rect(_width - 444f, _height - 560f, 420f, 360f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            GUILayout.Label("DEV PANEL (F1)", _skin.Label);
            GUILayout.Label("Seed " + casino.Run.Random.Seed + "   GM: " + casino.Setup.Personality + "   Authority " + casino.Authority.Current + "   Stage " + casino.Stage, _skin.Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+$1,000", _skin.Tab)) casino.Ledger.Credit(1000, Core.Economy.TransactionKind.Reward, "Developer");
            if (GUILayout.Button("+3 Inf", _skin.Tab)) casino.Influence.Earn(3, "Developer");
            if (GUILayout.Button("Event", _skin.Tab)) casino.TriggerRandomEvent();
            if (GUILayout.Button("Save", _skin.Tab)) session.TrySaveRun();
            GUILayout.EndHorizontal();
            GUILayout.Label("Impose a rule:", _skin.Small);
            _debugScroll = GUILayout.BeginScrollView(_debugScroll);
            foreach (RuleDefinition rule in casino.Rules.Catalog.All)
            {
                if (GUILayout.Button(rule.Id, _skin.Tab, GUILayout.Height(24f)))
                {
                    bool ok = casino.ImposeRule(rule.Id);
                    if (table != null) table.ShowNotice(ok ? "Imposed " + rule.Name : "Could not impose " + rule.Name);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}

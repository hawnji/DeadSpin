using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Player;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Connects the run (<see cref="CasinoRun"/>) to the table's presentation: the wheel, the ball,
    /// the betting layout, the camera and the Game Master's lines. It never decides an outcome or a
    /// payout; it routes the player's clicks to the core and shows the answers. It also owns the
    /// player's table-side choices: chip value, chip kind and ability targeting.
    /// </summary>
    public sealed class RouletteTableController : MonoBehaviour
    {
        public static readonly long[] ChipValues = { 10, 25, 100, 500, 1000 };

        /// <summary>How the next click on the layout is used.</summary>
        public enum ClickMode
        {
            Bet,
            /// <summary>The click picks a number for an ability (Lucky Coin).</summary>
            AbilityNumber,
            /// <summary>The click picks a bet for an ability (Insurance, Double Down).</summary>
            AbilityBet,
            /// <summary>The click picks the anchor number for a neighbours bet.</summary>
            NeighbourAnchor
        }

        public sealed class NoticeEntry
        {
            public string Text;
            public float Time;
        }

        [SerializeField] private GameSession session;
        [SerializeField] private WheelView wheel;
        [SerializeField] private BallSpinAnimator ball;
        [SerializeField] private BettingLayoutView layout;

        [Header("Camera")]
        [Tooltip("Move the camera to the wheel when it spins and back afterwards.")]
        [SerializeField] private bool autoCamera = true;
        [SerializeField] private int bettingViewIndex = 1;
        [SerializeField] private int wheelViewIndex = 2;

        [Header("Timing")]
        [Min(0.5f)] [SerializeField] private float announcementSeconds = 4.5f;
        [Min(0f)] [SerializeField] private float spinDelaySeconds = 0.6f;
        [Min(0f)] [SerializeField] private float respinDelaySeconds = 1.6f;

        private int _chipIndex = 1;
        private int _viewBeforeSpin = -1;
        private float _spinAt = -1f;
        private string _pendingAbility;
        private readonly List<NoticeEntry> _notices = new List<NoticeEntry>();

        public CasinoRun Casino { get; private set; }
        public TableGame Game => Casino != null ? Casino.Table : null;

        public long SelectedChip => ChipValues[_chipIndex];
        public int SelectedChipIndex => _chipIndex;
        public ChipKind SelectedKind { get; private set; } = ChipKind.Standard;
        public ClickMode Mode { get; private set; } = ClickMode.Bet;
        public string PendingAbility => _pendingAbility;

        /// <summary>The Game Master's current line, shown by the HUD.</summary>
        public string Announcement { get; private set; }
        public GameMasterMood AnnouncementMood { get; private set; }
        public float AnnouncementTime { get; private set; } = -100f;
        public float AnnouncementSeconds => announcementSeconds;

        /// <summary>Short feedback about the player's last action (e.g. "Not enough money").</summary>
        public string Notice { get; private set; }
        public float NoticeTime { get; private set; } = -100f;

        /// <summary>Recent messages from the house (installments, events, rule endings). Newest last.</summary>
        public IReadOnlyList<NoticeEntry> Feed => _notices;

        /// <summary>The result of the last spin, for the HUD's banner.</summary>
        public RoundResult LastResult { get; private set; }
        public float LastResultTime { get; private set; } = -100f;

        public bool IsBallMoving => ball != null && ball.IsSpinning;

        public event Action<RoundResult> RoundResolved;
        public event Action<SpokenLine> Spoke;
        public event Action<WheelChange> WheelChanging;
        public event Action<ShakeReport> TableShaken;
        public event Action<BetSelection> BetPlaced;
        public event Action SpinStarted;

        public void SetReferences(WheelView wheelView, BallSpinAnimator animator, BettingLayoutView layoutView)
        {
            wheel = wheelView;
            ball = animator;
            layout = layoutView;
        }

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            if (session == null || wheel == null || ball == null || layout == null)
            {
                Debug.LogError("[RouletteTableController] Missing GameSession, WheelView, BallSpinAnimator or BettingLayoutView.", this);
                enabled = false;
                return;
            }

            layout.SpotClicked += OnSpotClicked;
            layout.SpecialClicked += OnSpecialClicked;
            layout.SpotDescriber = DescribeSpot;
            layout.SpecialDescriber = DescribeSpecial;
            session.RunStarted += OnRunStarted;
            if (session.Casino != null) OnRunStarted(session.Casino);
        }

        private void OnDestroy()
        {
            if (session != null) session.RunStarted -= OnRunStarted;
            if (layout != null)
            {
                layout.SpotClicked -= OnSpotClicked;
                layout.SpecialClicked -= OnSpecialClicked;
            }
            Detach();
        }

        // ------------------------------------------------------------------ run binding

        private void OnRunStarted(CasinoRun casino)
        {
            Detach();
            Casino = casino;
            Casino.Run.Round.PhaseChanged += OnPhaseChanged;
            Casino.Spoke += OnSpoke;
            Casino.Notified += OnNotified;
            Casino.InterventionAnnounced += OnIntervention;
            Casino.RespinStarted += OnRespin;
            Casino.Table.WheelChanged += OnWheelChanged;
            Casino.Table.TableShaken += OnTableShaken;

            ball.Stop();
            wheel.Build(Casino.Table.Wheel);
            ball.RestIn(-1);
            layout.Bind(Casino.Table);
            _viewBeforeSpin = -1;
            _spinAt = -1f;
            _notices.Clear();
            LastResult = null;
            CancelTargeting();
            SelectedKind = ChipKind.Standard;
            if (Casino.LastLine != null) OnSpoke(Casino.LastLine);
            else Announce("Welcome to my table. Press Space when you are ready.", GameMasterMood.Amused);
        }

        private void Detach()
        {
            if (Casino == null) return;
            Casino.Run.Round.PhaseChanged -= OnPhaseChanged;
            Casino.Spoke -= OnSpoke;
            Casino.Notified -= OnNotified;
            Casino.InterventionAnnounced -= OnIntervention;
            Casino.RespinStarted -= OnRespin;
            Casino.Table.WheelChanged -= OnWheelChanged;
            Casino.Table.TableShaken -= OnTableShaken;
            Casino = null;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (Casino == null) return;

            if (_spinAt >= 0f && Time.time >= _spinAt && !wheel.IsTransforming)
            {
                _spinAt = -1f;
                BeginSpin();
            }

            if (session.Mode != GameMode.AtTable) return;
            PlayerInputFrame input = session.PlayerInput != null ? session.PlayerInput.Current : default(PlayerInputFrame);
            if (input.ChipPreviousPressed) _chipIndex = Mathf.Max(0, _chipIndex - 1);
            if (input.ChipNextPressed) _chipIndex = Mathf.Min(ChipValues.Length - 1, _chipIndex + 1);
            if (input.ClearBetsPressed) ClearBets();
            if (input.ActionPressed) PrimaryAction();
        }

        /// <summary>Space / HUD button: start a round when idle, or close betting early.</summary>
        public void PrimaryAction()
        {
            if (Casino == null || Casino.Run.IsOver) return;
            RoundPhase phase = Casino.Run.Round.Phase;
            if (phase == RoundPhase.Idle)
            {
                CancelTargeting();
                if (!Casino.BeginRound())
                {
                    if (Casino.PendingDecision == PendingDecision.FinalOffer) ShowNotice("He is waiting for your answer.");
                    else ShowNotice("The table is not ready.");
                }
            }
            else if (phase == RoundPhase.Betting)
            {
                CancelTargeting();
                Casino.CloseBetting();
            }
        }

        public string PrimaryActionLabel
        {
            get
            {
                if (Casino == null || Casino.Run.IsOver) return null;
                if (Casino.PendingDecision != PendingDecision.None) return null;
                switch (Casino.Run.Round.Phase)
                {
                    case RoundPhase.Idle: return "Start round " + Casino.CurrentRound;
                    case RoundPhase.Betting: return Casino.Table.Bets.Count > 0 ? "No more bets" : "Spin with no bets";
                    default: return null;
                }
            }
        }

        // ------------------------------------------------------------------ chips

        public void SelectChip(int index)
        {
            _chipIndex = Mathf.Clamp(index, 0, ChipValues.Length - 1);
        }

        /// <summary>Selects the chip kind for new bets. Special kinds come from the inventory.</summary>
        public void SelectKind(ChipKind kind)
        {
            if (kind != ChipKind.Standard && (Casino == null || Casino.Inventory.ChipsOf(kind) <= 0))
            {
                ShowNotice("You have no " + kind + " chips.");
                return;
            }
            SelectedKind = kind;
        }

        public void ClearBets()
        {
            if (Casino != null && Casino.ClearBets()) ShowNotice("Your chips are back in your tray.");
        }

        // ------------------------------------------------------------------ abilities

        /// <summary>Uses an ability. Abilities that need a number (or a chosen bet) wait for a click on the layout.</summary>
        public void UseAbility(string id)
        {
            if (Casino == null) return;
            AbilityDefinition ability = Casino.Abilities.Get(id);
            if (ability == null) return;
            // Number-targeted abilities are checked against a sample number so timing and cost problems show before targeting.
            string problem = Casino.Abilities.Check(ability, Casino, ability.Target == AbilityTarget.Number ? 1 : -1, null);
            if (problem != null) { ShowNotice(problem); return; }

            if (ability.Target == AbilityTarget.Number)
            {
                _pendingAbility = id;
                Mode = ClickMode.AbilityNumber;
                ShowNotice(ability.Name + ": click a number on the layout. Right-click the button again to cancel.");
                return;
            }
            Resolve(Casino.UseAbility(id));
        }

        /// <summary>Uses a bet-targeted ability on a bet the player clicks, instead of the largest one.</summary>
        public void UseAbilityOnChosenBet(string id)
        {
            if (Casino == null) return;
            AbilityDefinition ability = Casino.Abilities.Get(id);
            if (ability == null || ability.Target != AbilityTarget.Bet) { UseAbility(id); return; }
            _pendingAbility = id;
            Mode = ClickMode.AbilityBet;
            ShowNotice(ability.Name + ": click one of your bets.");
        }

        public void CancelTargeting()
        {
            _pendingAbility = null;
            Mode = ClickMode.Bet;
        }

        private void Resolve(AbilityResult result)
        {
            if (result == null) return;
            ShowNotice(result.Message);
            if (result.Success) layout.Rebuild();
        }

        // ------------------------------------------------------------------ special bets

        /// <summary>Places a combination bet (0-3: the colour and parity groups).</summary>
        public void PlaceCombination(int group)
        {
            if (Casino == null) return;
            BetSelection selection = Casino.Table.CreateSpecialBet(BetType.Combination, group);
            if (selection == null) { ShowNotice("That bet is not available."); return; }
            Place(selection);
        }

        /// <summary>The last special box clicked that needs a choice from the HUD (the combination picker).</summary>
        public bool CombinationPickerOpen { get; set; }

        private void OnSpecialClicked(BetType type)
        {
            if (Casino == null) return;
            if (!Casino.Table.Modifiers.SpecialBetsAllowed)
            {
                ShowNotice("Special bets are not allowed at this table yet.");
                return;
            }
            switch (type)
            {
                case BetType.Neighbor:
                    Mode = ClickMode.NeighbourAnchor;
                    ShowNotice("Neighbours: click the number at the centre of your bet.");
                    return;
                case BetType.Combination:
                    CombinationPickerOpen = true;
                    return;
            }
            BetSelection selection = Casino.Table.CreateSpecialBet(type, -1);
            if (selection == null)
            {
                ShowNotice(type == BetType.Jackpot ? "There is no jackpot pocket on the wheel." : "That bet needs more spin history.");
                return;
            }
            if (RemoveHeld && Casino.Table.Bets.AmountOn(selection) > 0) { Remove(selection); return; }
            Place(selection);
        }

        // ------------------------------------------------------------------ layout clicks

        private bool RemoveHeld => session.PlayerInput != null && session.PlayerInput.Current.RemoveModifierHeld;

        private void OnSpotClicked(BetSelection selection)
        {
            if (Casino == null) return;
            switch (Mode)
            {
                case ClickMode.AbilityNumber:
                {
                    if (selection.Type != BetType.Straight) { ShowNotice("Pick a single number."); return; }
                    string id = _pendingAbility;
                    CancelTargeting();
                    Resolve(Casino.UseAbility(id, selection.Numbers[0]));
                    return;
                }
                case ClickMode.AbilityBet:
                {
                    if (Casino.Table.Bets.AmountOn(selection) <= 0) { ShowNotice("You have no chips there."); return; }
                    string id = _pendingAbility;
                    CancelTargeting();
                    Resolve(Casino.UseAbility(id, -1, selection));
                    return;
                }
                case ClickMode.NeighbourAnchor:
                {
                    CancelTargeting();
                    if (selection.Type != BetType.Straight) { ShowNotice("Pick a single number."); return; }
                    BetSelection neighbours = Casino.Table.CreateSpecialBet(BetType.Neighbor, selection.Numbers[0]);
                    if (neighbours == null) { ShowNotice("That number is not on the wheel."); return; }
                    Place(neighbours);
                    return;
                }
            }

            if (RemoveHeld) Remove(selection);
            else Place(selection);
        }

        private void Place(BetSelection selection)
        {
            ChipKind kind = SelectedKind;
            if (kind != ChipKind.Standard && Casino.Inventory.ChipsOf(kind) <= 0) kind = SelectedKind = ChipKind.Standard;
            PlacementResult result = Casino.PlaceBet(selection, SelectedChip, kind);
            if (result != PlacementResult.Accepted) { ShowNotice(Explain(result)); return; }
            if (kind != ChipKind.Standard && Casino.Inventory.ChipsOf(kind) <= 0) SelectedKind = ChipKind.Standard;
            BetPlaced?.Invoke(selection);
        }

        private void Remove(BetSelection selection)
        {
            PlacementResult result = Casino.RemoveBet(selection, SelectedChip);
            if (result != PlacementResult.Accepted) ShowNotice(Explain(result));
        }

        // ------------------------------------------------------------------ descriptions

        private string DescribeSpot(BetSelection selection)
        {
            if (Casino == null) return selection.DisplayName;
            TableGame game = Casino.Table;
            string text = selection.DisplayName + "   pays " + game.PayoutRatio(selection).ToString("0.##") + " to 1";
            if (Casino.Insight.ShowProbabilities || Casino.Insight.RevealWheel)
            {
                text += "   (" + (game.WinProbability(selection) * 100.0).ToString("0.0") + "%)";
            }
            if (game.Modifiers.IsClosed(selection)) text += "   CLOSED";
            foreach (PayoutModifier modifier in game.ModifiersFor(selection, Casino.Insight.AuditPayouts))
            {
                text += "\n" + (modifier.Hidden ? "[hidden] " : "") + modifier.Description + " (" + (modifier.Percent > 0 ? "+" : "") + modifier.Percent + "%)";
                if (Casino.Insight.AuditPayouts && !string.IsNullOrEmpty(modifier.LoopholeHint)) text += "\n   loophole: " + modifier.LoopholeHint;
            }
            long on = game.Bets.AmountOn(selection);
            if (on > 0) text += "\n$" + on.ToString("N0") + " on it";
            if (Mode == ClickMode.AbilityNumber || Mode == ClickMode.NeighbourAnchor) text += "\n(click to choose)";
            return text;
        }

        private string DescribeSpecial(BetType type)
        {
            if (Casino == null) return type.ToString();
            string text;
            switch (type)
            {
                case BetType.Neighbor: text = "Neighbours: a number and the two pockets either side of it on the wheel."; break;
                case BetType.Sequence: text = "Sequence: one above or below the last winning number."; break;
                case BetType.Repeat: text = "Repeat: the last winning number comes up again."; break;
                case BetType.Survivor: text = "Survivor: every number that has not won in the last ten spins."; break;
                case BetType.Mystery: text = "Mystery: four hidden numbers, revealed after the spin."; break;
                case BetType.Combination: text = "Combination: a colour and parity together (red odd, black even...)."; break;
                case BetType.Jackpot: text = "Jackpot: the jackpot pocket. Maximum stake $" + TableGame.JackpotMaximumStake + "."; break;
                default: text = type.ToString(); break;
            }
            if (!Casino.Table.Modifiers.SpecialBetsAllowed) text += "\nNot allowed yet.";
            else if (Casino.Table.Modifiers.ClosedBetTypes.Contains(type)) text += "\nCLOSED by the house.";
            return text;
        }

        // ------------------------------------------------------------------ round flow

        private void OnPhaseChanged(RoundPhase previous, RoundPhase next)
        {
            switch (next)
            {
                case RoundPhase.Announcing:
                    layout.HighlightNumbers(null);
                    break;
                case RoundPhase.Betting:
                    if (autoCamera && session.CameraRig != null) session.CameraRig.Navigator.SelectView(bettingViewIndex);
                    break;
                case RoundPhase.BettingClosed:
                    CancelTargeting();
                    CombinationPickerOpen = false;
                    _spinAt = Time.time + (Casino.IsRespinning ? respinDelaySeconds : spinDelaySeconds);
                    break;
                case RoundPhase.Idle:
                    if (autoCamera && _viewBeforeSpin >= 0 && session.CameraRig != null) session.CameraRig.Navigator.SelectView(_viewBeforeSpin);
                    _viewBeforeSpin = -1;
                    break;
            }
        }

        private void BeginSpin()
        {
            if (Casino == null || Casino.Run.Round.Phase != RoundPhase.BettingClosed) return;
            if (autoCamera && session.CameraRig != null)
            {
                if (_viewBeforeSpin < 0) _viewBeforeSpin = session.CameraRig.Navigator.CurrentView;
                session.CameraRig.Navigator.SelectView(wheelViewIndex);
            }
            SpinPlan plan = Casino.StartSpin();
            int visualSeed = unchecked((int)Casino.Run.Random.Seed * 31 + Casino.CurrentRound * 7 + (Casino.IsRespinning ? 3 : 0));
            ball.Play(plan, visualSeed, OnBallSettled);
            SpinStarted?.Invoke();
        }

        private void OnBallSettled()
        {
            if (Casino == null || !Casino.Table.IsSpinPending) return;
            RoundResult result = Casino.FinishSpin();
            LastResult = result;
            LastResultTime = Time.unscaledTime;
            var winners = new List<int>();
            foreach (Pocket pocket in result.Pockets) winners.Add(pocket.Number);
            layout.Rebuild();
            layout.HighlightNumbers(winners);
            RoundResolved?.Invoke(result);
        }

        private void OnRespin()
        {
            ShowNotice("The Double Spin: your bets ride again.");
        }

        private void OnWheelChanged(WheelChange change)
        {
            WheelChanging?.Invoke(change);
            wheel.AnimateChange(change, () => layout.Rebuild());
            layout.Rebuild();
        }

        private void OnTableShaken(ShakeReport report)
        {
            layout.Rebuild();
            TableShaken?.Invoke(report);
        }

        // ------------------------------------------------------------------ the Game Master's voice

        private void OnSpoke(SpokenLine line)
        {
            if (line == null) return;
            Announce(line.Text, line.Mood);
            Spoke?.Invoke(line);
        }

        private void OnIntervention(InterventionNotice notice)
        {
            if (notice.Cancelled)
            {
                AddFeed("Your Golden Ticket stopped " + notice.Rule.Name + ".");
                return;
            }
            if (!string.IsNullOrEmpty(notice.Announcement))
            {
                Announce(notice.Announcement, Casino.Mood);
                AddFeed((notice.MidBetting ? "Mid-bet rule: " : "New rule: ") + notice.Rule.Name);
            }
            else if (notice.Instance != null && notice.Instance.IsVisible)
            {
                AddFeed("New rule: " + notice.Rule.Name);
            }
            else
            {
                AddFeed("Something at the table has changed.");
            }
        }

        private void OnNotified(string text)
        {
            AddFeed(text);
        }

        private void AddFeed(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notices.Add(new NoticeEntry { Text = text, Time = Time.unscaledTime });
            while (_notices.Count > 8) _notices.RemoveAt(0);
        }

        private void Announce(string line, GameMasterMood mood)
        {
            Announcement = line;
            AnnouncementMood = mood;
            AnnouncementTime = Time.unscaledTime;
        }

        public void ShowNotice(string text)
        {
            Notice = text;
            NoticeTime = Time.unscaledTime;
        }

        private string Explain(PlacementResult result)
        {
            switch (result)
            {
                case PlacementResult.BettingClosed: return Casino.Run.Round.Phase == RoundPhase.Idle ? "Press Space to start the round first." : "Betting is closed.";
                case PlacementResult.BelowMinimum: return "The minimum on a spot is $" + Casino.Run.EffectiveMinimumBet.ToString("N0") + ".";
                case PlacementResult.AboveMaximum: return "The maximum on a spot is $" + Casino.Table.Modifiers.MaximumBet.ToString("N0") + ".";
                case PlacementResult.InsufficientFunds: return "You can't afford that chip.";
                case PlacementResult.CannotWin: return "That bet cannot win on this wheel.";
                case PlacementResult.IllegalBet: return "That is not a bet on this table.";
                case PlacementResult.NothingToRemove: return "No chips there to take back.";
                case PlacementResult.NotEligible: return "That bet's conditions are not met (or you are out of that chip).";
                case PlacementResult.SectionClosed: return "The house has closed that part of the layout.";
                case PlacementResult.ChipsStuck: return "Those chips can't be taken back.";
                case PlacementResult.InvalidAmount: return "That is not a valid amount.";
                default: return result.ToString();
            }
        }
    }
}

using System;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Player;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Connects the table's rules (<see cref="TableGame"/>) to its presentation: the wheel, the ball,
    /// the betting layout, the camera and the Game Master's announcements. It never decides an
    /// outcome or a payout; it asks the core and shows the answer.
    /// </summary>
    public sealed class RouletteTableController : MonoBehaviour
    {
        public static readonly long[] ChipValues = { 10, 25, 100, 500 };

        [SerializeField] private GameSession session;
        [SerializeField] private WheelView wheel;
        [SerializeField] private BallSpinAnimator ball;
        [SerializeField] private BettingLayoutView layout;

        [Header("Camera")]
        [Tooltip("Move the camera to the wheel when it spins and back afterwards.")]
        [SerializeField] private bool autoCamera = true;
        [SerializeField] private int bettingViewIndex = 1;
        [SerializeField] private int wheelViewIndex = 2;

        [Header("Announcements")]
        [Min(0.5f)] [SerializeField] private float announcementSeconds = 3.5f;

        private int _chipIndex = 1;
        private int _viewBeforeSpin = -1;

        public TableGame Game { get; private set; }

        public long SelectedChip => ChipValues[_chipIndex];
        public int SelectedChipIndex => _chipIndex;

        /// <summary>The Game Master's current line, shown by the HUD.</summary>
        public string Announcement { get; private set; }
        public float AnnouncementTime { get; private set; } = -100f;
        public float AnnouncementSeconds => announcementSeconds;

        /// <summary>Short feedback about the player's last action (e.g. "Not enough money").</summary>
        public string Notice { get; private set; }
        public float NoticeTime { get; private set; } = -100f;

        public event Action<RoundResult> RoundResolved;

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
            layout.SpotDescriber = DescribeSpot;
            session.RunStarted += OnRunStarted;
            OnRunStarted(session.Run);
        }

        private void OnDestroy()
        {
            if (session != null) session.RunStarted -= OnRunStarted;
            if (layout != null) layout.SpotClicked -= OnSpotClicked;
            DetachGame();
        }

        private void OnRunStarted(Run run)
        {
            DetachGame();
            Game = new TableGame(run, StandardWheels.European());
            Game.Run.Round.PhaseChanged += OnPhaseChanged;
            wheel.Build(Game.Wheel);
            ball.Stop();
            ball.RestIn(-1);
            layout.Bind(Game);
            _viewBeforeSpin = -1;
            Announce("Welcome to my table. Press Space when you are ready to play.");
        }

        private void DetachGame()
        {
            if (Game != null) Game.Run.Round.PhaseChanged -= OnPhaseChanged;
            Game = null;
        }

        private void Update()
        {
            if (Game == null || session.Mode != GameMode.AtTable) return;
            PlayerInputFrame input = session.PlayerInput.Current;

            if (input.ChipPreviousPressed) _chipIndex = Mathf.Max(0, _chipIndex - 1);
            if (input.ChipNextPressed) _chipIndex = Mathf.Min(ChipValues.Length - 1, _chipIndex + 1);
            if (input.ClearBetsPressed) ClearBets();
            if (input.ActionPressed) PrimaryAction();
        }

        /// <summary>Space / HUD button: start a round when idle, or close betting early.</summary>
        public void PrimaryAction()
        {
            if (Game == null) return;
            RoundPhase phase = Game.Run.Round.Phase;
            if (phase == RoundPhase.Idle)
            {
                if (!Game.TryBeginRound()) ShowNotice("The table is closed.");
            }
            else if (phase == RoundPhase.Betting)
            {
                Game.TryCloseBetting();
            }
        }

        public string PrimaryActionLabel
        {
            get
            {
                if (Game == null || Game.Run.IsOver) return null;
                switch (Game.Run.Round.Phase)
                {
                    case RoundPhase.Idle: return "Start round " + Game.Run.CurrentRoundNumber;
                    case RoundPhase.Betting: return Game.Bets.Count > 0 ? "No more bets" : "Spin with no bets";
                    default: return null;
                }
            }
        }

        public void SelectChip(int index)
        {
            _chipIndex = Mathf.Clamp(index, 0, ChipValues.Length - 1);
        }

        public void ClearBets()
        {
            if (Game != null && Game.ClearBets()) ShowNotice("Your chips are back in your tray.");
        }

        /// <summary>Pays toward the debt between rounds. Returns the amount paid.</summary>
        public long PayDebt(long amount)
        {
            if (Game == null || Game.Run.Round.InProgress || Game.Run.IsOver) return 0;
            long paid = Game.Run.Ledger.PayDebt(amount, "Payment to the house");
            if (paid > 0)
            {
                Announce(Game.Run.IsOver ? "Paid in full. ...Very well. The door is open." : "$" + paid.ToString("N0") + ". How responsible of you.");
            }
            return paid;
        }

        private void OnSpotClicked(BetSelection selection)
        {
            if (Game == null) return;
            bool remove = session.PlayerInput.Current.RemoveModifierHeld;
            PlacementResult result = remove ? Game.RemoveBet(selection, SelectedChip) : Game.PlaceBet(selection, SelectedChip);
            if (result != PlacementResult.Accepted) ShowNotice(Explain(result));
        }

        private string DescribeSpot(BetSelection selection)
        {
            if (Game == null) return selection.DisplayName;
            decimal ratio = Game.PayoutRatio(selection);
            double chance = Game.WinProbability(selection);
            string text = selection.DisplayName + "   pays " + ratio.ToString("0.##") + " to 1   (" + (chance * 100.0).ToString("0.0") + "%)";
            long on = Game.Bets.AmountOn(selection);
            if (on > 0) text += "   $" + on.ToString("N0") + " on it";
            return text;
        }

        private void OnPhaseChanged(RoundPhase previous, RoundPhase next)
        {
            switch (next)
            {
                case RoundPhase.Announcing:
                    Announce("Round " + Game.Run.CurrentRoundNumber + " of " + Game.Run.Config.TotalRounds + ". The house rules are unchanged... for now.");
                    break;
                case RoundPhase.Betting:
                    Announce("Place your bets.");
                    if (autoCamera) session.CameraRig.Navigator.SelectView(bettingViewIndex);
                    break;
                case RoundPhase.BettingClosed:
                    Announce("No more bets.");
                    BeginSpin();
                    break;
                case RoundPhase.Idle:
                    if (autoCamera && _viewBeforeSpin >= 0) session.CameraRig.Navigator.SelectView(_viewBeforeSpin);
                    _viewBeforeSpin = -1;
                    break;
            }
        }

        private void BeginSpin()
        {
            if (autoCamera)
            {
                _viewBeforeSpin = session.CameraRig.Navigator.CurrentView;
                session.CameraRig.Navigator.SelectView(wheelViewIndex);
            }
            int target = Game.StartSpin();
            int visualSeed = unchecked((int)Game.Run.Random.Seed * 31 + Game.Run.CurrentRoundNumber);
            ball.Play(target, visualSeed, OnBallSettled);
        }

        private void OnBallSettled()
        {
            if (Game == null || !Game.IsSpinPending) return;
            RoundResult result = Game.FinishSpin();
            Announce(DescribeResult(result));
            RoundResolved?.Invoke(result);
        }

        private static string DescribeResult(RoundResult result)
        {
            string pocket = result.Pocket.Number + " " + result.Pocket.Color.ToString().ToUpperInvariant() + ".";
            if (result.TotalStaked == 0) return pocket;
            if (result.Net > 0) return pocket + " You win $" + result.Net.ToString("N0") + ". Enjoy it while it lasts.";
            if (result.Net == 0) return pocket + " You break even.";
            return pocket + " The house thanks you for $" + (-result.Net).ToString("N0") + ".";
        }

        private void Announce(string line)
        {
            Announcement = line;
            AnnouncementTime = Time.unscaledTime;
        }

        private void ShowNotice(string text)
        {
            Notice = text;
            NoticeTime = Time.unscaledTime;
        }

        private string Explain(PlacementResult result)
        {
            switch (result)
            {
                case PlacementResult.BettingClosed: return Game.Run.Round.Phase == RoundPhase.Idle ? "Press Space to start the round first." : "Betting is closed.";
                case PlacementResult.BelowMinimum: return "The minimum on a spot is $" + Game.Run.Config.MinimumBet.ToString("N0") + ".";
                case PlacementResult.AboveMaximum: return "The maximum on a spot is $" + Game.Run.Config.MaximumBet.ToString("N0") + ".";
                case PlacementResult.InsufficientFunds: return "You can't afford that chip.";
                case PlacementResult.CannotWin: return "That bet cannot win on this wheel.";
                case PlacementResult.IllegalBet: return "That is not a bet on this table.";
                case PlacementResult.NothingToRemove: return "No chips there to take back.";
                default: return result.ToString();
            }
        }
    }
}

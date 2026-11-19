using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Campaign
{
    /// <summary>A choice the run is waiting on before the next round can begin.</summary>
    public enum PendingDecision
    {
        None,
        FinalOffer
    }

    /// <summary>The Game Master announced (or tried to announce) an intervention.</summary>
    public sealed class InterventionNotice
    {
        public RuleDefinition Rule { get; }
        public RuleInstance Instance { get; }
        /// <summary>His spoken announcement, or null for hidden and silent interventions.</summary>
        public string Announcement { get; }
        public bool MidBetting { get; }
        /// <summary>Stopped by a Golden Ticket before it took effect.</summary>
        public bool Cancelled { get; }

        public InterventionNotice(RuleDefinition rule, RuleInstance instance, string announcement, bool midBetting, bool cancelled)
        {
            Rule = rule;
            Instance = instance;
            Announcement = announcement;
            MidBetting = midBetting;
            Cancelled = cancelled;
        }
    }

    /// <summary>
    /// The round director for a full run: owns every system (table, rules, Game Master, economy,
    /// specials, progression) and moves them through each round. The Unity layer drives it with
    /// <see cref="BeginRound"/>, <see cref="Tick"/>, <see cref="StartSpin"/> and
    /// <see cref="FinishSpin"/>, routes the player's actions through it, and listens to its events.
    /// Everything here is pure C# and runs headless in tests and the balance simulator.
    /// </summary>
    public sealed class CasinoRun : IGameHost, IRuleEnvironment
    {
        private static readonly IReadOnlyList<int> NoHistory = new int[0];
        private static readonly string[] LiveTransformations = { "number_shuffle", "move_numbers" };

        private readonly SeededRandom _dialogueRandom;
        private readonly List<RuleInstance> _announcedThisRound = new List<RuleInstance>();
        private readonly List<string> _imposedHistory = new List<string>();
        private readonly Dictionary<string, KeyValuePair<WheelConfiguration, WheelConfiguration>> _permanentUndo = new Dictionary<string, KeyValuePair<WheelConfiguration, WheelConfiguration>>();
        private readonly HashSet<string> _placedAfterTransform = new HashSet<string>();
        private readonly List<string> _notices = new List<string>();

        private long _baseMinimumBet;
        private float _nextBettingAdjust;
        private bool _started;
        private int _lastStageStarted;
        private bool _cancelPlanned;
        private bool _liveTransformPending;
        private bool _transformStartedThisRound;
        private bool _syncPending;
        private bool _mandatoryFailed;
        private bool _respinning;
        private long _roundNet;
        private int _roundLoopholes;
        private int _prediction = int.MinValue;

        // Betting-window schedule, in seconds since betting opened (-1: nothing scheduled).
        private float _bettingElapsed;
        private float _midBettingAt = -1f, _liveTransformAt = -1f, _shakeAt = -1f;
        private float _midFraction = -1f, _liveFraction = -1f, _shakeFraction = -1f;
        private int _shakeStrength, _shakeTiltX, _shakeTiltY;

        // ------------------------------------------------------------------ systems

        public RunSetup Setup { get; }
        public MetaProgress Meta { get; }
        public Run Run { get; }
        public Ledger Ledger => Run.Ledger;
        public TableGame Table { get; }
        public RuleEngine Rules { get; }
        public InfluenceWallet Influence { get; }
        public Inventory Inventory { get; }
        public PlayerProfile Profile { get; }
        public AuthorityPool Authority { get; }
        public GameMasterBrain GameMaster { get; }
        public DialogueBank Dialogue { get; }
        public ContractBook Contracts { get; }
        public RoundInsight Insight { get; }
        public AbilitySystem Abilities { get; }
        public SpecialPocketCatalog Pockets { get; }
        public EventDirector Events { get; }
        public ChallengeBoard Challenges { get; }
        public Shop Shop { get; }
        public PaymentSchedule Payments { get; }
        public StageTable Stages { get; }
        public FinalEncounter Final { get; }
        public CasinoRecords Records { get; }
        public EndingCatalog Endings { get; }

        public long StartingDebt { get; }
        public IRandomSource Random => Run.Random;
        public int CurrentRound => Run.CurrentRoundNumber;
        public int Stage => Stages.StageOf(Run.CurrentRoundNumber);
        public StageDefinition CurrentStage => Stages.Get(Stage);
        public FinalPhase CurrentFinalPhase { get; private set; }
        public PendingDecision PendingDecision { get; private set; }
        public EndingDefinition Ending { get; private set; }
        public int MarksEarned { get; private set; }
        public RoundResult LastResult { get; private set; }
        public List<SpecialPocketOutcome> LastSpecialOutcomes { get; private set; } = new List<SpecialPocketOutcome>();
        public SpokenLine LastLine { get; private set; }
        public IReadOnlyList<string> Notices => _notices;
        public IReadOnlyList<RuleInstance> AnnouncedThisRound => _announcedThisRound;
        public bool IsRespinning => _respinning;
        public long BaseMinimumBet => _baseMinimumBet;
        /// <summary>Where the Game Master was glimpsed this round (environmental storytelling), or null.</summary>
        public RoomId? GameMasterSighting { get; private set; }
        public EnvironmentState Environment => CurrentStage.Environment;
        public GameMasterMood Mood => GameMasterBrain.MoodFor(Profile, Ledger.Debt, StartingDebt, Ledger.Balance);
        /// <summary>Loopholes exploited, hidden rules exposed and secrets read: the case for The Loophole.</summary>
        public int LoopholeEvidence => Profile.TotalLoopholeExploits + Records.SecretsFound;
        public bool HasPrediction => _prediction != int.MinValue;

        // ------------------------------------------------------------------ events

        public event Action<SpokenLine> Spoke;
        public event Action<string> Notified;
        public event Action<InterventionNotice> InterventionAnnounced;
        public event Action<StageDefinition> StageStarted;
        public event Action<FinalPhase> FinalPhaseStarted;
        public event Action<PendingDecision> DecisionRequired;
        public event Action<int> RoundStarted;
        public event Action<RoundResult> SpinResolved;
        public event Action RespinStarted;
        public event Action<List<SpecialPocketOutcome>> SpecialPocketsResolved;
        public event Action<string, string> LoopholeFound;
        public event Action<int> RoundEnded;
        public event Action<EndingDefinition> RunEnded;

        public CasinoRun(RunSetup setup, MetaProgress meta = null, bool countAsNewRun = true)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Meta = meta;
            Run = new Run(setup.Config);
            _dialogueRandom = new SeededRandom(Run.Random.Seed ^ 0xD1A1060EUL);
            _baseMinimumBet = Run.Config.MinimumBet;
            StartingDebt = Run.Config.StartingDebt;

            WheelConfiguration start = RunSetup.CreateWheel(setup.StartingWheelId);
            Table = new TableGame(Run, start);
            Profile = new PlayerProfile();
            Authority = new AuthorityPool(3);
            GameMaster = new GameMasterBrain(Authority, setup.Personality);
            Rules = new RuleEngine(RuleCatalog.CreateDefault(), start, this);
            Dialogue = DialogueBank.CreateDefault();
            Influence = new InfluenceWallet(setup.StartingInfluence);
            Inventory = new Inventory();
            foreach (string id in setup.StartingUpgrades) Inventory.AddUpgrade(id);
            foreach (KeyValuePair<string, int> c in setup.StartingCharges) Inventory.AddCharges(c.Key, c.Value);
            foreach (KeyValuePair<ChipKind, int> c in setup.StartingChips) Inventory.AddChips(c.Key, c.Value);
            Contracts = new ContractBook();
            Insight = new RoundInsight();
            Abilities = new AbilitySystem(AbilityCatalog.CreateDefault());
            Pockets = SpecialPocketCatalog.CreateDefault();
            Events = new EventDirector(EventCatalog.CreateDefault());
            foreach (KeyValuePair<string, double> w in setup.EventWeights) Events.WeightScale[w.Key] = w.Value;
            Challenges = new ChallengeBoard(ChallengeCatalog.CreateDefault());
            Shop = new Shop(ShopCatalog.CreateDefault());
            Stages = StageTable.CreateDefault(Run.Config.TotalRounds);
            Payments = PaymentSchedule.Create(StartingDebt, Stages.StageEndRounds());
            StageDefinition last = Stages.Get(5);
            Final = new FinalEncounter(last.FirstRound, last.LastRound, setup.FinalVariant);
            Records = CasinoRecords.CreateDefault();
            Endings = EndingCatalog.CreateDefault();

            Run.ExtraBankruptcyCheck = () => Payments.Defaulted || _mandatoryFailed;
            Run.OutcomeOverride = DecideOutcome;
            Run.Ended += OnRunEnded;
            Run.RoundAdvanced += OnRoundAdvanced;
            Run.Round.PhaseChanged += OnPhaseChanged;
            Abilities.Used += (a, m) => Challenges.AbilityUsed(this);
            if (Meta != null && countAsNewRun) Meta.RunsStarted++;
            SyncTable();
        }

        /// <summary>Opens the run: the first stage begins and the Game Master greets the player. Called automatically by the first <see cref="BeginRound"/>.</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            Say(DialogueTrigger.RunStart);
            StartStage(Stages.Get(Stage));
            PlanNext();
        }

        // ------------------------------------------------------------------ IRuleEnvironment

        int IRuleEnvironment.Round => CurrentRound;
        public long Balance => Ledger.Balance;
        public long Debt => Ledger.Debt;
        public int RoundsRemaining => Run.RoundsRemaining;
        public long ConfiguredMinimumBet => _baseMinimumBet;
        public long ConfiguredMaximumBet => Run.Config.MaximumBet;
        public int FavoriteNumber => Profile.FavoriteNumber;
        public int FavoriteDozen => Profile.FavoriteDozen;
        public IReadOnlyList<int> History => Table != null ? Table.History : NoHistory;

        // ------------------------------------------------------------------ the round

        /// <summary>
        /// Starts the next round: stage changes, the final encounter, events and the Game Master's
        /// interventions, then the announcement and betting. Returns false if the run is over, a round
        /// is already running, or a decision (the final offer) is pending.
        /// </summary>
        public bool BeginRound()
        {
            if (!_started) Start();
            if (Run.IsOver || Run.Round.Phase != RoundPhase.Idle || PendingDecision != PendingDecision.None) return false;
            int round = Run.CurrentRoundNumber;
            StageDefinition stage = Stages.For(round);
            if (stage.Number != _lastStageStarted) StartStage(stage);

            FinalPhase phase = Final.PhaseFor(round);
            if (phase == FinalPhase.TheLastSpin && !Final.OfferAnswered)
            {
                PendingDecision = PendingDecision.FinalOffer;
                CurrentFinalPhase = FinalPhase.HisFinalOffer;
                Contracts.Offer = ContractCatalog.Get("final_offer");
                Say(DialogueTrigger.FinalPhase, "3");
                FinalPhaseStarted?.Invoke(FinalPhase.HisFinalOffer);
                DecisionRequired?.Invoke(PendingDecision);
                return false;
            }

            _announcedThisRound.Clear();
            _placedAfterTransform.Clear();
            _transformStartedThisRound = false;
            _mandatoryFailed = false;
            _respinning = false;
            _roundNet = 0;
            _roundLoopholes = 0;
            LastSpecialOutcomes = new List<SpecialPocketOutcome>();
            _midFraction = _liveFraction = _shakeFraction = -1f;
            if (CurrentFinalPhase != phase && phase != FinalPhase.None) FinalPhaseStarted?.Invoke(phase);
            CurrentFinalPhase = phase;
            GameMasterSighting = Random.NextDouble() < stage.Environment.GameMasterSightings
                ? (RoomId?)(RoomId)(1 + Random.NextInt(5))
                : null;

            switch (phase)
            {
                case FinalPhase.None:
                    Events.RollForRound(this, stage.EventChance, stage.EventPool);
                    GameMasterTurn();
                    _midFraction = 0.35f + (float)Random.NextDouble() * 0.25f;
                    break;
                case FinalPhase.HisHouse:
                    Say(DialogueTrigger.FinalPhase, "1");
                    if (!HisHouse()) GameMasterTurn();
                    break;
                case FinalPhase.HisGame:
                    if (!Final.HisGameBuilt) BuildHisGame();
                    GameMasterTurn();
                    _midFraction = 0.35f + (float)Random.NextDouble() * 0.25f;
                    break;
                case FinalPhase.TheLastSpin:
                    Say(DialogueTrigger.FinalPhase, "4");
                    ActivateInternal(Rules.Catalog.Get("final_last_spin"), -1, false, false);
                    break;
            }
            CheckPrediction();

            if (_liveTransformPending)
            {
                _liveTransformPending = false;
                _liveFraction = 0.45f;
            }
            SyncTable();
            Run.Round.BettingSecondsDelta = Table.Modifiers.BettingSecondsDelta + _nextBettingAdjust;
            _nextBettingAdjust = 0f;
            if (!Table.TryBeginRound()) return false;
            Say(DialogueTrigger.RoundStart);
            RoundStarted?.Invoke(round);
            return true;
        }

        /// <summary>Advances the round's timers and runs scheduled mid-betting events. Call once per frame while not paused.</summary>
        public void Tick(float deltaSeconds)
        {
            if (Run.IsOver || deltaSeconds <= 0f) return;
            if (Run.Round.Phase == RoundPhase.Betting)
            {
                _bettingElapsed += deltaSeconds;
                if (_liveTransformAt >= 0f && _bettingElapsed >= _liveTransformAt) { _liveTransformAt = -1f; LiveTransformation(); }
                if (_midBettingAt >= 0f && _bettingElapsed >= _midBettingAt) { _midBettingAt = -1f; MidBettingIntervention(); }
                if (_shakeAt >= 0f && _bettingElapsed >= _shakeAt) { _shakeAt = -1f; DoShake(_shakeStrength, _shakeTiltX, _shakeTiltY); }
                if (Run.IsOver) return;
                if (Run.Round.Phase == RoundPhase.Betting && Run.Round.TimeRemaining <= deltaSeconds) EnforceMandatoryWager();
            }
            if (!Run.IsOver) Run.Round.Tick(deltaSeconds);
        }

        /// <summary>The player declares "no more bets".</summary>
        public bool CloseBetting()
        {
            if (Run.Round.Phase != RoundPhase.Betting) return false;
            EnforceMandatoryWager();
            return !Run.IsOver && Table.TryCloseBetting();
        }

        public SpinPlan StartSpin()
        {
            return Table.StartSpin();
        }

        /// <summary>
        /// Called when the ball has settled: settles bets, then applies special pockets, contracts,
        /// challenges, Influence, loopholes and events. If an event asks for another spin the round
        /// returns to "betting closed" with the same bets; otherwise the round moves to its summary.
        /// </summary>
        public RoundResult FinishSpin()
        {
            RoundResult result = Table.FinishSpin(false);
            LastResult = result;
            Profile.Observe(result, Table.HighestNumber);
            _roundNet += result.Net;

            foreach (string id in result.LoopholesExploited)
            {
                _roundLoopholes++;
                OnLoophole(id, DescribeLoophole(id), false);
            }

            int bonus = 0;
            foreach (BetOutcome bet in result.Bets)
            {
                if (!bet.Won) continue;
                if (bet.Ratio >= 30m) bonus += 2;
                else if (bet.Ratio >= 10m) bonus += 1;
            }
            if (bonus > 0) Influence.Earn(Math.Min(3, bonus), "Long-odds win");

            LastSpecialOutcomes = Pockets.ResolveAll(this, result);
            if (LastSpecialOutcomes.Count > 0) SpecialPocketsResolved?.Invoke(LastSpecialOutcomes);
            Contracts.RoundResolved(result, this);
            Challenges.RoundResolved(result, this);
            React(result);

            bool respin = !Run.IsOver && Events.RoundResolved(result, this);
            SpinResolved?.Invoke(result);
            if (respin && !Run.IsOver)
            {
                _respinning = true;
                if (Table.PrepareRespin(result.Bets) >= 0)
                {
                    Notify("The Double Spin: your bets ride again.");
                    RespinStarted?.Invoke();
                    return result;
                }
                _respinning = false;
            }
            _respinning = false;
            if (!Run.IsOver) Table.CompleteResolution();
            if (_syncPending && !Run.IsOver) SyncTable();
            return result;
        }

        private void OnPhaseChanged(RoundPhase previous, RoundPhase next)
        {
            switch (next)
            {
                case RoundPhase.Betting:
                {
                    _bettingElapsed = 0f;
                    float window = Run.Round.TimeRemaining;
                    _midBettingAt = _midFraction >= 0f ? window * _midFraction : -1f;
                    _liveTransformAt = _liveFraction >= 0f ? window * _liveFraction : -1f;
                    _shakeAt = _shakeFraction >= 0f ? window * _shakeFraction : -1f;
                    Say(DialogueTrigger.BettingOpen);
                    break;
                }
                case RoundPhase.BettingClosed:
                    if (_respinning) break;
                    Contracts.SpinStarting(this);
                    Events.SpinStarting(this);
                    Say(DialogueTrigger.BettingClosed);
                    break;
            }
        }

        private void OnRoundAdvanced(int roundsPlayed)
        {
            foreach (RuleInstance expired in Rules.TickRound())
            {
                if (expired.Definition.Negative && expired.IsVisible) Notify(expired.Definition.Name + " has ended.");
            }
            Contracts.RoundEnded(this);
            Events.RoundEnded(this);
            Challenges.RoundEnded(this);

            long paid;
            InstallmentResult installment = Payments.Collect(roundsPlayed, Ledger, (a, r) => IncreaseDebt(a, r), out paid);
            switch (installment)
            {
                case InstallmentResult.Collected:
                    Notify("Installment collected: $" + paid.ToString("N0") + ".");
                    Say(DialogueTrigger.InstallmentPaid, null, new Dictionary<string, string> { { "amount", "$" + paid.ToString("N0") } });
                    break;
                case InstallmentResult.Grace:
                    Notify("You missed an installment. A late fee was added to your debt. The next miss is the end.");
                    break;
                case InstallmentResult.Default:
                    Notify("You missed a second installment. The house calls in the debt.");
                    break;
            }

            Authority.Gain(GameMaster.AuthorityForRound(_roundNet, _roundLoopholes), "Round " + roundsPlayed);
            Insight.Reset();
            Inventory.NewRound();
            _prediction = int.MinValue;

            if (!Run.IsOver && Run.RoundsRemaining > 0)
            {
                SyncTable();
                PlanNext();
                StageDefinition next = Stages.For(Run.CurrentRoundNumber);
                if (Challenges.Active == null && Challenges.Offer == null && Random.NextDouble() < next.ChallengeChance)
                {
                    Challenges.MakeOffer(this, Setup.IsUnlocked);
                }
                if (next.ContractsAvailable && Contracts.Offer == null && next.Number < 5 && Random.NextDouble() < next.ContractChance)
                {
                    List<ContractDefinition> options = AvailableContracts();
                    if (options.Count > 0)
                    {
                        Contracts.Offer = options[Random.NextInt(options.Count)];
                        Say(DialogueTrigger.ContractOffer, Contracts.Offer.Id);
                    }
                }
                if (Ledger.Balance + Ledger.Stored < Run.EffectiveMinimumBet * 4) Say(DialogueTrigger.NearBankrupt);
                else if (Ledger.Debt <= StartingDebt / 5) Say(DialogueTrigger.NearFreedom);
            }
            RoundEnded?.Invoke(roundsPlayed);
        }

        // ------------------------------------------------------------------ the Game Master

        private void PlanNext()
        {
            StageDefinition stage = Stages.For(Run.CurrentRoundNumber);
            GameMaster.Difficulty = stage.Difficulty;
            GameMaster.RuleFilter = stage.AllowsRule;
            if (Final.PhaseFor(Run.CurrentRoundNumber) == FinalPhase.TheLastSpin) { GameMaster.ClearPlan(); return; }
            GameMaster.Plan(Rules, Profile, this, StartingDebt, Random);
        }

        private void GameMasterTurn()
        {
            if (_cancelPlanned)
            {
                _cancelPlanned = false;
                GameMaster.ClearPlan();
                return;
            }
            var picks = new List<InterventionCandidate>();
            if (GameMaster.Planned.HasValue)
            {
                InterventionCandidate planned = GameMaster.Planned.Value;
                GameMaster.ClearPlan();
                if (planned.Rule.AuthorityCost <= Authority.Current && Rules.CanActivate(planned.Rule, planned.Parameter).Success) picks.Add(planned);
            }
            int more = GameMaster.Difficulty.MaxInterventionsPerRound - 1;
            if (more > 0)
            {
                foreach (InterventionCandidate c in GameMaster.Choose(Rules, Profile, this, StartingDebt, Random, false, more))
                {
                    bool duplicate = false;
                    foreach (InterventionCandidate p in picks) if (p.Rule.Id == c.Rule.Id) duplicate = true;
                    if (!duplicate) picks.Add(c);
                }
            }
            foreach (InterventionCandidate pick in picks) Intervene(pick.Rule, pick.Parameter, false);
        }

        private void MidBettingIntervention()
        {
            foreach (InterventionCandidate pick in GameMaster.Choose(Rules, Profile, this, StartingDebt, Random, true, 1))
            {
                Intervene(pick.Rule, pick.Parameter, true);
            }
        }

        private void Intervene(RuleDefinition rule, int parameter, bool midBetting)
        {
            if (rule.Negative && Inventory.GoldenTickets > 0 && Inventory.TryUseGoldenTicket())
            {
                Notify("Your Golden Ticket cancels " + rule.Name + " before it begins.");
                Say(DialogueTrigger.RuleCancelled, null, new Dictionary<string, string> { { "rule", rule.Name } });
                InterventionAnnounced?.Invoke(new InterventionNotice(rule, null, rule.Announcement, midBetting, true));
                return;
            }
            if (!Rules.CanActivate(rule, parameter).Success) return;
            if (!Authority.TrySpend(rule.AuthorityCost, rule.Name)) return;
            ActivateInternal(rule, parameter, true, midBetting);
        }

        private ulong NextSeed()
        {
            return (ulong)Random.NextInt(int.MaxValue) * 2654435761UL + 1UL;
        }

        private bool ActivateInternal(RuleDefinition rule, int parameter, bool byGameMaster, bool midBetting)
        {
            if (rule == null) return false;
            WheelConfiguration baseBefore = Rules.BaseWheel;
            RuleActivation activation = Rules.Activate(rule, parameter, NextSeed());
            if (!activation.Success) return false;
            if (rule.Permanent && !ReferenceEquals(baseBefore, Rules.BaseWheel))
            {
                _permanentUndo[rule.Id] = new KeyValuePair<WheelConfiguration, WheelConfiguration>(baseBefore, Rules.BaseWheel);
            }
            if (byGameMaster)
            {
                _announcedThisRound.Add(activation.Instance);
                if (rule.Negative && !_imposedHistory.Contains(rule.Id)) _imposedHistory.Add(rule.Id);
            }

            WheelConfiguration tableBefore = Table.Wheel;
            SyncTable();
            if (Run.Round.Phase == RoundPhase.Betting && !ReferenceEquals(tableBefore, Table.Wheel)) _transformStartedThisRound = true;

            bool silent = Table.Modifiers.SilentSpin;
            if (!rule.Hidden && !silent && rule.Announcement != "") Notify(rule.Name + ": " + rule.Description);
            if (byGameMaster)
            {
                InterventionAnnounced?.Invoke(new InterventionNotice(rule, activation.Instance, rule.Hidden || silent ? null : rule.Announcement, midBetting, false));
            }
            ExecuteInstantAction(rule);
            return true;
        }

        private void ExecuteInstantAction(RuleDefinition rule)
        {
            switch (rule.InstantAction)
            {
                case RuleInstantAction.ShakeTable:
                case RuleInstantAction.TiltTable:
                {
                    bool tilt = rule.InstantAction == RuleInstantAction.TiltTable;
                    int tx = tilt ? (Random.NextInt(2) == 0 ? -1 : 1) : 0;
                    int ty = tilt ? -1 : 0;
                    if (Run.Round.Phase == RoundPhase.Betting) DoShake(rule.InstantValue, tx, ty);
                    else
                    {
                        _shakeStrength = rule.InstantValue;
                        _shakeTiltX = tx;
                        _shakeTiltY = ty;
                        _shakeFraction = 0.6f;
                    }
                    break;
                }
                case RuleInstantAction.TakeBalancePercent:
                {
                    long amount = Ledger.Balance * rule.InstantValue / 100;
                    if (amount > 0) Ledger.ForceDebit(amount, TransactionKind.Penalty, rule.Name);
                    break;
                }
            }
        }

        private void DoShake(int strength, int tiltX, int tiltY)
        {
            if (Insight.ChipAnchor) Table.LockAll(true);
            ShakeReport report = Table.Shake(strength, Random, tiltX, tiltY);
            if (report.Displacements.Count > 0) Notify((report.IsTilt ? "The table tilts: " : "The table shakes: ") + report.Displacements.Count + " stack" + (report.Displacements.Count == 1 ? "" : "s") + " moved.");
            else Notify(report.IsTilt ? "The table tilts. Your chips hold." : "The table shakes. Your chips hold.");
        }

        private void LiveTransformation()
        {
            _transformStartedThisRound = true;
            foreach (string id in LiveTransformations)
            {
                RuleDefinition rule = Rules.Catalog.Get(id);
                if (rule != null && Rules.CanActivate(rule).Success && ActivateInternal(rule, -1, true, true)) return;
            }
        }

        private bool HisHouse()
        {
            int imposed = 0;
            for (int i = _imposedHistory.Count - 1; i >= 0 && imposed < 3; i--)
            {
                RuleDefinition rule = Rules.Catalog.Get(_imposedHistory[i]);
                if (rule == null || rule.Permanent || Rules.IsActive(rule.Id) || !Rules.CanActivate(rule).Success) continue;
                if (ActivateInternal(rule, -1, true, false)) imposed++;
            }
            return imposed > 0;
        }

        private void BuildHisGame()
        {
            Final.HisGameBuilt = true;
            Say(DialogueTrigger.FinalPhase, "2");
            WheelChange change = Final.BuildHisGame(Rules.BaseWheel, Random);
            if (change.Succeeded && Rules.SetBaseWheel(change.After)) Notify(change.Description);
            if (Final.Variant == 1) ActivateInternal(Rules.Catalog.Get("double_trouble"), -1, true, false);
        }

        private void StartStage(StageDefinition stage)
        {
            _lastStageStarted = stage.Number;
            GameMaster.Difficulty = stage.Difficulty;
            GameMaster.RuleFilter = stage.AllowsRule;
            Shop.Restock(stage.Number, Random, Setup.IsUnlocked, Inventory);
            if (stage.ResizeWheelTo > 0 && Rules.BaseWheel.PocketCount != stage.ResizeWheelTo)
            {
                int size = stage.ResizeWheelTo;
                ChangeBaseWheel(w => WheelTransforms.Resize(w, size));
            }
            for (int i = 0; i < stage.SpecialPocketsAtStart; i++)
            {
                SpecialPocketType type = PickStagePocket(stage.Number, i);
                ChangeBaseWheel(w => WheelTransforms.InsertSpecial(w, type, Random));
            }
            Say(DialogueTrigger.StageStart, stage.Number.ToString());
            Notify("Stage " + stage.Number + ": " + stage.Name + ". " + stage.NewMechanics);
            StageStarted?.Invoke(stage);
        }

        private SpecialPocketType PickStagePocket(int stage, int index)
        {
            if (stage == 2 && index == 0 && Setup.IsUnlocked("pocket_lucky_start")) return SpecialPocketType.Influence;
            SpecialPocketType[] pool = stage >= 3
                ? new[] { SpecialPocketType.Skull, SpecialPocketType.Tax, SpecialPocketType.Curse, SpecialPocketType.Bomb, SpecialPocketType.Mystery, SpecialPocketType.Hourglass, SpecialPocketType.Jackpot, SpecialPocketType.Influence, SpecialPocketType.DebtRelief, SpecialPocketType.GoldenTicket }
                : new[] { SpecialPocketType.Skull, SpecialPocketType.Tax, SpecialPocketType.Mystery, SpecialPocketType.Hourglass, SpecialPocketType.Jackpot, SpecialPocketType.Influence, SpecialPocketType.DebtRelief, SpecialPocketType.GoldenTicket };
            return pool[Random.NextInt(pool.Length)];
        }

        private void CheckPrediction()
        {
            if (_prediction == int.MinValue) return;
            bool correct;
            if (_prediction == -1) correct = _announcedThisRound.Count == 0;
            else
            {
                correct = false;
                foreach (RuleInstance r in _announcedThisRound) if ((int)r.Definition.Category == _prediction) correct = true;
            }
            if (correct)
            {
                Influence.Earn(1, "Predicted the Game Master");
                Notify("You read him correctly: +1 Influence.");
            }
            else Notify("Your prediction was wrong.");
            _prediction = int.MinValue;
        }

        // ------------------------------------------------------------------ keeping the table in step

        /// <summary>
        /// Recomposes the rules and pushes the effective wheel and modifiers to the table. While the
        /// ball is moving the table cannot change, so the sync waits until the round allows it.
        /// </summary>
        public void SyncTable()
        {
            WheelChange recomposed = Rules.Recompose();
            RoundPhase phase = Run.Round.Phase;
            if (phase == RoundPhase.BettingClosed || phase == RoundPhase.Spinning || phase == RoundPhase.Resolving || Table.IsSpinPending)
            {
                _syncPending = true;
                return;
            }
            _syncPending = false;
            WheelConfiguration target = Rules.EffectiveWheel;
            if (!SameContent(Table.Wheel, target))
            {
                WheelChange change = recomposed != null && ReferenceEquals(recomposed.Before, Table.Wheel)
                    ? recomposed
                    : new WheelChange(Table.Wheel, target, recomposed != null ? recomposed.Kind : WheelChangeKind.Replace,
                        recomposed != null ? recomposed.Description : "The wheel changes.", recomposed != null ? recomposed.CloneSources : null);
                Table.ApplyWheelChange(change);
            }
            TableModifiers modifiers = Rules.EffectiveModifiers.Clone();
            StageDefinition stage = Stages.Get(Stage);
            if (!stage.SpecialBetsAllowed) modifiers.SpecialBetsAllowed = false;
            Table.SetModifiers(modifiers);
        }

        private static bool SameContent(WheelConfiguration a, WheelConfiguration b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.PocketCount != b.PocketCount || !ReferenceEquals(a.Ring, b.Ring) && (a.Ring == null || b.Ring == null || a.Ring.Segments.Count != b.Ring.Segments.Count)) return false;
            for (int i = 0; i < a.PocketCount; i++)
            {
                Pocket p = a.Pockets[i], q = b.Pockets[i];
                if (p.Id != q.Id || p.Number != q.Number || p.Color != q.Color || p.Weight != q.Weight || p.IsActive != q.IsActive || p.Special != q.Special || p.PayoutPercent != q.PayoutPercent) return false;
            }
            return true;
        }

        private void EnforceMandatoryWager()
        {
            long required = Table.Modifiers.MandatoryWager;
            if (required <= 0) return;
            long shortfall = required - Table.Bets.Total;
            if (shortfall <= 0) return;
            long amount = Math.Max(shortfall, Table.Modifiers.MinimumBet);
            BetType[] evenMoney = { BetType.Red, BetType.Black, BetType.Even, BetType.Odd, BetType.Low, BetType.High };
            foreach (BetType type in evenMoney)
            {
                BetSelection selection = BetSelection.Outside(type);
                if (Table.CanPlace(selection, amount) != PlacementResult.Accepted) continue;
                if (Table.PlaceBet(selection, amount) == PlacementResult.Accepted)
                {
                    Notify("Mandatory wager: the house places $" + amount.ToString("N0") + " on " + selection.DisplayName + " for you.");
                    return;
                }
            }
            _mandatoryFailed = true;
            Notify("You cannot cover the mandatory wager of $" + required.ToString("N0") + ".");
            Run.EvaluateOutcome();
        }

        // ------------------------------------------------------------------ outcome and endings

        private EndingContext ContextFor(RunOutcome raw)
        {
            return new EndingContext
            {
                RawOutcome = raw,
                Balance = Ledger.Balance,
                Stored = Ledger.Stored,
                Debt = Ledger.Debt,
                StartingDebt = StartingDebt,
                Stage = Stage,
                LoopholeEvidence = LoopholeEvidence,
                ReachedFinalTable = _lastStageStarted >= 5
            };
        }

        private RunOutcome DecideOutcome(RunOutcome raw)
        {
            EndingDefinition ending = Endings.Resolve(ContextFor(raw));
            Ending = ending;
            return ending != null ? ending.Outcome : raw;
        }

        private void OnRunEnded(RunOutcome outcome)
        {
            if (Ending == null || Ending.Outcome != outcome) Ending = Endings.Resolve(ContextFor(outcome));
            Say(DialogueTrigger.Ending, outcome.ToString());
            if (Meta != null)
            {
                var summary = new RunSummary
                {
                    EndingId = Ending != null ? Ending.Id : outcome.ToString(),
                    Victory = Ending != null && Ending.IsVictory,
                    RoundsPlayed = Run.RoundsPlayed,
                    DebtPaid = Math.Max(0, StartingDebt - Ledger.Debt),
                    ChallengesWon = Challenges.Won,
                    SecretsFound = Records.SecretsFound,
                    LoopholesFound = Profile.TotalLoopholeExploits,
                    FinalBalance = Ledger.Balance + Ledger.Stored
                };
                MarksEarned = Meta.RecordRun(summary);
            }
            RunEnded?.Invoke(Ending);
        }

        /// <summary>Answers His Final Offer. The last round can begin afterwards.</summary>
        public bool AnswerFinalOffer(FinalOfferChoice choice)
        {
            if (PendingDecision != PendingDecision.FinalOffer) return false;
            PendingDecision = PendingDecision.None;
            Final.OfferAnswered = true;
            Final.Answer = choice;
            Contracts.Offer = null;
            switch (choice)
            {
                case FinalOfferChoice.TheDeal:
                    Say(DialogueTrigger.ContractAccepted, "final_offer");
                    Contracts.Sign(ContractCatalog.Get("final_offer"), this);
                    break;
                case FinalOfferChoice.EscapeClause:
                    Say(DialogueTrigger.ContractAccepted, "escape_clause");
                    Contracts.Sign(ContractCatalog.Get("escape_clause"), this);
                    break;
                default:
                    Say(DialogueTrigger.ContractRefused, "final_offer");
                    break;
            }
            return true;
        }

        // ------------------------------------------------------------------ player actions

        private bool BetweenRounds => Run.Round.Phase == RoundPhase.Idle || Run.Round.Phase == RoundPhase.Summary;
        private bool BeforeSpin => BetweenRounds || Run.Round.Phase == RoundPhase.Announcing || Run.Round.Phase == RoundPhase.Betting;

        public PlacementResult PlaceBet(BetSelection selection, long amount, ChipKind kind = ChipKind.Standard)
        {
            if (kind != ChipKind.Standard && Inventory.ChipsOf(kind) <= 0) return PlacementResult.NotEligible;
            PlacementResult result = Table.PlaceBet(selection, amount, kind);
            if (result != PlacementResult.Accepted) return result;
            Inventory.TryUseChip(kind);
            if (Insight.ChipAnchor) Table.SetLocked(selection, true);
            if (_transformStartedThisRound) _placedAfterTransform.Add(selection.Key);
            return result;
        }

        public PlacementResult RemoveBet(BetSelection selection, long amount) { return Table.RemoveBet(selection, amount); }

        public PlacementResult MoveBet(BetSelection from, BetSelection to, long amount)
        {
            PlacementResult result = Table.MoveBet(from, to, amount);
            if (result == PlacementResult.Accepted && _transformStartedThisRound) _placedAfterTransform.Add(to.Key);
            return result;
        }

        public bool ClearBets() { return Table.ClearBets(); }

        public AbilityResult UseAbility(string id, int number = -1, BetSelection bet = null)
        {
            return Abilities.Use(id, this, number, bet);
        }

        /// <summary>Guess what kind of intervention he will make next round (null: none at all). A correct guess earns Influence.</summary>
        public bool PredictIntervention(RuleCategory? category)
        {
            if (!BetweenRounds || Run.IsOver) return false;
            _prediction = category.HasValue ? (int)category.Value : -1;
            return true;
        }

        /// <summary>The Vault: pays debt from the balance (or the stored money). Counts toward installments.</summary>
        public long PayDebt(long amount, bool fromVault = false)
        {
            if (amount <= 0 || !BeforeSpin || Run.IsOver) return 0;
            long paid = fromVault ? Ledger.PayDebtFromVault(amount, "Vault payment") : Ledger.PayDebt(amount, "Debt payment");
            Payments.RecordPayment(paid);
            return paid;
        }

        public long Deposit(long amount)
        {
            if (amount <= 0 || !BetweenRounds || Run.IsOver) return 0;
            return Ledger.Deposit(amount);
        }

        public long Withdraw(long amount)
        {
            if (amount <= 0 || !BetweenRounds || Run.IsOver) return 0;
            return Ledger.Withdraw(amount);
        }

        /// <summary>Contracts the Vault can offer right now.</summary>
        public List<ContractDefinition> AvailableContracts()
        {
            var list = new List<ContractDefinition>();
            if (!Stages.Get(Stage).ContractsAvailable) return list;
            foreach (ContractDefinition c in ContractCatalog.All)
            {
                if (c.Exclusive || Contracts.Has(c.Id) || !c.IsAvailable(this)) continue;
                if (c.Id == "loan_shark" && !Setup.IsUnlocked("contract_loan_shark")) continue;
                list.Add(c);
            }
            return list;
        }

        /// <summary>Signs the offered contract or one from the Vault. Returns null on success, otherwise why not.</summary>
        public string SignContract(string id)
        {
            if (!BeforeSpin || Run.IsOver) return "Not while the ball is moving.";
            ContractDefinition def = ContractCatalog.Get(id);
            if (def == null) return "Unknown contract.";
            if (def.Id == "final_offer" || def.Id == "escape_clause") return "Answer his final offer at the table.";
            bool offered = Contracts.Offer != null && Contracts.Offer.Id == id;
            if (!offered && !AvailableContracts().Contains(def)) return "That contract is not on offer.";
            if (!def.IsAvailable(this)) return "Its conditions are not met.";
            Contracts.Sign(def, this);
            Say(DialogueTrigger.ContractAccepted, def.Id);
            SyncTable();
            return null;
        }

        public void RefuseContractOffer()
        {
            if (Contracts.Offer == null || PendingDecision != PendingDecision.None) return;
            Contracts.Offer = null;
            Say(DialogueTrigger.ContractRefused);
        }

        public string BuyItem(string itemId)
        {
            if (!BeforeSpin || Run.IsOver) return "The shop is closed during the spin.";
            return Shop.Buy(itemId, Ledger, Inventory, Influence);
        }

        public ChallengeInstance AcceptChallenge()
        {
            if (!BetweenRounds || Run.IsOver) return null;
            ChallengeInstance c = Challenges.Accept(this);
            if (c != null) SyncTable();
            return c;
        }

        public void DeclineChallenge() { Challenges.Decline(); }

        public string Bid(int lotIndex, long amount)
        {
            if (Events.Auction == null) return "There is no auction.";
            if (Run.Round.Phase != RoundPhase.Announcing && Run.Round.Phase != RoundPhase.Betting) return "The auction is closed.";
            return Events.Auction.Bid(lotIndex, amount, this);
        }

        /// <summary>Reads a document in the Records Room. Returns its text, or null if it cannot be read yet.</summary>
        public string ReadRecord(string id)
        {
            RecordEntry entry = Records.Get(id);
            if (entry == null || !Records.CanRead(entry, Stage, Profile.TotalLoopholeExploits)) return null;
            if (Records.Read(entry))
            {
                Influence.Earn(1, "Discovered a secret");
                Notify("A secret: " + entry.Title + ". +1 Influence.");
            }
            return entry.Text;
        }

        /// <summary>The Private Lounge: rest once per stage for 1 Influence. Returns what the staff say.</summary>
        public string Rest()
        {
            int stage = Stage;
            string line = CasinoRecords.StaffLine(stage, Random.NextInt(8));
            if (BetweenRounds && Records.Rest(stage)) Influence.Earn(1, "Rested in the lounge");
            return line;
        }

        public string VisitOffice() { return CasinoRecords.OfficeScene(Stage); }

        public List<FinancialWarning> Warnings()
        {
            return BankruptcyForecast.Evaluate(Ledger.Balance + Ledger.Stored, Ledger.Debt, Run.EffectiveMinimumBet, Table.Modifiers.MandatoryWager,
                Payments, CurrentRound, Run.RoundsRemaining);
        }

        // ------------------------------------------------------------------ IGameHost

        public void Say(DialogueTrigger trigger, string key = null, IDictionary<string, string> values = null)
        {
            if (Table != null && Table.Modifiers.SilentSpin && (trigger == DialogueTrigger.RoundStart || trigger == DialogueTrigger.BettingOpen || trigger == DialogueTrigger.BettingClosed)) return;
            SpokenLine line = Dialogue.Pick(trigger, Mood, Stage, GameMaster.Personality.Id, _dialogueRandom, key, values);
            if (line == null) return;
            LastLine = line;
            Spoke?.Invoke(line);
        }

        public void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notices.Add(text);
            if (_notices.Count > 40) _notices.RemoveAt(0);
            Notified?.Invoke(text);
        }

        public bool ActivateRule(string ruleId, bool free, int parameter = -1)
        {
            RuleDefinition rule = Rules.Catalog.Get(ruleId);
            if (rule == null || !Rules.CanActivate(rule, parameter).Success) return false;
            if (!free && !Authority.TrySpend(rule.AuthorityCost, rule.Name)) return false;
            return ActivateInternal(rule, parameter, false, Run.Round.Phase == RoundPhase.Betting);
        }

        /// <summary>The Game Master imposes a rule right now, free of Authority, announced as his intervention (debug menu, scripted moments).</summary>
        public bool ImposeRule(string ruleId, int parameter = -1)
        {
            RuleDefinition rule = Rules.Catalog.Get(ruleId);
            if (rule == null || !Rules.CanActivate(rule, parameter).Success) return false;
            return ActivateInternal(rule, parameter, true, Run.Round.Phase == RoundPhase.Betting);
        }

        public long IncreaseDebt(long amount, string reason)
        {
            if (amount <= 0) return 0;
            if (Table.Modifiers.DebtFrozen || Rules.EffectiveModifiers.DebtFrozen)
            {
                Notify("Debt Freeze: $" + amount.ToString("N0") + " (" + reason + ") was not added.");
                return 0;
            }
            Ledger.IncreaseDebt(amount, reason);
            return amount;
        }

        public void AdjustNextBettingTime(float seconds) { _nextBettingAdjust += seconds; }

        public void RaiseBaseMinimumBet(long amount)
        {
            if (amount <= 0) return;
            _baseMinimumBet += amount;
            SyncTable();
        }

        public void TriggerRandomEvent()
        {
            EventDefinition next = Events.PickRandom(this, null);
            if (next == null) return;
            Events.Force(next);
            Notify("Something stirs: " + next.Name + " begins next round.");
        }

        public void RecordLoophole(string sourceId, string description) { OnLoophole(sourceId, description, true); }

        private void OnLoophole(string sourceId, string description, bool addToProfile)
        {
            if (addToProfile)
            {
                Profile.RecordLoophole(sourceId);
                _roundLoopholes++;
            }
            int uses;
            Profile.LoopholeUses.TryGetValue(sourceId, out uses);
            Influence.Earn(uses <= 1 ? 2 : 1, "Loophole");
            RuleDefinition rule = Rules.Catalog.Get(sourceId);
            var values = new Dictionary<string, string> { { "rule", rule != null ? rule.Name : sourceId } };
            Say(uses > 1 ? DialogueTrigger.RepeatedExploit : DialogueTrigger.Loophole, null, values);
            Notify("Loophole: " + description);
            LoopholeFound?.Invoke(sourceId, description);
        }

        private string DescribeLoophole(string ruleId)
        {
            RuleDefinition rule = Rules.Catalog.Get(ruleId);
            if (rule == null) return "a rule did not apply to your bet.";
            return rule.Name + " did not apply to your bet" + (string.IsNullOrEmpty(rule.LoopholeHint) ? "." : " (" + rule.LoopholeHint + ")");
        }

        public bool ChangeBaseWheel(Func<WheelConfiguration, WheelChange> transform)
        {
            WheelChange change = transform(Rules.BaseWheel);
            if (change == null || !change.Succeeded) return false;
            if (!Rules.SetBaseWheel(change.After)) return false;
            SyncTable();
            return true;
        }

        public string PeekNextIntervention()
        {
            if (_cancelPlanned) return null;
            if (!GameMaster.Planned.HasValue && BetweenRounds) PlanNext();
            return GameMaster.Planned.HasValue ? GameMaster.Planned.Value.Rule.Name : null;
        }

        public string CancelIntervention()
        {
            for (int i = _announcedThisRound.Count - 1; i >= 0; i--)
            {
                RuleInstance r = _announcedThisRound[i];
                if (!r.Definition.Negative) continue;
                bool undone = false;
                KeyValuePair<WheelConfiguration, WheelConfiguration> undo;
                if (r.Definition.Permanent && _permanentUndo.TryGetValue(r.Id, out undo) && ReferenceEquals(Rules.BaseWheel, undo.Value))
                {
                    undone = Rules.SetBaseWheel(undo.Key);
                }
                bool cancelled = Rules.Cancel(r.Id);
                if (!cancelled && !undone) continue;
                _announcedThisRound.RemoveAt(i);
                SyncTable();
                return r.Definition.Name;
            }
            if (GameMaster.Planned.HasValue && !_cancelPlanned)
            {
                string name = GameMaster.Planned.Value.Rule.Name;
                GameMaster.ClearPlan();
                _cancelPlanned = true;
                return name + " (planned)";
            }
            return null;
        }

        public void ScheduleLiveTransformation() { _liveTransformPending = true; }

        public bool PlacedDuringTransformation(BetSelection selection)
        {
            return selection != null && _placedAfterTransform.Contains(selection.Key);
        }

        private void React(RoundResult result)
        {
            if (result.TotalStaked == 0) Say(DialogueTrigger.PlayerNoBet);
            else if (result.Net >= Math.Max(2000, result.TotalStaked * 5)) Say(DialogueTrigger.PlayerWinBig);
            else if (result.Net > 0) Say(DialogueTrigger.PlayerWinSmall);
            else if (result.Net < 0) Say(DialogueTrigger.PlayerLoss);
        }

        // ------------------------------------------------------------------ persistence hooks

        /// <summary>Re-applies the current stage's difficulty and rule pool to the Game Master (after loading).</summary>
        internal void RefreshStageSettings()
        {
            StageDefinition stage = Stages.For(Run.CurrentRoundNumber);
            GameMaster.Difficulty = stage.Difficulty;
            GameMaster.RuleFilter = stage.AllowsRule;
        }

        internal List<string> ImposedHistory => _imposedHistory;
        internal int LastStageStarted { get => _lastStageStarted; set => _lastStageStarted = value; }
        internal bool Started { get => _started; set => _started = value; }
        internal long BaseMinimumBetInternal { get => _baseMinimumBet; set => _baseMinimumBet = value; }
        internal float NextBettingAdjust { get => _nextBettingAdjust; set => _nextBettingAdjust = value; }
        internal bool CancelPlanned { get => _cancelPlanned; set => _cancelPlanned = value; }
        internal bool LiveTransformPending { get => _liveTransformPending; set => _liveTransformPending = value; }
    }
}

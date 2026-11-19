using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.Specials
{
    public enum ChallengeStatus
    {
        Active,
        Won,
        Lost
    }

    /// <summary>What a challenge pays out. Any combination of money, Influence, debt reduction and items.</summary>
    public sealed class ChallengeReward
    {
        public long Money { get; set; }
        public int Influence { get; set; }
        public long DebtReduction { get; set; }
        /// <summary>Ability id for a free charge, "golden_ticket", "upgrade:&lt;id&gt;" for a permanent upgrade, or null.</summary>
        public string Item { get; set; }

        public string Describe()
        {
            var parts = new List<string>();
            if (Money > 0) parts.Add("$" + Money.ToString("N0"));
            if (Influence > 0) parts.Add(Influence + " Influence");
            if (DebtReduction > 0) parts.Add("$" + DebtReduction.ToString("N0") + " off your debt");
            if (!string.IsNullOrEmpty(Item)) parts.Add(Item.StartsWith("upgrade:", StringComparison.Ordinal) ? "the " + Item.Substring(8).Replace('_', ' ') + " upgrade" : "a " + Item.Replace('_', ' '));
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// A challenge the Game Master offers between rounds. Accepting is optional; success pays the
    /// reward scaled by stage, failure costs nothing but pride (and his respect).
    /// </summary>
    public abstract class ChallengeDefinition
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Describe(IGameHost host);
        public int MinStage { get; set; } = 1;
        /// <summary>Rounds the player has to complete it.</summary>
        public int Rounds { get; set; } = 3;
        public long RewardMoney { get; set; }
        public int RewardInfluence { get; set; }
        public long RewardDebt { get; set; }
        public string RewardItem { get; set; }
        /// <summary>Needs this meta unlock (null: always available).</summary>
        public string UnlockId { get; set; }

        public ChallengeReward RewardAt(int stage)
        {
            int scale = Math.Max(1, stage);
            return new ChallengeReward { Money = RewardMoney * scale, Influence = RewardInfluence, DebtReduction = RewardDebt * scale, Item = RewardItem };
        }

        public virtual bool IsAvailable(IGameHost host) { return host.Stage >= MinStage; }

        public virtual void OnAccept(ChallengeInstance challenge, IGameHost host) { }

        /// <summary>Called after each resolved spin while active.</summary>
        public abstract ChallengeStatus OnRoundResolved(ChallengeInstance challenge, RoundResult result, IGameHost host);

        public virtual ChallengeStatus OnAbilityUsed(ChallengeInstance challenge, IGameHost host) { return ChallengeStatus.Active; }

        /// <summary>Called when the challenge runs out of rounds without being decided.</summary>
        public virtual ChallengeStatus OnTimeUp(ChallengeInstance challenge, IGameHost host) { return ChallengeStatus.Lost; }
    }

    public sealed class ChallengeInstance
    {
        public ChallengeDefinition Definition { get; }
        public int AcceptedRound { get; }
        public int RoundsLeft { get; set; }
        public int Progress { get; set; }
        /// <summary>Target or baseline amount fixed when accepted.</summary>
        public long Target { get; set; }
        public long Accumulated { get; set; }
        public ChallengeStatus Status { get; set; } = ChallengeStatus.Active;

        public ChallengeInstance(ChallengeDefinition definition, int round)
        {
            Definition = definition;
            AcceptedRound = round;
            RoundsLeft = definition.Rounds;
        }
    }

    // ------------------------------------------------------------------ the seven challenges

    /// <summary>Predict three winning numbers in a row: three consecutive spins won with a bet covering six numbers or fewer.</summary>
    public sealed class PredictThree : ChallengeDefinition
    {
        public PredictThree() { Rounds = 3; RewardMoney = 2500; RewardInfluence = 4; RewardDebt = 2500; MinStage = 1; }
        public override string Id => "predict_three";
        public override string Name => "Three in a Row";
        public override string Describe(IGameHost host) { return "Predict three winners in a row: in each of the next three spins, win with a bet covering six numbers or fewer."; }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            bool predicted = false;
            foreach (BetOutcome b in r.Bets) if (b.Won && b.Selection.IsNumbered && b.Selection.Numbers.Length <= 6) predicted = true;
            if (!predicted) return ChallengeStatus.Lost;
            c.Progress++;
            return c.Progress >= 3 ? ChallengeStatus.Won : ChallengeStatus.Active;
        }
    }

    /// <summary>Survive five spins without losing more than a set amount.</summary>
    public sealed class SurviveFive : ChallengeDefinition
    {
        public SurviveFive() { Rounds = 5; RewardMoney = 800; RewardInfluence = 2; RewardDebt = 500; RewardItem = "bet_insurance"; }
        public override string Id => "survive_five";
        public override string Name => "Steady Hands";
        public override string Describe(IGameHost host) { return "Play the next five spins (betting at least the minimum each time) without losing more than $" + LimitFor(host).ToString("N0") + " in total."; }

        private static long LimitFor(IGameHost host) { return Math.Max(200, host.Ledger.Balance / 5 / 10 * 10); }

        public override void OnAccept(ChallengeInstance c, IGameHost host) { c.Target = LimitFor(host); }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            if (r.TotalStaked <= 0) return ChallengeStatus.Lost;
            c.Accumulated += r.Net;
            if (-c.Accumulated > c.Target) return ChallengeStatus.Lost;
            c.Progress++;
            return c.Progress >= 5 ? ChallengeStatus.Won : ChallengeStatus.Active;
        }
    }

    /// <summary>Win using only outside bets.</summary>
    public sealed class OutsideOnly : ChallengeDefinition
    {
        public OutsideOnly() { Rounds = 3; RewardMoney = 1000; RewardInfluence = 2; RewardDebt = 1000; }
        public override string Id => "outside_only";
        public override string Name => "The Long Way Round";
        public override string Describe(IGameHost host) { return "Finish the next three spins ahead using only outside bets (colours, halves, parity, dozens, columns)."; }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            foreach (BetOutcome b in r.Bets) if (!b.Selection.IsOutside) return ChallengeStatus.Lost;
            c.Accumulated += r.Net;
            c.Progress++;
            if (c.Progress < 3) return ChallengeStatus.Active;
            return c.Accumulated > 0 ? ChallengeStatus.Won : ChallengeStatus.Lost;
        }
    }

    /// <summary>Bet during a wheel transformation, and win with that bet.</summary>
    public sealed class BetDuringTransformation : ChallengeDefinition
    {
        public BetDuringTransformation() { Rounds = 1; RewardMoney = 600; RewardInfluence = 3; MinStage = 2; }
        public override string Id => "bet_mid_transform";
        public override string Name => "Moving Target";
        public override string Describe(IGameHost host) { return "Next round the wheel will transform while betting is open. Win with a bet placed after the transformation starts."; }

        public override void OnAccept(ChallengeInstance c, IGameHost host) { host.ScheduleLiveTransformation(); }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            foreach (BetOutcome b in r.Bets) if (b.Won && host.PlacedDuringTransformation(b.Selection)) return ChallengeStatus.Won;
            return ChallengeStatus.Lost;
        }
    }

    /// <summary>Beat a 72-pocket wheel: three rounds on 72 pockets, finishing ahead.</summary>
    public sealed class BeatSeventyTwo : ChallengeDefinition
    {
        public BeatSeventyTwo() { Rounds = 3; RewardMoney = 1500; RewardInfluence = 3; RewardDebt = 1000; MinStage = 2; UnlockId = "challenge_seventy_two"; }
        public override string Id => "beat_72";
        public override string Name => "Seventy-Two";
        public override string Describe(IGameHost host) { return "The wheel grows to 72 pockets for three rounds. Finish those rounds ahead."; }

        public override bool IsAvailable(IGameHost host) { return base.IsAvailable(host) && !host.Rules.IsActive("expansion") && host.Table.Wheel.PocketCount != 72; }

        public override void OnAccept(ChallengeInstance c, IGameHost host) { host.ActivateRule("expansion", true); }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            c.Accumulated += r.Net;
            c.Progress++;
            if (c.Progress < 3) return ChallengeStatus.Active;
            return c.Accumulated > 0 ? ChallengeStatus.Won : ChallengeStatus.Lost;
        }
    }

    /// <summary>Win without using player abilities.</summary>
    public sealed class NoAbilities : ChallengeDefinition
    {
        public NoAbilities() { Rounds = 4; RewardMoney = 800; RewardInfluence = 4; RewardDebt = 1000; }
        public override string Id => "no_abilities";
        public override string Name => "Bare Hands";
        public override string Describe(IGameHost host) { return "Finish the next four spins ahead without using a single ability."; }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            c.Accumulated += r.Net;
            c.Progress++;
            if (c.Progress < 4) return ChallengeStatus.Active;
            return c.Accumulated > 0 ? ChallengeStatus.Won : ChallengeStatus.Lost;
        }

        public override ChallengeStatus OnAbilityUsed(ChallengeInstance c, IGameHost host) { return ChallengeStatus.Lost; }
    }

    /// <summary>Reach a specific balance within a limited number of rounds.</summary>
    public sealed class ReachBalance : ChallengeDefinition
    {
        public ReachBalance() { Rounds = 4; RewardInfluence = 2; RewardDebt = 2500; RewardItem = "upgrade:rule_peek"; }
        public override string Id => "reach_balance";
        public override string Name => "The Target";
        public override string Describe(IGameHost host) { return "Reach a balance of $" + TargetFor(host).ToString("N0") + " within four rounds."; }

        private static long TargetFor(IGameHost host) { return Math.Max(1000, host.Ledger.Balance * 3 / 2 / 100 * 100); }

        public override void OnAccept(ChallengeInstance c, IGameHost host) { c.Target = TargetFor(host); }

        public override ChallengeStatus OnRoundResolved(ChallengeInstance c, RoundResult r, IGameHost host)
        {
            return host.Ledger.Balance >= c.Target ? ChallengeStatus.Won : ChallengeStatus.Active;
        }
    }

    public static class ChallengeCatalog
    {
        public static IReadOnlyList<ChallengeDefinition> CreateDefault()
        {
            return new ChallengeDefinition[]
            {
                new PredictThree(), new SurviveFive(), new OutsideOnly(), new BetDuringTransformation(), new BeatSeventyTwo(), new NoAbilities(), new ReachBalance()
            };
        }
    }

    /// <summary>The offered challenge, the active one, and the record of every challenge this run.</summary>
    public sealed class ChallengeBoard
    {
        private readonly List<ChallengeDefinition> _catalog;
        private readonly List<ChallengeInstance> _history = new List<ChallengeInstance>();

        public IReadOnlyList<ChallengeDefinition> Catalog => _catalog;
        public ChallengeDefinition Offer { get; private set; }
        public ChallengeInstance Active { get; private set; }
        public IReadOnlyList<ChallengeInstance> History => _history;
        public int Won { get; private set; }

        public event Action<ChallengeInstance, ChallengeReward> Completed;
        public event Action<ChallengeInstance> Failed;

        public ChallengeBoard(IEnumerable<ChallengeDefinition> catalog)
        {
            _catalog = new List<ChallengeDefinition>(catalog ?? ChallengeCatalog.CreateDefault());
        }

        public ChallengeDefinition Get(string id)
        {
            foreach (ChallengeDefinition c in _catalog) if (c.Id == id) return c;
            return null;
        }

        /// <summary>Offers a random available challenge the player has not already tried this run.</summary>
        public ChallengeDefinition MakeOffer(IGameHost host, Func<string, bool> isUnlocked)
        {
            if (Active != null) return null;
            var options = new List<ChallengeDefinition>();
            foreach (ChallengeDefinition c in _catalog)
            {
                if (!c.IsAvailable(host)) continue;
                if (c.UnlockId != null && (isUnlocked == null || !isUnlocked(c.UnlockId))) continue;
                bool tried = false;
                foreach (ChallengeInstance h in _history) if (h.Definition == c) tried = true;
                if (!tried) options.Add(c);
            }
            Offer = options.Count == 0 ? null : options[host.Random.NextInt(options.Count)];
            if (Offer != null) host.Say(DialogueTrigger.ChallengeOffer, Offer.Id, new Dictionary<string, string> { { "name", Offer.Name } });
            return Offer;
        }

        public void Decline() { Offer = null; }

        public ChallengeInstance Accept(IGameHost host)
        {
            if (Offer == null || Active != null) return null;
            Active = new ChallengeInstance(Offer, host.CurrentRound);
            _history.Add(Active);
            Offer = null;
            Active.Definition.OnAccept(Active, host);
            return Active;
        }

        public void RoundResolved(RoundResult result, IGameHost host)
        {
            if (Active == null) return;
            Settle(Active.Definition.OnRoundResolved(Active, result, host), host);
        }

        public void AbilityUsed(IGameHost host)
        {
            if (Active == null) return;
            Settle(Active.Definition.OnAbilityUsed(Active, host), host);
        }

        /// <summary>Counts down at the end of each round; a challenge that runs out is decided by its time-up rule.</summary>
        public void RoundEnded(IGameHost host)
        {
            if (Active == null) return;
            Active.RoundsLeft--;
            if (Active.RoundsLeft <= 0) Settle(Active.Definition.OnTimeUp(Active, host), host);
        }

        private void Settle(ChallengeStatus status, IGameHost host)
        {
            if (status == ChallengeStatus.Active || Active == null) return;
            ChallengeInstance c = Active;
            c.Status = status;
            Active = null;
            if (status == ChallengeStatus.Won)
            {
                Won++;
                ChallengeReward reward = c.Definition.RewardAt(host.Stage);
                Pay(reward, host);
                host.Say(DialogueTrigger.ChallengeWon, c.Definition.Id);
                host.Notify("Challenge complete: " + c.Definition.Name + ". You receive " + reward.Describe() + ".");
                Completed?.Invoke(c, reward);
            }
            else
            {
                host.Say(DialogueTrigger.ChallengeLost, c.Definition.Id);
                host.Notify("Challenge failed: " + c.Definition.Name + ".");
                Failed?.Invoke(c);
            }
        }

        public static void Pay(ChallengeReward reward, IGameHost host)
        {
            if (reward.Money > 0) host.Ledger.Credit(reward.Money, TransactionKind.Reward, "Challenge reward");
            if (reward.Influence > 0) host.Influence.Earn(reward.Influence, "Challenge reward");
            if (reward.DebtReduction > 0) host.Ledger.ForgiveDebt(reward.DebtReduction, "Challenge reward");
            if (string.IsNullOrEmpty(reward.Item)) return;
            if (reward.Item == "golden_ticket") host.Inventory.AddGoldenTicket();
            else if (reward.Item.StartsWith("upgrade:", StringComparison.Ordinal))
            {
                string id = reward.Item.Substring(8);
                if (host.Inventory.HasUpgrade(id)) host.Influence.Earn(2, "Challenge reward (already owned)");
                else host.Inventory.AddUpgrade(id);
            }
            else host.Inventory.AddCharges(reward.Item, 1);
        }

        public void Restore(IEnumerable<ChallengeInstance> history, ChallengeInstance active, int won, ChallengeDefinition offer = null)
        {
            _history.Clear();
            if (history != null) _history.AddRange(history);
            Active = active;
            Won = won;
            Offer = offer;
        }
    }
}

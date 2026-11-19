using System;
using System.Collections.Generic;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.Specials
{
    /// <summary>
    /// A casino-wide complication. Trigger conditions, duration, rewards, penalties, probability and
    /// difficulty requirements are data on the definition; the behaviour is a few small hooks.
    /// </summary>
    public abstract class EventDefinition
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public int MinStage { get; set; } = 1;
        public int MaxStage { get; set; } = 5;
        /// <summary>Relative chance of being picked at stage 1.</summary>
        public double BaseWeight { get; set; } = 1.0;
        /// <summary>Weight added per stage after the first (dangerous events grow more common).</summary>
        public double WeightPerStage { get; set; }
        /// <summary>Rounds the event lasts (1: this round only).</summary>
        public int Duration { get; set; } = 1;
        /// <summary>1 (mild) to 3 (dangerous). Shown on the event banner.</summary>
        public int Danger { get; set; } = 1;
        public string RewardText { get; set; } = "";
        public string PenaltyText { get; set; } = "";

        public double WeightAt(int stage) { return Math.Max(0, BaseWeight + WeightPerStage * (stage - 1)); }

        public virtual bool CanTrigger(IGameHost host) { return host.Stage >= MinStage && host.Stage <= MaxStage; }

        /// <summary>Called as the round begins, before betting opens.</summary>
        public abstract void Begin(EventInstance instance, IGameHost host);

        /// <summary>Called as the spin starts with every bet locked in.</summary>
        public virtual void OnSpinStarting(EventInstance instance, IGameHost host) { }

        /// <summary>Called after each resolved spin. Return true to ask for another spin with the same bets (The Double Spin).</summary>
        public virtual bool OnRoundResolved(EventInstance instance, RoundResult result, IGameHost host) { return false; }

        /// <summary>Called when the event ends.</summary>
        public virtual void End(EventInstance instance, IGameHost host) { }
    }

    public sealed class EventInstance
    {
        public EventDefinition Definition { get; }
        public int StartRound { get; }
        public int RoundsLeft { get; set; }
        /// <summary>Free counter for the event's own bookkeeping (spins taken, money collected).</summary>
        public long Value { get; set; }

        public EventInstance(EventDefinition definition, int startRound)
        {
            Definition = definition;
            StartRound = startRound;
            RoundsLeft = Math.Max(1, definition.Duration);
        }
    }

    // ------------------------------------------------------------------ the seven events

    public sealed class TheInspection : EventDefinition
    {
        public TheInspection() { MinStage = 2; BaseWeight = 1.0; WeightPerStage = 0.3; Duration = 1; Danger = 2; PenaltyText = "Your favourite bet type is closed for two rounds (three from stage 4)."; }
        public override string Id => "inspection";
        public override string Name => "The Inspection";
        public override string Description => "The Game Master studies your betting history and closes the bet you rely on most.";

        public override bool CanTrigger(IGameHost host) { return base.CanTrigger(host) && host.Profile.TotalStaked > 0 && host.Profile.RoundsObserved >= 2; }

        public override void Begin(EventInstance instance, IGameHost host)
        {
            Betting.BetType type = host.Profile.FavoriteType;
            RuleDefinition rule = host.Rules.Catalog.Get("event_inspection");
            if (rule != null) rule.Duration = host.Stage >= 4 ? 3 : 2;
            if (host.ActivateRule("event_inspection", true, (int)type)) host.Notify("The Inspection: " + type + " bets are closed.");
            else host.Notify("The Inspection finds nothing it can close.");
        }
    }

    public sealed class TheGenerousHouse : EventDefinition
    {
        public TheGenerousHouse() { BaseWeight = 1.0; WeightPerStage = -0.1; Danger = 1; RewardText = "Winning bets pay 50% more..."; PenaltyText = "...under a hidden condition."; }
        public override string Id => "generous_house";
        public override string Name => "The Generous House";
        public override string Description => "Payouts rise by half for one round. There is a condition he does not mention.";

        public override void Begin(EventInstance instance, IGameHost host)
        {
            host.ActivateRule("event_generous_house", true);
        }

        public override bool OnRoundResolved(EventInstance instance, RoundResult result, IGameHost host)
        {
            Wheel.Pocket p = result.Pocket;
            if (!p.IsZero && p.Number % 2 == 0) host.Notify("The fine print: even pockets paid only 60% this round.");
            return false;
        }
    }

    public sealed class TheSilentSpin : EventDefinition
    {
        public TheSilentSpin() { MinStage = 2; BaseWeight = 0.8; WeightPerStage = 0.2; Danger = 2; RewardText = "Win the round to earn 1 Influence."; PenaltyText = "No announcements and no countdown; the wheel changes in silence."; }
        public override string Id => "silent_spin";
        public override string Name => "The Silent Spin";
        public override string Description => "No announcements. No countdown. Something on the table has changed; find it.";

        private static readonly string[] SilentChanges = { "number_shuffle", "color_reversal", "move_numbers", "uneven_distribution" };

        public override void Begin(EventInstance instance, IGameHost host)
        {
            host.ActivateRule("event_silent_spin", true);
            var pool = new List<string>(SilentChanges);
            while (pool.Count > 0)
            {
                int i = host.Random.NextInt(pool.Count);
                string id = pool[i];
                pool.RemoveAt(i);
                if (host.Rules.IsActive(id)) continue;
                if (host.ActivateRule(id, true))
                {
                    instance.Value = Array.IndexOf(SilentChanges, id);
                    return;
                }
            }
            instance.Value = -1;
        }

        public override bool OnRoundResolved(EventInstance instance, RoundResult result, IGameHost host)
        {
            if (instance.Value >= 0)
            {
                RuleDefinition rule = host.Rules.Catalog.Get(SilentChanges[instance.Value]);
                host.Notify("In the silence: " + (rule != null ? rule.Name : "something changed") + ".");
            }
            if (result.Net > 0) host.Influence.Earn(1, "Read the silent table");
            return false;
        }
    }

    public sealed class TheAuction : EventDefinition
    {
        public TheAuction() { MinStage = 2; BaseWeight = 0.9; Danger = 1; RewardText = "Buy an advantage or the removal of a rule."; PenaltyText = "Winning bids are paid in cash."; }
        public override string Id => "auction";
        public override string Name => "The Auction";
        public override string Description => "The Game Master auctions favours. Each lot has a hidden reserve price; one sealed bid per lot.";

        public override void Begin(EventInstance instance, IGameHost host)
        {
            host.Notify("The Auction is open until the spin.");
        }

        public override void OnSpinStarting(EventInstance instance, IGameHost host)
        {
            host.Notify("The Auction is closed.");
        }
    }

    public sealed class TheHouseCollects : EventDefinition
    {
        public TheHouseCollects() { MinStage = 2; BaseWeight = 0.6; WeightPerStage = 0.35; Danger = 3; PenaltyText = "The house takes 8% of your balance, plus 2% per stage."; }
        public override string Id => "house_collects";
        public override string Name => "The House Collects";
        public override string Description => "A collection. The house takes a percentage of everything you hold.";

        public static int PercentAt(int stage) { return 8 + 2 * stage; }

        public override bool CanTrigger(IGameHost host) { return base.CanTrigger(host) && host.Ledger.Balance > host.Run.EffectiveMinimumBet * 4; }

        public override void Begin(EventInstance instance, IGameHost host)
        {
            int percent = PercentAt(host.Stage);
            long amount = host.Ledger.Balance * percent / 100;
            if (amount <= 0) return;
            instance.Value = host.Ledger.ForceDebit(amount, TransactionKind.Penalty, "The House Collects");
            host.Notify("The House Collects " + percent + "%: $" + instance.Value.ToString("N0") + ".");
        }
    }

    public sealed class TheDoubleSpin : EventDefinition
    {
        public TheDoubleSpin() { MinStage = 2; BaseWeight = 0.8; WeightPerStage = 0.1; Danger = 2; RewardText = "Two spins for one betting window."; PenaltyText = "Your bets ride again automatically."; }
        public override string Id => "double_spin";
        public override string Name => "The Double Spin";
        public override string Description => "Two spins, one betting phase. Whatever you bet is placed again for the second spin, as far as your balance allows.";

        public override void Begin(EventInstance instance, IGameHost host) { instance.Value = 0; }

        public override bool OnRoundResolved(EventInstance instance, RoundResult result, IGameHost host)
        {
            instance.Value++;
            return instance.Value == 1 && result.Bets.Count > 0;
        }
    }

    public sealed class TheUnluckyGuest : EventDefinition
    {
        public TheUnluckyGuest() { BaseWeight = 0.7; Danger = 1; RewardText = "A long-odds contract: $15,000."; PenaltyText = "Costs $1,000 to sign."; }
        public override string Id => "unlucky_guest";
        public override string Name => "The Unlucky Guest";
        public override string Description => "A stranger in a wet coat sits down beside you and slides a contract across the felt.";

        public override bool CanTrigger(IGameHost host) { return base.CanTrigger(host) && host.Ledger.Balance >= 1500 && host.Contracts.Offer == null; }

        public override void Begin(EventInstance instance, IGameHost host)
        {
            host.Contracts.Offer = ContractCatalog.Get("strangers_wager");
            host.Notify("A stranger offers you a contract.");
        }

        public override void End(EventInstance instance, IGameHost host)
        {
            if (host.Contracts.Offer != null && host.Contracts.Offer.Id == "strangers_wager") host.Contracts.Offer = null;
        }
    }

    public static class EventCatalog
    {
        public static IReadOnlyList<EventDefinition> CreateDefault()
        {
            return new EventDefinition[]
            {
                new TheInspection(), new TheGenerousHouse(), new TheSilentSpin(), new TheAuction(), new TheHouseCollects(), new TheDoubleSpin(), new TheUnluckyGuest()
            };
        }
    }

    // ------------------------------------------------------------------ the auction

    public enum AuctionLotKind
    {
        RemoveRule,
        Influence,
        GoldenTicket,
        Charge
    }

    public sealed class AuctionLot
    {
        public string Name { get; }
        public string Description { get; }
        public AuctionLotKind Kind { get; }
        public string Parameter { get; }
        /// <summary>The hidden reserve price. Not shown to the player.</summary>
        internal long Reserve { get; }
        public bool Closed { get; internal set; }
        public bool Won { get; internal set; }

        public AuctionLot(string name, string description, AuctionLotKind kind, string parameter, long reserve)
        {
            Name = name;
            Description = description;
            Kind = kind;
            Parameter = parameter;
            Reserve = reserve;
        }

        /// <summary>The reserve, for tests and the end-of-run records.</summary>
        public long RevealReserve() { return Reserve; }
    }

    /// <summary>
    /// The Auction: three lots, each with a hidden reserve price. One sealed bid per lot; a bid at or
    /// above the reserve wins and is paid, a lower bid is refused and costs nothing.
    /// </summary>
    public sealed class AuctionState
    {
        private readonly List<AuctionLot> _lots = new List<AuctionLot>();
        public IReadOnlyList<AuctionLot> Lots => _lots;
        public bool Open { get; internal set; } = true;

        public static AuctionState Create(IGameHost host)
        {
            var a = new AuctionState();
            long unit = 400L * host.Stage;
            RuleInstance worst = null;
            foreach (RuleInstance r in host.Rules.Active)
            {
                if (!r.Definition.Negative || r.Definition.Permanent || !r.IsVisible) continue;
                if (worst == null || r.Definition.AuthorityCost > worst.Definition.AuthorityCost) worst = r;
            }
            if (worst != null) a._lots.Add(new AuctionLot("Lift: " + worst.Definition.Name, "The rule is removed at once.", AuctionLotKind.RemoveRule, worst.Id, Reserve(host, unit * 2)));
            a._lots.Add(new AuctionLot("Three Influence", "Whispered favours.", AuctionLotKind.Influence, "3", Reserve(host, unit)));
            a._lots.Add(host.Stage >= 3
                ? new AuctionLot("A Golden Ticket", "Cancels his next intervention.", AuctionLotKind.GoldenTicket, null, Reserve(host, unit * 3))
                : new AuctionLot("Double Down charge", "One free Double Down.", AuctionLotKind.Charge, "double_down", Reserve(host, unit)));
            return a;
        }

        private static long Reserve(IGameHost host, long baseAmount)
        {
            double factor = 0.6 + host.Random.NextDouble() * 0.8;
            return Math.Max(50, (long)(baseAmount * factor) / 10 * 10);
        }

        /// <summary>Places the one sealed bid on a lot. Returns the message for the player.</summary>
        public string Bid(int lotIndex, long amount, IGameHost host)
        {
            if (!Open) return "The auction is closed.";
            if (lotIndex < 0 || lotIndex >= _lots.Count) return "No such lot.";
            AuctionLot lot = _lots[lotIndex];
            if (lot.Closed) return "You already bid on that lot.";
            if (amount <= 0 || !host.Ledger.CanAfford(amount)) return "You cannot cover that bid.";
            lot.Closed = true;
            if (amount < lot.Reserve) return "Refused. Your bid was below the reserve.";
            host.Ledger.TryDebit(amount, TransactionKind.Purchase, "Auction: " + lot.Name);
            lot.Won = true;
            switch (lot.Kind)
            {
                case AuctionLotKind.RemoveRule:
                    host.Rules.Cancel(lot.Parameter);
                    host.Rules.Recompose();
                    host.Table.SetModifiers(host.Rules.EffectiveModifiers);
                    break;
                case AuctionLotKind.Influence: host.Influence.Earn(int.Parse(lot.Parameter), "Auction"); break;
                case AuctionLotKind.GoldenTicket: host.Inventory.AddGoldenTicket(); break;
                case AuctionLotKind.Charge: host.Inventory.AddCharges(lot.Parameter, 1); break;
            }
            return "Sold, for $" + amount.ToString("N0") + ": " + lot.Name + ".";
        }
    }

    /// <summary>Rolls, starts, runs and ends events.</summary>
    public sealed class EventDirector
    {
        private readonly List<EventDefinition> _catalog;
        private readonly List<EventInstance> _active = new List<EventInstance>();
        private readonly List<string> _history = new List<string>();
        private EventDefinition _forced;

        public IReadOnlyList<EventDefinition> Catalog => _catalog;
        public IReadOnlyList<EventInstance> Active => _active;
        public IReadOnlyList<string> History => _history;
        public AuctionState Auction { get; private set; }
        /// <summary>Per-event weight multipliers chosen for this run (run randomisation).</summary>
        public Dictionary<string, double> WeightScale { get; } = new Dictionary<string, double>();

        public event Action<EventInstance> Started;
        public event Action<EventInstance> Ended;

        public EventDirector(IEnumerable<EventDefinition> catalog)
        {
            _catalog = new List<EventDefinition>(catalog ?? EventCatalog.CreateDefault());
        }

        public EventDefinition Get(string id)
        {
            foreach (EventDefinition e in _catalog) if (e.Id == id) return e;
            return null;
        }

        public bool IsActive(string id)
        {
            foreach (EventInstance e in _active) if (e.Definition.Id == id) return true;
            return false;
        }

        /// <summary>Queues an event (a mystery pocket's) to start at the beginning of the next round.</summary>
        public void Force(EventDefinition definition) { _forced = definition; }

        /// <summary>The event queued for the next round, or null.</summary>
        public EventDefinition Forced => _forced;

        /// <summary>Picks a random event that can trigger now, by weight, or null.</summary>
        public EventDefinition PickRandom(IGameHost host, ICollection<string> pool)
        {
            double total = 0;
            var options = new List<KeyValuePair<EventDefinition, double>>();
            foreach (EventDefinition e in _catalog)
            {
                if (pool != null && !pool.Contains(e.Id)) continue;
                if (IsActive(e.Id) || !e.CanTrigger(host)) continue;
                double scale;
                double w = e.WeightAt(host.Stage) * (WeightScale.TryGetValue(e.Id, out scale) ? scale : 1.0);
                if (w <= 0) continue;
                options.Add(new KeyValuePair<EventDefinition, double>(e, w));
                total += w;
            }
            if (options.Count == 0) return null;
            double roll = host.Random.NextDouble() * total;
            foreach (KeyValuePair<EventDefinition, double> o in options)
            {
                roll -= o.Value;
                if (roll <= 0) return o.Key;
            }
            return options[options.Count - 1].Key;
        }

        /// <summary>At the start of a round: starts a queued event, or rolls for a new one with the stage's chance.</summary>
        public EventInstance RollForRound(IGameHost host, double chance, ICollection<string> pool)
        {
            EventDefinition chosen = null;
            if (_forced != null && _forced.CanTrigger(host) && !IsActive(_forced.Id)) chosen = _forced;
            _forced = null;
            if (chosen == null && host.Random.NextDouble() < chance) chosen = PickRandom(host, pool);
            return chosen == null ? null : Start(chosen, host);
        }

        public EventInstance Start(EventDefinition definition, IGameHost host)
        {
            var instance = new EventInstance(definition, host.CurrentRound);
            _active.Add(instance);
            _history.Add(definition.Id);
            if (definition is TheAuction) Auction = AuctionState.Create(host);
            definition.Begin(instance, host);
            host.Say(DialogueTrigger.EventStart, definition.Id, new Dictionary<string, string> { { "name", definition.Name } });
            Started?.Invoke(instance);
            return instance;
        }

        public void SpinStarting(IGameHost host)
        {
            foreach (EventInstance e in _active.ToArray()) e.Definition.OnSpinStarting(e, host);
            if (Auction != null) Auction.Open = false;
        }

        /// <summary>Returns true if any event asks for a respin.</summary>
        public bool RoundResolved(RoundResult result, IGameHost host)
        {
            bool respin = false;
            foreach (EventInstance e in _active.ToArray()) respin |= e.Definition.OnRoundResolved(e, result, host);
            return respin;
        }

        public void RoundEnded(IGameHost host)
        {
            foreach (EventInstance e in _active.ToArray())
            {
                e.RoundsLeft--;
                if (e.RoundsLeft > 0) continue;
                _active.Remove(e);
                e.Definition.End(e, host);
                Ended?.Invoke(e);
            }
            if (!IsActive("auction")) Auction = null;
        }

        public void RestoreHistory(IEnumerable<string> history)
        {
            _history.Clear();
            if (history != null) _history.AddRange(history);
            _active.Clear();
            Auction = null;
        }
    }
}

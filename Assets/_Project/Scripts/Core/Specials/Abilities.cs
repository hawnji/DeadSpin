using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Rules;

namespace DeadSpin.Core.Specials
{
    /// <summary>
    /// Information the player has bought this round with abilities. The HUD reads it; it resets
    /// at the end of each round.
    /// </summary>
    public sealed class RoundInsight
    {
        /// <summary>House Inspection: hidden rules and the full wheel configuration are shown.</summary>
        public bool RevealWheel;
        /// <summary>Wheel Analysis: exact probabilities are shown on the layout.</summary>
        public bool ShowProbabilities;
        /// <summary>House Audit: every payout modifier, hidden ones included, is shown on each bet.</summary>
        public bool AuditPayouts;
        /// <summary>Chip Anchor: table manipulation cannot move chips this round.</summary>
        public bool ChipAnchor;
        /// <summary>Rule Peek: the name of the intervention planned for next round.</summary>
        public string PeekedIntervention;

        public void Reset()
        {
            RevealWheel = false;
            ShowProbabilities = false;
            AuditPayouts = false;
            ChipAnchor = false;
            PeekedIntervention = null;
        }
    }

    public enum AbilityKind
    {
        /// <summary>Owned permanently once bought; each use costs Influence and starts a cooldown.</summary>
        Upgrade,
        /// <summary>Bought as charges; each use spends one charge and no Influence.</summary>
        Consumable
    }

    public enum AbilityTiming
    {
        /// <summary>Only while betting is open.</summary>
        Betting,
        /// <summary>While betting is open or just after it closes, before the spin.</summary>
        BeforeSpin,
        /// <summary>Between rounds, or any time before the spin.</summary>
        BetweenOrBeforeSpin
    }

    public enum AbilityTarget
    {
        None,
        Number,
        Bet
    }

    /// <summary>
    /// A player ability. Costs, cooldowns and limits are plain data so they can be tuned from Unity;
    /// the effect is one small method. New abilities subclass this and register in
    /// <see cref="AbilityCatalog"/>.
    /// </summary>
    public abstract class AbilityDefinition
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract AbilityKind Kind { get; }
        public int InfluenceCost { get; set; }
        /// <summary>Rounds before it can be used again (0: once per round).</summary>
        public int Cooldown { get; set; }
        /// <summary>Uses allowed per run, or -1 for unlimited.</summary>
        public int MaxUsesPerRun { get; set; } = -1;
        public virtual AbilityTiming Timing => AbilityTiming.Betting;
        public virtual AbilityTarget Target => AbilityTarget.None;
        /// <summary>Single-key hotkey hint for the HUD.</summary>
        public string Hotkey { get; set; } = "";

        /// <summary>Null when the ability can be used right now, otherwise why not (beyond cost and cooldown).</summary>
        public virtual string CanUse(IGameHost host, int number, BetSelection bet) { return null; }

        /// <summary>Applies the effect. Returns the message for the HUD.</summary>
        public abstract string Execute(IGameHost host, int number, BetSelection bet);
    }

    public sealed class AbilityResult
    {
        public bool Success { get; }
        public string Message { get; }

        private AbilityResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static AbilityResult Ok(string message) { return new AbilityResult(true, message); }
        public static AbilityResult Fail(string message) { return new AbilityResult(false, message); }
    }

    // ------------------------------------------------------------------ the twelve abilities

    public sealed class LuckyCoin : AbilityDefinition
    {
        public LuckyCoin() { InfluenceCost = 3; Cooldown = 3; Hotkey = "1"; }
        public override string Id => "lucky_coin";
        public override string Name => "Lucky Coin";
        public override string Description => "Pick a number: it gains an extra pocket on the wheel for one spin. A real, visible wheel change; payouts adjust to the new odds.";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTarget Target => AbilityTarget.Number;

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            if (number < 0 || !host.Table.Wheel.Contains(number)) return "Choose a number that is on the wheel.";
            if (host.Rules.IsActive("player_lucky_coin")) return "Your coin is already on the wheel.";
            return host.Rules.CanActivate(host.Rules.Catalog.Get("player_lucky_coin"), number).Success ? null : "The wheel cannot take another pocket.";
        }

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            return host.ActivateRule("player_lucky_coin", true, number) ? number + " now has an extra pocket for this spin." : "The coin bounced off the wheel.";
        }
    }

    public sealed class HouseInspection : AbilityDefinition
    {
        public HouseInspection() { InfluenceCost = 2; Cooldown = 2; Hotkey = "2"; }
        public override string Id => "house_inspection";
        public override string Name => "House Inspection";
        public override string Description => "Reveals every hidden rule and the wheel's full configuration (weights, payout pockets, special pockets).";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            int revealed = 0;
            foreach (RuleInstance r in host.Rules.Active)
            {
                if (r.Definition.Hidden && !r.Revealed)
                {
                    r.Revealed = true;
                    revealed++;
                    host.RecordLoophole("inspection:" + r.Id, "Exposed the hidden rule " + r.Definition.Name + ".");
                }
            }
            host.Insight.RevealWheel = true;
            host.Rules.Recompose();
            host.Table.SetModifiers(host.Rules.EffectiveModifiers);
            return revealed > 0 ? "Inspection found " + revealed + " hidden rule" + (revealed == 1 ? "" : "s") + "." : "Inspection: no hidden rules. The wheel's configuration is shown.";
        }
    }

    public sealed class ChipAnchor : AbilityDefinition
    {
        public ChipAnchor() { InfluenceCost = 2; Cooldown = 1; Hotkey = "3"; }
        public override string Id => "chip_anchor";
        public override string Name => "Chip Anchor";
        public override string Description => "Your chips cannot be moved by shakes or tilts for the rest of the round.";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            host.Insight.ChipAnchor = true;
            host.Table.LockAll(true);
            return "Your chips are anchored.";
        }
    }

    public sealed class TimeExtension : AbilityDefinition
    {
        public const float Seconds = 15f;
        public TimeExtension() { InfluenceCost = 1; Cooldown = 1; Hotkey = "4"; }
        public override string Id => "time_extension";
        public override string Name => "Time Extension";
        public override string Description => "Adds 15 seconds to the betting clock.";
        public override AbilityKind Kind => AbilityKind.Upgrade;

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            return host.Table.Modifiers.SilentSpin ? "There is no clock to extend." : null;
        }

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            host.Run.Round.TryExtendBetting(Seconds);
            return "+" + Seconds.ToString("0") + " seconds.";
        }
    }

    public sealed class RulePeek : AbilityDefinition
    {
        public RulePeek() { InfluenceCost = 2; Cooldown = 2; Hotkey = "5"; }
        public override string Id => "rule_peek";
        public override string Name => "Rule Peek";
        public override string Description => "Reveals the Game Master's next intervention before he makes it.";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            string next = host.PeekNextIntervention();
            host.Insight.PeekedIntervention = next ?? "nothing";
            return next != null ? "Next round he plans: " + next + "." : "He has nothing planned. Yet.";
        }
    }

    public sealed class RuleCancel : AbilityDefinition
    {
        public RuleCancel() { InfluenceCost = 4; Cooldown = 3; MaxUsesPerRun = 3; Hotkey = "6"; }
        public override string Id => "rule_cancel";
        public override string Name => "Rule Cancel";
        public override string Description => "Cancels the intervention he announced this round (or, between rounds, the one he has planned). Three uses per run.";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            string cancelled = host.CancelIntervention();
            if (cancelled == null) return "There was nothing to cancel.";
            host.Say(DialogueTrigger.RuleCancelled, null, new Dictionary<string, string> { { "rule", cancelled } });
            return cancelled + " is cancelled.";
        }

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            return host.Rules.Active.Count == 0 && host.PeekNextIntervention() == null ? "There is no intervention to cancel." : null;
        }
    }

    public sealed class BetInsurance : AbilityDefinition
    {
        public const int Percent = 50;
        public BetInsurance() { Hotkey = "7"; }
        public override string Id => "bet_insurance";
        public override string Name => "Bet Insurance";
        public override string Description => "Half of your largest bet (or a bet you choose) comes back if it loses.";
        public override AbilityKind Kind => AbilityKind.Consumable;
        public override AbilityTiming Timing => AbilityTiming.BeforeSpin;
        public override AbilityTarget Target => AbilityTarget.Bet;

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            return host.Table.Bets.Count == 0 ? "Place a bet first." : null;
        }

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            Wager target = bet != null ? host.Table.Bets.Get(bet) : host.Table.LargestWager();
            if (target == null) target = host.Table.LargestWager();
            host.Table.Insure(target.Selection, Percent);
            return target.Selection.DisplayName + " is insured for " + Percent + "%.";
        }
    }

    public sealed class WheelAnalysis : AbilityDefinition
    {
        public WheelAnalysis() { InfluenceCost = 1; Cooldown = 0; Hotkey = "8"; }
        public override string Id => "wheel_analysis";
        public override string Name => "Wheel Analysis";
        public override string Description => "Shows the exact chance and payout of every spot on the current wheel.";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            host.Insight.ShowProbabilities = true;
            return "Probabilities shown for this round.";
        }
    }

    public sealed class ChipRecall : AbilityDefinition
    {
        public ChipRecall() { Hotkey = "9"; }
        public override string Id => "chip_recall";
        public override string Name => "Chip Recall";
        public override string Description => "Returns every chip that fell off the table to your balance.";
        public override AbilityKind Kind => AbilityKind.Consumable;
        public override AbilityTiming Timing => AbilityTiming.BeforeSpin;

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            return host.Table.DisplacedTotal <= 0 ? "No chips have fallen." : null;
        }

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            long back = host.Table.RecallDisplaced();
            return "$" + back.ToString("N0") + " recovered.";
        }
    }

    public sealed class DoubleDown : AbilityDefinition
    {
        public DoubleDown() { Hotkey = "0"; }
        public override string Id => "double_down";
        public override string Name => "Double Down";
        public override string Description => "Doubles your largest bet (or a bet you choose), even after betting closes and above the table maximum.";
        public override AbilityKind Kind => AbilityKind.Consumable;
        public override AbilityTiming Timing => AbilityTiming.BeforeSpin;
        public override AbilityTarget Target => AbilityTarget.Bet;

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            Wager w = bet != null ? host.Table.Bets.Get(bet) : host.Table.LargestWager();
            if (w == null) w = host.Table.LargestWager();
            if (w == null) return "Place a bet first.";
            return host.Ledger.CanAfford(w.Amount) ? null : "You cannot afford to double it.";
        }

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            Wager w = bet != null ? host.Table.Bets.Get(bet) : host.Table.LargestWager();
            if (w == null) w = host.Table.LargestWager();
            return host.Table.TryDoubleDown(w.Selection) ? w.Selection.DisplayName + " doubled." : "The dealer refused.";
        }
    }

    public sealed class DebtFreeze : AbilityDefinition
    {
        public DebtFreeze() { Hotkey = "F"; }
        public override string Id => "debt_freeze";
        public override string Name => "Debt Freeze";
        public override string Description => "Your debt cannot increase this round: contracts, pockets and penalties cannot add to it.";
        public override AbilityKind Kind => AbilityKind.Consumable;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string CanUse(IGameHost host, int number, BetSelection bet)
        {
            return host.Table.Modifiers.DebtFrozen ? "Your debt is already frozen." : null;
        }

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            return host.ActivateRule("player_debt_freeze", true) ? "Debt frozen for this round." : "The freeze did not take.";
        }
    }

    public sealed class HouseAudit : AbilityDefinition
    {
        public HouseAudit() { InfluenceCost = 2; Cooldown = 2; Hotkey = "A"; }
        public override string Id => "house_audit";
        public override string Name => "House Audit";
        public override string Description => "Reveals every manipulated payout condition, including hidden ones and their loopholes.";
        public override AbilityKind Kind => AbilityKind.Upgrade;
        public override AbilityTiming Timing => AbilityTiming.BetweenOrBeforeSpin;

        public override string Execute(IGameHost host, int number, BetSelection bet)
        {
            host.Insight.AuditPayouts = true;
            int hidden = 0, loopholes = 0;
            foreach (Table.PayoutModifier m in host.Table.Modifiers.PayoutModifiers)
            {
                if (m.Hidden) hidden++;
                if (m.Loophole != null) loopholes++;
            }
            foreach (RuleInstance r in host.Rules.Active)
            {
                if (r.Definition.Hidden && !r.Revealed && r.Definition.Category == RuleCategory.Payout)
                {
                    r.Revealed = true;
                    host.RecordLoophole("audit:" + r.Id, "Audited the hidden payout rule " + r.Definition.Name + ".");
                }
            }
            return "Audit: " + host.Table.Modifiers.PayoutModifiers.Count + " payout change" + (host.Table.Modifiers.PayoutModifiers.Count == 1 ? "" : "s")
                   + ", " + hidden + " hidden, " + loopholes + " with a loophole.";
        }
    }

    /// <summary>Every ability in the game.</summary>
    public static class AbilityCatalog
    {
        public static IReadOnlyList<AbilityDefinition> CreateDefault()
        {
            return new AbilityDefinition[]
            {
                new LuckyCoin(), new HouseInspection(), new ChipAnchor(), new TimeExtension(), new RulePeek(), new RuleCancel(),
                new BetInsurance(), new WheelAnalysis(), new ChipRecall(), new DoubleDown(), new DebtFreeze(), new HouseAudit()
            };
        }
    }

    /// <summary>Checks and uses abilities: ownership, timing, cooldowns, limits and cost.</summary>
    public sealed class AbilitySystem
    {
        private readonly List<AbilityDefinition> _abilities;

        public IReadOnlyList<AbilityDefinition> All => _abilities;

        public event Action<AbilityDefinition, string> Used;

        public AbilitySystem(IEnumerable<AbilityDefinition> abilities)
        {
            _abilities = new List<AbilityDefinition>(abilities ?? AbilityCatalog.CreateDefault());
        }

        public AbilityDefinition Get(string id)
        {
            foreach (AbilityDefinition a in _abilities) if (a.Id == id) return a;
            return null;
        }

        public static int CostOf(AbilityDefinition ability, IGameHost host)
        {
            if (ability.Kind == AbilityKind.Consumable) return 0;
            return (ability.InfluenceCost * host.Table.Modifiers.AbilityCostPercent + 99) / 100;
        }

        public bool Owns(AbilityDefinition ability, Inventory inventory)
        {
            return ability.Kind == AbilityKind.Upgrade ? inventory.HasUpgrade(ability.Id) : inventory.ChargesOf(ability.Id) > 0;
        }

        /// <summary>Null when usable now, otherwise the reason (for the HUD tooltip).</summary>
        public string Check(AbilityDefinition ability, IGameHost host, int number = -1, BetSelection bet = null)
        {
            if (ability == null) return "Unknown ability.";
            if (host.Run.IsOver) return "The run is over.";
            if (!Owns(ability, host.Inventory)) return ability.Kind == AbilityKind.Upgrade ? "You do not own " + ability.Name + "." : "No charges left.";
            RoundPhase phase = host.Run.Round.Phase;
            switch (ability.Timing)
            {
                case AbilityTiming.Betting:
                    if (phase != RoundPhase.Betting) return "Only while betting is open.";
                    break;
                case AbilityTiming.BeforeSpin:
                    if (phase != RoundPhase.Betting && phase != RoundPhase.BettingClosed || host.Table.IsSpinPending) return "Only before the spin.";
                    break;
                case AbilityTiming.BetweenOrBeforeSpin:
                    if (phase == RoundPhase.Spinning || phase == RoundPhase.Resolving || host.Table.IsSpinPending) return "Not while the ball is moving.";
                    break;
            }
            if (ability.MaxUsesPerRun >= 0 && host.Inventory.UsesThisRun(ability.Id) >= ability.MaxUsesPerRun) return "No uses left this run.";
            if (host.Inventory.UsesThisRound(ability.Id) > 0) return "Already used this round.";
            int ready = host.Inventory.ReadyRound(ability.Id);
            if (ready > host.CurrentRound) return "Ready in " + (ready - host.CurrentRound) + " round" + (ready - host.CurrentRound == 1 ? "" : "s") + ".";
            int cost = CostOf(ability, host);
            if (!host.Influence.CanAfford(cost)) return "Needs " + cost + " Influence.";
            return ability.CanUse(host, number, bet);
        }

        public AbilityResult Use(string id, IGameHost host, int number = -1, BetSelection bet = null)
        {
            AbilityDefinition ability = Get(id);
            string problem = Check(ability, host, number, bet);
            if (problem != null) return AbilityResult.Fail(problem);
            int cost = CostOf(ability, host);
            if (ability.Kind == AbilityKind.Consumable)
            {
                if (!host.Inventory.TryConsumeCharge(ability.Id)) return AbilityResult.Fail("No charges left.");
            }
            else if (!host.Influence.TrySpend(cost, ability.Name))
            {
                return AbilityResult.Fail("Needs " + cost + " Influence.");
            }
            string message = ability.Execute(host, number, bet);
            host.Inventory.RecordUse(ability.Id, host.CurrentRound, ability.Cooldown);
            host.Profile.RecordAbility(ability.Id);
            host.Say(DialogueTrigger.AbilityUsed, ability.Id, new Dictionary<string, string> { { "ability", ability.Name } });
            Used?.Invoke(ability, message);
            return AbilityResult.Ok(message);
        }
    }
}

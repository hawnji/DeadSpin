using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Rules
{
    /// <summary>What the engine needs to know about the run to apply and validate rules.</summary>
    public interface IRuleEnvironment
    {
        int Stage { get; }
        int Round { get; }
        long Balance { get; }
        long Debt { get; }
        int RoundsRemaining { get; }
        long ConfiguredMinimumBet { get; }
        long ConfiguredMaximumBet { get; }
        int FavoriteNumber { get; }
        int FavoriteDozen { get; }
        IReadOnlyList<int> History { get; }
    }

    /// <summary>The effective state of the table after every active rule has been applied.</summary>
    public sealed class RuleComposition
    {
        public WheelConfiguration Wheel { get; }
        public TableModifiers Modifiers { get; }
        /// <summary>Rules whose effects could not apply this round (e.g. the wheel had no zero to magnetise).</summary>
        public IReadOnlyList<string> Skipped { get; }

        public RuleComposition(WheelConfiguration wheel, TableModifiers modifiers, IReadOnlyList<string> skipped)
        {
            Wheel = wheel;
            Modifiers = modifiers;
            Skipped = skipped;
        }
    }

    /// <summary>Why an activation was refused, or that it succeeded.</summary>
    public sealed class RuleActivation
    {
        public bool Success { get; }
        public string Reason { get; }
        public RuleInstance Instance { get; }
        /// <summary>A lower-priority rule this one replaced, if any.</summary>
        public RuleInstance Replaced { get; }

        private RuleActivation(bool success, string reason, RuleInstance instance, RuleInstance replaced)
        {
            Success = success;
            Reason = reason;
            Instance = instance;
            Replaced = replaced;
        }

        public static RuleActivation Ok(RuleInstance instance, RuleInstance replaced) { return new RuleActivation(true, null, instance, replaced); }
        public static RuleActivation Fail(string reason) { return new RuleActivation(false, reason, null, null); }
    }

    /// <summary>
    /// The rule engine: the single owner of active house rules. It keeps the base wheel (changed
    /// only by permanent rules and story events) and composes the effective wheel and table
    /// modifiers each round by applying active rules in priority order. It detects conflicts,
    /// refreshes stacked rules, expires them, and refuses any activation that would leave the
    /// player without a legitimate way to win (see <see cref="FairnessValidator"/>).
    /// </summary>
    public sealed class RuleEngine
    {
        private readonly List<RuleInstance> _active = new List<RuleInstance>();
        private readonly IRuleEnvironment _environment;

        public RuleCatalog Catalog { get; }
        public WheelConfiguration BaseWheel { get; private set; }
        public WheelConfiguration EffectiveWheel { get; private set; }
        public TableModifiers EffectiveModifiers { get; private set; }

        public IReadOnlyList<RuleInstance> Active => _active;

        public event Action<RuleInstance> RuleActivated;
        public event Action<RuleInstance> RuleExpired;
        public event Action<RuleInstance> RuleCancelled;

        public RuleEngine(RuleCatalog catalog, WheelConfiguration baseWheel, IRuleEnvironment environment)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            if (baseWheel == null) throw new ArgumentNullException(nameof(baseWheel));
            baseWheel.ThrowIfInvalid();
            BaseWheel = baseWheel;
            RuleComposition first = Compose(null);
            EffectiveWheel = first.Wheel;
            EffectiveModifiers = first.Modifiers;
        }

        public bool IsActive(string ruleId)
        {
            foreach (RuleInstance r in _active) if (r.Id == ruleId) return true;
            return false;
        }

        public RuleInstance Find(string ruleId)
        {
            foreach (RuleInstance r in _active) if (r.Id == ruleId) return r;
            return null;
        }

        private RuleContext ContextFor(RuleInstance instance, WheelConfiguration wheel)
        {
            return new RuleContext(instance, _environment.Stage, _environment.Balance, _environment.ConfiguredMinimumBet,
                _environment.FavoriteNumber, _environment.History, Math.Max(1, wheel.HighestNumber), _environment.FavoriteDozen);
        }

        /// <summary>Applies every active rule (plus an optional candidate) to the base wheel and default modifiers.</summary>
        public RuleComposition Compose(RuleInstance candidate)
        {
            var ordered = new List<RuleInstance>(_active);
            if (candidate != null) ordered.Add(candidate);
            // Stable sort by priority: lower first, so higher priority applies last and wins.
            ordered = StableSortByPriority(ordered);

            var skipped = new List<string>();
            WheelConfiguration wheel = BaseWheel;
            foreach (RuleInstance rule in ordered)
            {
                if (rule.Definition.Permanent && rule != candidate) continue; // already baked into the base wheel
                foreach (RuleEffect effect in rule.Definition.Effects)
                {
                    WheelChange change = effect.ApplyToWheel(wheel, ContextFor(rule, wheel));
                    if (change == null) continue;
                    if (change.Succeeded) wheel = change.After;
                    else skipped.Add(rule.Definition.Name + ": " + change.Error);
                }
            }

            var modifiers = new TableModifiers
            {
                MinimumBet = _environment.ConfiguredMinimumBet,
                MaximumBet = _environment.ConfiguredMaximumBet
            };
            foreach (RuleInstance rule in ordered)
            {
                foreach (RuleEffect effect in rule.Definition.Effects) effect.ApplyToTable(modifiers, ContextFor(rule, wheel));
            }
            FairnessValidator.Clamp(modifiers, _environment);
            return new RuleComposition(wheel, modifiers, skipped);
        }

        private static List<RuleInstance> StableSortByPriority(List<RuleInstance> rules)
        {
            var result = new List<RuleInstance>(rules.Count);
            foreach (RuleInstance rule in rules)
            {
                int at = result.Count;
                while (at > 0 && result[at - 1].Definition.Priority > rule.Definition.Priority) at--;
                result.Insert(at, rule);
            }
            return result;
        }

        /// <summary>Checks an activation without changing anything.</summary>
        public RuleActivation CanActivate(RuleDefinition rule, int parameter = -1, ulong seed = 1)
        {
            if (rule == null) return RuleActivation.Fail("Unknown rule.");
            if (rule.MinStage > _environment.Stage) return RuleActivation.Fail(rule.Name + " is not available until stage " + rule.MinStage + ".");

            RuleInstance conflict = null;
            foreach (RuleInstance active in _active)
            {
                if (active.Id == rule.Id) continue; // stacking the same rule refreshes it
                if (rule.ConflictsWithRule(active.Definition))
                {
                    if (rule.Priority > active.Definition.Priority) { conflict = active; continue; }
                    return RuleActivation.Fail(rule.Name + " conflicts with " + active.Definition.Name + ".");
                }
            }

            var candidate = new RuleInstance(rule, seed, parameter, _environment.Round);
            RuleComposition preview;
            if (conflict != null)
            {
                _active.Remove(conflict);
                preview = Compose(candidate);
                _active.Add(conflict);
            }
            else
            {
                preview = Compose(candidate);
            }

            // A rule whose only purpose is a wheel change must actually be able to change the wheel.
            if (HasWheelEffect(rule) && !HasNonWheelEffect(rule) && rule.InstantAction == RuleInstantAction.None)
            {
                foreach (string s in preview.Skipped) if (s.StartsWith(rule.Name + ":", StringComparison.Ordinal)) return RuleActivation.Fail(s);
            }

            string unfair = FairnessValidator.Check(preview.Wheel, preview.Modifiers, _environment);
            if (unfair != null) return RuleActivation.Fail(rule.Name + " would be unfair: " + unfair);
            return RuleActivation.Ok(candidate, conflict);
        }

        private static bool HasWheelEffect(RuleDefinition rule)
        {
            foreach (RuleEffect e in rule.Effects) if (e is WheelEffect) return true;
            return false;
        }

        private static bool HasNonWheelEffect(RuleDefinition rule)
        {
            foreach (RuleEffect e in rule.Effects) if (!(e is WheelEffect)) return true;
            return false;
        }

        /// <summary>
        /// Activates a rule. Re-activating an active rule refreshes its duration. A conflicting rule of
        /// lower priority is replaced; a higher-priority one blocks the activation. Permanent rules bake
        /// their wheel change into the base wheel now. Call <see cref="Recompose"/> afterwards to get the
        /// wheel change to animate.
        /// </summary>
        public RuleActivation Activate(RuleDefinition rule, int parameter = -1, ulong seed = 1)
        {
            RuleActivation check = CanActivate(rule, parameter, seed);
            if (!check.Success) return check;

            RuleInstance existing = Find(rule.Id);
            if (existing != null && !rule.Permanent)
            {
                existing.RoundsRemaining = Math.Max(existing.RoundsRemaining, rule.Duration);
                RuleActivated?.Invoke(existing);
                return RuleActivation.Ok(existing, null);
            }

            if (check.Replaced != null)
            {
                _active.Remove(check.Replaced);
                RuleCancelled?.Invoke(check.Replaced);
            }

            RuleInstance instance = check.Instance;
            if (rule.Permanent)
            {
                WheelConfiguration wheel = BaseWheel;
                foreach (RuleEffect effect in rule.Effects)
                {
                    WheelChange change = effect.ApplyToWheel(wheel, ContextFor(instance, wheel));
                    if (change != null && change.Succeeded) wheel = change.After;
                }
                BaseWheel = wheel;
                // Permanent rules stay listed for the round they were imposed, then fall off the rules panel.
                instance.RoundsRemaining = Math.Max(1, rule.Duration);
            }
            if (existing != null) _active.Remove(existing);
            _active.Add(instance);
            RuleActivated?.Invoke(instance);
            return RuleActivation.Ok(instance, check.Replaced);
        }

        /// <summary>Removes an active rule early (Rule Cancel, Devil's Bargain). Permanent wheel changes stay.</summary>
        public bool Cancel(string ruleId)
        {
            RuleInstance instance = Find(ruleId);
            if (instance == null) return false;
            _active.Remove(instance);
            RuleCancelled?.Invoke(instance);
            return true;
        }

        /// <summary>Counts down every timed rule at the end of a round and removes the expired ones.</summary>
        public List<RuleInstance> TickRound()
        {
            var expired = new List<RuleInstance>();
            foreach (RuleInstance rule in _active.ToArray())
            {
                if (rule.RoundsRemaining <= 0) continue; // until removed
                rule.RoundsRemaining--;
                if (rule.RoundsRemaining > 0) continue;
                _active.Remove(rule);
                expired.Add(rule);
            }
            foreach (RuleInstance rule in expired) RuleExpired?.Invoke(rule);
            return expired;
        }

        /// <summary>Replaces the base wheel directly (bomb pockets, stage resets). Validated.</summary>
        public bool SetBaseWheel(WheelConfiguration wheel)
        {
            if (wheel == null || !wheel.IsValid) return false;
            BaseWheel = wheel;
            return true;
        }

        /// <summary>
        /// Recomputes the effective wheel and modifiers. Returns the wheel change to animate (null when
        /// the wheel did not change).
        /// </summary>
        public WheelChange Recompose()
        {
            RuleComposition composition = Compose(null);
            WheelConfiguration before = EffectiveWheel;
            EffectiveWheel = composition.Wheel;
            EffectiveModifiers = composition.Modifiers;
            if (ReferenceEquals(before, composition.Wheel) || SameWheel(before, composition.Wheel)) return null;
            return new WheelChange(before, composition.Wheel, Classify(before, composition.Wheel), "The wheel changes.", CloneSourcesFor(before, composition.Wheel));
        }

        private static bool SameWheel(WheelConfiguration a, WheelConfiguration b)
        {
            if (a.PocketCount != b.PocketCount || (a.Ring == null) != (b.Ring == null)) return false;
            for (int i = 0; i < a.PocketCount; i++)
            {
                Pocket p = a.Pockets[i], q = b.Pockets[i];
                if (p.Id != q.Id || p.Number != q.Number || p.Color != q.Color || p.Weight != q.Weight || p.IsActive != q.IsActive || p.Special != q.Special || p.PayoutPercent != q.PayoutPercent) return false;
            }
            return true;
        }

        private static WheelChangeKind Classify(WheelConfiguration a, WheelConfiguration b)
        {
            if (a.PocketCount != b.PocketCount) return b.PocketCount > a.PocketCount ? WheelChangeKind.Add : WheelChangeKind.Remove;
            if ((a.Ring == null) != (b.Ring == null)) return WheelChangeKind.Ring;
            return WheelChangeKind.Rearrange;
        }

        /// <summary>New pockets that show a number already on the wheel emerge from that number's pocket.</summary>
        private static Dictionary<int, int> CloneSourcesFor(WheelConfiguration before, WheelConfiguration after)
        {
            var sources = new Dictionary<int, int>();
            var beforeIds = new HashSet<int>();
            foreach (Pocket p in before.Pockets) beforeIds.Add(p.Id);
            foreach (Pocket p in after.Pockets)
            {
                if (beforeIds.Contains(p.Id)) continue;
                foreach (Pocket q in before.Pockets)
                {
                    if (q.Number == p.Number) { sources[p.Id] = q.Id; break; }
                }
            }
            return sources;
        }

        /// <summary>Restores state from a save.</summary>
        public void Restore(WheelConfiguration baseWheel, IEnumerable<RuleInstance> active)
        {
            if (baseWheel == null || !baseWheel.IsValid) throw new ArgumentException("Saved base wheel is invalid.", nameof(baseWheel));
            BaseWheel = baseWheel;
            _active.Clear();
            if (active != null) _active.AddRange(active);
            RuleComposition composition = Compose(null);
            EffectiveWheel = composition.Wheel;
            EffectiveModifiers = composition.Modifiers;
        }
    }

    /// <summary>
    /// The fairness guarantee: whatever the Game Master stacks up, the player always keeps a
    /// legitimate, mathematically possible way to win. Rules may make the odds terrible; they may
    /// not make winning impossible.
    /// </summary>
    public static class FairnessValidator
    {
        /// <summary>Caps pressure rules so they can never bankrupt the player by themselves.</summary>
        public static void Clamp(TableModifiers modifiers, IRuleEnvironment environment)
        {
            long configured = environment.ConfiguredMinimumBet;
            // The minimum bet may rise, but never above half the player's balance (or the configured minimum).
            long ceiling = Math.Max(configured, environment.Balance / 2);
            if (modifiers.MinimumBet > ceiling) modifiers.MinimumBet = Math.Max(configured, ceiling / 5 * 5);
            if (modifiers.MaximumBet < modifiers.MinimumBet) modifiers.MaximumBet = modifiers.MinimumBet;
            long wagerCeiling = Math.Max(configured, environment.Balance / 2);
            if (modifiers.MandatoryWager > wagerCeiling) modifiers.MandatoryWager = wagerCeiling;
            if (modifiers.BallCount > PayoutPolicy.MaxBalls) modifiers.BallCount = PayoutPolicy.MaxBalls;
        }

        /// <summary>Null when the composition is fair, otherwise the reason it is not.</summary>
        public static string Check(WheelConfiguration wheel, TableModifiers modifiers, IRuleEnvironment environment)
        {
            if (wheel == null || !wheel.IsValid) return "the wheel would be invalid";
            IReadOnlyList<string> errors = modifiers.Validate();
            if (errors.Count > 0) return errors[0];

            // At least one open bet must be able to win and pay something.
            var layout = new BettingLayout(Math.Max(1, wheel.HighestNumber));
            var policy = PayoutPolicy.Standard;
            decimal bestRatio = 0m;
            foreach (BetSpot spot in layout.Spots)
            {
                if (modifiers.IsClosed(spot.Selection)) continue;
                if (PayoutPolicy.WinningWeight(wheel, spot.Selection, layout.HighestNumber) <= 0) continue;
                decimal ratio = policy.PayoutRatio(wheel, spot.Selection, layout.HighestNumber, modifiers.BallCount);
                decimal worst = ratio;
                foreach (PayoutModifier m in modifiers.PayoutModifiers) worst = worst * m.Percent / 100m;
                if (worst > bestRatio) bestRatio = worst;
            }
            if (bestRatio <= 0m) return "no open bet could win anything";

            // The debt must still be payable in the rounds left at the maximum bet, in principle.
            long needed = Math.Max(0, environment.Debt - environment.Balance);
            decimal possible = bestRatio * modifiers.MaximumBet * Math.Max(1, environment.RoundsRemaining);
            if (needed > 0 && possible < needed) return "the debt could no longer be repaid in time";
            return null;
        }
    }
}

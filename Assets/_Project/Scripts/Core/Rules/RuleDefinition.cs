using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Rules
{
    /// <summary>What a rule is mostly about; the Game Master's brain and the UI group rules by this.</summary>
    public enum RuleCategory
    {
        Wheel,
        Payout,
        Betting,
        Table,
        Economy,
        Ability,
        Presentation
    }

    /// <summary>Something a rule does once, when it activates (not every round).</summary>
    public enum RuleInstantAction
    {
        None,
        ShakeTable,
        TiltTable,
        TakeBalancePercent,
        PlaceMagnet
    }

    /// <summary>
    /// A data-driven house rule: identity, presentation, timing, cost, conflicts and the effects it
    /// applies. Definitions are immutable data held by <see cref="RuleCatalog"/>; Unity can override
    /// cost, duration and priority per id from a ScriptableObject without code changes. Active
    /// copies are <see cref="RuleInstance"/>s owned by the <see cref="RuleEngine"/>.
    /// </summary>
    public sealed class RuleDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public RuleCategory Category { get; }

        /// <summary>Rounds the rule lasts once active (1 = this round only). 0 = until removed.</summary>
        public int Duration { get; set; }

        /// <summary>Higher priority rules apply later and so win when two rules touch the same value.</summary>
        public int Priority { get; set; }

        /// <summary>House Authority the Game Master spends to activate it.</summary>
        public int AuthorityCost { get; set; }

        /// <summary>Earliest stage (1-5) the Game Master may use it.</summary>
        public int MinStage { get; set; } = 1;

        /// <summary>Permanent rules bake their wheel change into the base wheel when they activate.</summary>
        public bool Permanent { get; set; }

        /// <summary>Hidden rules are not announced; House Inspection reveals them.</summary>
        public bool Hidden { get; set; }

        /// <summary>The Game Master may trigger it while betting is open, not only between rounds.</summary>
        public bool MidBetting { get; set; }

        /// <summary>Bad for the player: curses and the Devil's Bargain pick from these.</summary>
        public bool Negative { get; set; } = true;

        /// <summary>Rules sharing a tag cannot be active together (e.g. two rules setting the ball count).</summary>
        public IReadOnlyList<string> ConflictTags { get; set; } = new string[0];

        /// <summary>Specific rules this one cannot coexist with.</summary>
        public IReadOnlyList<string> ConflictsWith { get; set; } = new string[0];

        /// <summary>Behaviour hints for the Game Master's decision system (e.g. "anti-favorite", "anti-outside", "punish-win").</summary>
        public IReadOnlyList<string> AiTags { get; set; } = new string[0];

        public IReadOnlyList<RuleEffect> Effects { get; }

        public RuleInstantAction InstantAction { get; set; }
        /// <summary>Strength or percentage used by the instant action.</summary>
        public int InstantValue { get; set; }

        /// <summary>What the Game Master says when he imposes it.</summary>
        public string Announcement { get; set; }

        /// <summary>Visual cue id for the presentation layer (lights, wheel glow...).</summary>
        public string VisualCue { get; set; } = "";

        /// <summary>Audio cue id for the presentation layer.</summary>
        public string AudioCue { get; set; } = "";

        /// <summary>The deliberate gap in this rule, if it has one, in plain words. Revealed by audits and the Records Room.</summary>
        public string LoopholeHint { get; set; }

        public RuleDefinition(string id, string name, string description, RuleCategory category, int duration, int priority, int authorityCost, params RuleEffect[] effects)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Rules need an id.", nameof(id));
            Id = id;
            Name = name ?? id;
            Description = description ?? "";
            Category = category;
            Duration = duration;
            Priority = priority;
            AuthorityCost = authorityCost;
            Effects = effects ?? new RuleEffect[0];
            Announcement = description;
        }

        public bool ConflictsWithRule(RuleDefinition other)
        {
            if (other == null || other.Id == Id) return other != null;
            foreach (string id in ConflictsWith) if (id == other.Id) return true;
            foreach (string id in other.ConflictsWith) if (id == Id) return true;
            foreach (string a in ConflictTags) foreach (string b in other.ConflictTags) if (a == b) return true;
            return false;
        }

        public bool HasAiTag(string tag)
        {
            foreach (string t in AiTags) if (t == tag) return true;
            return false;
        }
    }

    /// <summary>An active rule: its definition, how long it has left and the parameters it was activated with.</summary>
    public sealed class RuleInstance
    {
        public RuleDefinition Definition { get; }
        public int RoundsRemaining { get; set; }
        /// <summary>Seed for any randomness in the rule's effects, fixed at activation so re-applying gives the same wheel.</summary>
        public ulong Seed { get; }
        /// <summary>Activation parameter (target number, section index...), or -1.</summary>
        public int Parameter { get; }
        public int ActivatedRound { get; }
        /// <summary>Revealed by House Inspection (hidden rules only).</summary>
        public bool Revealed { get; set; }

        public RuleInstance(RuleDefinition definition, ulong seed, int parameter, int activatedRound)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            RoundsRemaining = definition.Duration;
            Seed = seed;
            Parameter = parameter;
            ActivatedRound = activatedRound;
        }

        public string Id => Definition.Id;
        public bool IsPermanentLength => Definition.Duration == 0;
        public bool IsVisible => !Definition.Hidden || Revealed;
    }
}

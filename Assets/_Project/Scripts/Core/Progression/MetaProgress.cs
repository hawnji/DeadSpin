using System;
using System.Collections.Generic;
using DeadSpin.Core.Common;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Progression
{
    public enum UnlockCategory
    {
        StartingItem,
        Ability,
        ChipDesign,
        WheelConfiguration,
        Environment,
        Personality,
        SpecialPocket,
        Contract,
        Challenge
    }

    /// <summary>Something unlocked between runs with Marks. Unlocks widen the options rather than make runs easier.</summary>
    public sealed class UnlockDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public UnlockCategory Category { get; }
        public int Cost { get; }
        /// <summary>Only purchasable after reaching this ending at least once (null: no requirement).</summary>
        public string RequiresEnding { get; }

        public UnlockDefinition(string id, string name, string description, UnlockCategory category, int cost, string requiresEnding = null)
        {
            Id = id;
            Name = name;
            Description = description;
            Category = category;
            Cost = cost;
            RequiresEnding = requiresEnding;
        }
    }

    public static class UnlockCatalog
    {
        public static IReadOnlyList<UnlockDefinition> All { get; } = new[]
        {
            new UnlockDefinition("start_insurance", "Starting Item: Insurance", "Begin each run with one Bet Insurance charge.", UnlockCategory.StartingItem, 4),
            new UnlockDefinition("start_magnetic", "Starting Item: Magnetic Chips", "Begin each run with five Magnetic chips.", UnlockCategory.StartingItem, 3),
            new UnlockDefinition("start_anchor", "Starting Item: Chip Anchor", "Begin each run owning the Chip Anchor upgrade.", UnlockCategory.StartingItem, 8),
            new UnlockDefinition("ability_lucky_coin", "Ability: Lucky Coin", "The Shop can stock the Lucky Coin.", UnlockCategory.Ability, 6),
            new UnlockDefinition("ability_debt_freeze", "Ability: Debt Freeze", "The Shop can stock Debt Freeze charges.", UnlockCategory.Ability, 6),
            new UnlockDefinition("chips_weighted", "Chips: Weighted", "The Shop can stock Weighted chips.", UnlockCategory.ChipDesign, 3),
            new UnlockDefinition("chips_fragile", "Chips: Fragile", "The Shop can stock Fragile chips (+10% winnings, shatter when they fall).", UnlockCategory.ChipDesign, 4),
            new UnlockDefinition("design_obsidian", "Chip Design: Obsidian", "Black glass chips with gold rims.", UnlockCategory.ChipDesign, 2),
            new UnlockDefinition("wheel_double_zero", "Wheel: Double Zero", "Runs may start on a 38-pocket wheel with two zeros.", UnlockCategory.WheelConfiguration, 5),
            new UnlockDefinition("wheel_small", "Wheel: The Parlour", "Runs may start on a small 25-pocket wheel.", UnlockCategory.WheelConfiguration, 5),
            new UnlockDefinition("env_crimson", "Casino: Crimson Hall", "A red-lit variant of the roulette room.", UnlockCategory.Environment, 3),
            new UnlockDefinition("env_flooded", "Casino: The Flooded Floor", "Water on the floor, reflections in the dark.", UnlockCategory.Environment, 4, "house_always_wins"),
            new UnlockDefinition("gm_trickster", "Game Master: The Trickster", "He may appear as the Trickster: hidden rules and sleight of hand.", UnlockCategory.Personality, 6),
            new UnlockDefinition("gm_collector", "Game Master: The Collector", "He may appear as the Collector: patient, squeezing your stakes and debt.", UnlockCategory.Personality, 6),
            new UnlockDefinition("pocket_lucky_start", "Special Pocket: The Lucky Start", "Stage 2 begins with an Influence pocket on the wheel instead of a random one.", UnlockCategory.SpecialPocket, 5),
            new UnlockDefinition("contract_loan_shark", "Contract: The Loan Shark", "The Vault can offer the Loan Shark.", UnlockCategory.Contract, 4),
            new UnlockDefinition("challenge_seventy_two", "Challenge: Seventy-Two", "He may challenge you to beat a 72-pocket wheel.", UnlockCategory.Challenge, 4),
            new UnlockDefinition("shop_golden_ticket", "Shop: Golden Tickets", "The Shop can stock Golden Tickets from stage 3.", UnlockCategory.StartingItem, 7, "freedom")
        };

        public static UnlockDefinition Get(string id)
        {
            foreach (UnlockDefinition u in All) if (u.Id == id) return u;
            return null;
        }
    }

    /// <summary>What a finished run is worth for progression.</summary>
    public sealed class RunSummary
    {
        public string EndingId;
        public bool Victory;
        public int RoundsPlayed;
        public long DebtPaid;
        public int ChallengesWon;
        public int SecretsFound;
        public int LoopholesFound;
        public long FinalBalance;
    }

    /// <summary>
    /// Progress kept between runs (roguelite layer): runs played, endings reached, Marks to spend,
    /// unlocks owned and best results. Saved separately from the run.
    /// </summary>
    public sealed class MetaProgress
    {
        private readonly HashSet<string> _unlocked = new HashSet<string>();
        private readonly Dictionary<string, int> _endings = new Dictionary<string, int>();

        public int RunsStarted { get; set; }
        public int RunsFinished { get; set; }
        public int Marks { get; set; }
        public int BestRound { get; set; }
        public long BestDebtPaid { get; set; }

        public IEnumerable<string> Unlocked => _unlocked;
        public IReadOnlyDictionary<string, int> Endings => _endings;

        public bool IsUnlocked(string id) { return id == null || _unlocked.Contains(id); }

        public bool HasReached(string endingId)
        {
            int n;
            return _endings.TryGetValue(endingId, out n) && n > 0;
        }

        /// <summary>Null on success, otherwise why it could not be unlocked.</summary>
        public string TryUnlock(string id)
        {
            UnlockDefinition u = UnlockCatalog.Get(id);
            if (u == null) return "Unknown unlock.";
            if (_unlocked.Contains(id)) return "Already unlocked.";
            if (u.RequiresEnding != null && !HasReached(u.RequiresEnding) && !(u.RequiresEnding == "house_always_wins" && HasReached("house_always_wins_deadline")))
                return "Reach the ending \"" + u.RequiresEnding.Replace('_', ' ') + "\" first.";
            if (Marks < u.Cost) return "Needs " + u.Cost + " Marks.";
            Marks -= u.Cost;
            _unlocked.Add(id);
            return null;
        }

        /// <summary>Marks earned by a run: survival, debt paid, challenges, secrets and the ending.</summary>
        public static int MarksFor(RunSummary s)
        {
            int marks = s.RoundsPlayed / 3 + (int)Math.Min(10, s.DebtPaid / 5000) + s.ChallengesWon + s.SecretsFound / 2 + Math.Min(3, s.LoopholesFound);
            switch (s.EndingId)
            {
                case "new_owner": marks += 12; break;
                case "freedom": marks += 8; break;
                case "loophole": marks += 10; break;
                case "the_deal": marks += 4; break;
            }
            return Math.Max(1, marks);
        }

        public int RecordRun(RunSummary summary)
        {
            RunsFinished++;
            if (!string.IsNullOrEmpty(summary.EndingId))
            {
                int n;
                _endings[summary.EndingId] = _endings.TryGetValue(summary.EndingId, out n) ? n + 1 : 1;
            }
            BestRound = Math.Max(BestRound, summary.RoundsPlayed);
            BestDebtPaid = Math.Max(BestDebtPaid, summary.DebtPaid);
            int earned = MarksFor(summary);
            Marks += earned;
            return earned;
        }

        public void Restore(IEnumerable<string> unlocked, IDictionary<string, int> endings)
        {
            _unlocked.Clear();
            if (unlocked != null) foreach (string u in unlocked) _unlocked.Add(u);
            _endings.Clear();
            if (endings != null) foreach (KeyValuePair<string, int> e in endings) _endings[e.Key] = e.Value;
        }
    }

    /// <summary>
    /// The randomised parameters of one run: starting money and debt, Game Master personality,
    /// starting wheel, event mix, shop inventory seed, final encounter variant, starting kit and
    /// cosmetics. Derived deterministically from the seed and the unlocks owned.
    /// </summary>
    public sealed class RunSetup
    {
        public RunConfig Config;
        public GameMasterPersonality Personality;
        public string StartingWheelId = "european";
        public int FinalVariant;
        public Dictionary<string, double> EventWeights = new Dictionary<string, double>();
        public List<string> StartingUpgrades = new List<string>();
        public Dictionary<string, int> StartingCharges = new Dictionary<string, int>();
        public Dictionary<ChipKind, int> StartingChips = new Dictionary<ChipKind, int>();
        public int StartingInfluence = 2;
        public string EnvironmentId = "classic";
        public string ChipDesignId = "classic";
        public Func<string, bool> IsUnlocked = id => false;

        public static readonly string[] EventIds = { "inspection", "generous_house", "silent_spin", "auction", "house_collects", "double_spin", "unlucky_guest" };

        /// <summary>The standard kit every run starts with.</summary>
        public static readonly string[] BaseUpgrades = { "wheel_analysis", "time_extension", "house_inspection" };

        public static RunSetup Create(ulong seed, MetaProgress meta, RunConfig template = null, bool randomizeNumbers = true)
        {
            RunConfig config = (template ?? RunConfig.CreateDefault()).Clone();
            if (seed == 0) seed = (ulong)DateTime.UtcNow.Ticks;
            config.Seed = seed;
            var random = new SeededRandom(seed ^ 0x5EEDF00DUL);
            var setup = new RunSetup { Config = config };
            setup.IsUnlocked = id => meta != null && meta.IsUnlocked(id);

            if (randomizeNumbers)
            {
                // Debt 45k-60k, balance 2,000-3,000: always the same shape of problem, never the same numbers.
                config.StartingDebt = Math.Max(1000, config.StartingDebt * (90 + random.NextInt(31)) / 100 / 1000 * 1000);
                config.StartingBalance = Math.Max(config.MinimumBet * 10, config.StartingBalance * (80 + random.NextInt(41)) / 100 / 100 * 100);
            }

            var personalities = new List<GameMasterPersonality> { GameMasterPersonality.Showman, GameMasterPersonality.Accountant, GameMasterPersonality.Brute };
            if (setup.IsUnlocked("gm_trickster")) personalities.Add(GameMasterPersonality.Trickster);
            if (setup.IsUnlocked("gm_collector")) personalities.Add(GameMasterPersonality.Collector);
            setup.Personality = personalities[random.NextInt(personalities.Count)];

            var wheels = new List<string> { "european" };
            if (setup.IsUnlocked("wheel_double_zero")) wheels.Add("double_zero");
            if (setup.IsUnlocked("wheel_small")) wheels.Add("parlour");
            setup.StartingWheelId = wheels[random.NextInt(wheels.Count)];

            foreach (string id in EventIds) setup.EventWeights[id] = 0.6 + random.NextDouble() * 0.8;
            setup.FinalVariant = random.NextInt(FinalEncounter.VariantCount);

            setup.StartingUpgrades.AddRange(BaseUpgrades);
            setup.StartingCharges["chip_recall"] = 1;
            if (setup.IsUnlocked("start_insurance")) setup.StartingCharges["bet_insurance"] = 1;
            if (setup.IsUnlocked("start_magnetic")) setup.StartingChips[ChipKind.Magnetic] = 5;
            if (setup.IsUnlocked("start_anchor")) setup.StartingUpgrades.Add("chip_anchor");

            var environments = new List<string> { "classic" };
            if (setup.IsUnlocked("env_crimson")) environments.Add("crimson");
            if (setup.IsUnlocked("env_flooded")) environments.Add("flooded");
            setup.EnvironmentId = environments[random.NextInt(environments.Count)];
            setup.ChipDesignId = setup.IsUnlocked("design_obsidian") ? "obsidian" : "classic";
            return setup;
        }

        public static WheelConfiguration CreateWheel(string id)
        {
            switch (id)
            {
                case "double_zero": return WheelFactory.Create(36, 2);
                case "parlour": return WheelFactory.Create(24, 1);
                default: return StandardWheels.European();
            }
        }
    }
}

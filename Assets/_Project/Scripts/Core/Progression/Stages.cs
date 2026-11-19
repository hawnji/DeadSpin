using System;
using System.Collections.Generic;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.Rules;

namespace DeadSpin.Core.Progression
{
    /// <summary>
    /// One of the five stages of a run. Each has its own rule pool, event pool, difficulty,
    /// financial requirement, dialogue key, new mechanics and environment state.
    /// </summary>
    public sealed class StageDefinition
    {
        public int Number { get; }
        public string Name { get; }
        public string Subtitle { get; }
        public int FirstRound { get; internal set; }
        public int LastRound { get; internal set; }

        public GameMasterDifficulty Difficulty { get; set; }
        /// <summary>Chance per round that an event starts.</summary>
        public double EventChance { get; set; }
        /// <summary>Event ids that may occur in this stage (null: all).</summary>
        public HashSet<string> EventPool { get; set; }
        /// <summary>Rule ids the Game Master may use in this stage (null: every rule whose MinStage allows it).</summary>
        public HashSet<string> RulePool { get; set; }
        /// <summary>Chance per round that he offers a challenge.</summary>
        public double ChallengeChance { get; set; }
        /// <summary>Chance per round that he offers a contract.</summary>
        public double ContractChance { get; set; }
        public bool SpecialBetsAllowed { get; set; }
        public bool ContractsAvailable { get; set; }
        /// <summary>Special pockets added to the wheel when the stage begins.</summary>
        public int SpecialPocketsAtStart { get; set; }
        /// <summary>Pocket count the base wheel is rebuilt to when the stage begins (0: unchanged).</summary>
        public int ResizeWheelTo { get; set; }
        public EnvironmentState Environment { get; set; }
        /// <summary>One line on what is new, for the stage banner.</summary>
        public string NewMechanics { get; set; }

        public StageDefinition(int number, string name, string subtitle)
        {
            Number = number;
            Name = name;
            Subtitle = subtitle;
        }

        public bool AllowsRule(RuleDefinition rule)
        {
            if (rule.MinStage > Number) return false;
            return RulePool == null || RulePool.Contains(rule.Id);
        }

        public int RoundCount => LastRound - FirstRound + 1;
    }

    /// <summary>How the casino looks and behaves in a stage (environmental storytelling).</summary>
    public sealed class EnvironmentState
    {
        /// <summary>0: steady lights, 1: violent flicker.</summary>
        public float LightFlicker;
        /// <summary>Strange markings appear on the walls.</summary>
        public bool WallMarkings;
        /// <summary>Previous players' names are written on the walls.</summary>
        public bool PlayerNames;
        /// <summary>0: relaxed staff, 1: terrified staff.</summary>
        public float StaffUnease;
        /// <summary>0: normal, 1: the room bends.</summary>
        public float Distortion;
        /// <summary>Chance the Game Master is seen somewhere he should not be.</summary>
        public float GameMasterSightings;
        /// <summary>Ambient light tint (RGB hex).</summary>
        public string AmbientTint = "FFE2B0";
    }

    /// <summary>The five stages and the rounds each one covers.</summary>
    public sealed class StageTable
    {
        private readonly List<StageDefinition> _stages = new List<StageDefinition>();
        public IReadOnlyList<StageDefinition> Stages => _stages;
        public int TotalRounds { get; private set; }

        /// <summary>Cumulative share of the run that ends each stage (18 rounds: 3, 7, 11, 15, 18).</summary>
        private static readonly double[] StageEnds = { 3.0 / 18, 7.0 / 18, 11.0 / 18, 15.0 / 18, 1.0 };

        public static StageTable CreateDefault(int totalRounds)
        {
            var t = new StageTable();
            t.Add(new StageDefinition(1, "The Invitation", "Plain roulette. Mostly.")
            {
                Difficulty = GameMasterDifficulty.ForStage(1),
                EventChance = 0.12,
                EventPool = new HashSet<string> { "generous_house", "unlucky_guest" },
                RulePool = new HashSet<string> { "move_numbers", "number_shuffle", "house_shake", "closed_section", "no_takebacks", "higher_stakes", "reverse_rotation", "red_tax", "black_tax", "number_clone" },
                ChallengeChance = 0.25,
                SpecialBetsAllowed = false,
                NewMechanics = "Standard roulette. The Game Master shakes the table and moves numbers.",
                Environment = new EnvironmentState { LightFlicker = 0f, StaffUnease = 0.1f, AmbientTint = "FFE2B0" }
            });
            t.Add(new StageDefinition(2, "The House Rules", "The wheel is his to reshape.")
            {
                Difficulty = GameMasterDifficulty.ForStage(2),
                EventChance = 0.25,
                EventPool = new HashSet<string> { "generous_house", "unlucky_guest", "inspection", "auction", "double_spin", "silent_spin" },
                ChallengeChance = 0.3,
                SpecialBetsAllowed = true,
                SpecialPocketsAtStart = 2,
                NewMechanics = "Special pockets, wheel modifications and special bets.",
                Environment = new EnvironmentState { LightFlicker = 0.15f, StaffUnease = 0.3f, WallMarkings = true, AmbientTint = "FFD49A" }
            });
            t.Add(new StageDefinition(3, "The Debt", "Payments are due.")
            {
                Difficulty = GameMasterDifficulty.ForStage(3),
                EventChance = 0.3,
                ChallengeChance = 0.3,
                ContractChance = 0.3,
                SpecialBetsAllowed = true,
                ContractsAvailable = true,
                SpecialPocketsAtStart = 1,
                NewMechanics = "Mandatory wagers, contracts in the Vault and harder interventions.",
                Environment = new EnvironmentState { LightFlicker = 0.35f, StaffUnease = 0.55f, WallMarkings = true, PlayerNames = true, GameMasterSightings = 0.2f, AmbientTint = "F2B880" }
            });
            t.Add(new StageDefinition(4, "No Turning Back", "Every trick, all at once.")
            {
                Difficulty = GameMasterDifficulty.ForStage(4),
                EventChance = 0.4,
                ChallengeChance = 0.25,
                ContractChance = 0.35,
                SpecialBetsAllowed = true,
                ContractsAvailable = true,
                SpecialPocketsAtStart = 2,
                ResizeWheelTo = 48,
                NewMechanics = "A 48-pocket wheel, stacked rules and casino hazards.",
                Environment = new EnvironmentState { LightFlicker = 0.6f, StaffUnease = 0.8f, WallMarkings = true, PlayerNames = true, Distortion = 0.35f, GameMasterSightings = 0.45f, AmbientTint = "E08C6A" }
            });
            t.Add(new StageDefinition(5, "The Final Table", "Just you and him.")
            {
                Difficulty = GameMasterDifficulty.ForStage(5),
                EventChance = 0.0,
                ChallengeChance = 0.0,
                SpecialBetsAllowed = true,
                ContractsAvailable = true,
                NewMechanics = "His House, His Game, His Final Offer, The Last Spin.",
                Environment = new EnvironmentState { LightFlicker = 0.25f, StaffUnease = 1f, WallMarkings = true, PlayerNames = true, Distortion = 0.7f, GameMasterSightings = 0f, AmbientTint = "C85A5A" }
            });
            t.Layout(totalRounds);
            return t;
        }

        public void Add(StageDefinition stage) { _stages.Add(stage); }

        /// <summary>Spreads the stages over the run. Every stage gets at least one round; the final table at least three when the run allows it.</summary>
        public void Layout(int totalRounds)
        {
            TotalRounds = Math.Max(1, totalRounds);
            int count = _stages.Count;
            int previous = 0;
            for (int i = 0; i < count; i++)
            {
                int end = (int)Math.Round(StageEnds[Math.Min(i, StageEnds.Length - 1)] * TotalRounds);
                int remainingStages = count - 1 - i;
                end = Math.Max(end, previous + 1);
                end = Math.Min(end, TotalRounds - remainingStages);
                if (i == count - 1) end = TotalRounds;
                if (i == count - 2 && TotalRounds >= count + 2) end = Math.Min(end, TotalRounds - 3);
                if (end <= previous) end = previous + 1;
                _stages[i].FirstRound = Math.Min(previous + 1, TotalRounds);
                _stages[i].LastRound = Math.Min(end, TotalRounds);
                previous = end;
            }
        }

        public StageDefinition Get(int number) { return _stages[Math.Max(1, Math.Min(_stages.Count, number)) - 1]; }

        public int StageOf(int round)
        {
            foreach (StageDefinition s in _stages) if (round <= s.LastRound) return s.Number;
            return _stages.Count;
        }

        public StageDefinition For(int round) { return Get(StageOf(round)); }

        public List<int> StageEndRounds()
        {
            var ends = new List<int>();
            foreach (StageDefinition s in _stages) ends.Add(s.LastRound);
            return ends;
        }
    }
}

using System;
using System.Collections.Generic;
using DeadSpin.Core.Common;
using DeadSpin.Core.Rules;

namespace DeadSpin.Core.GameMaster
{
    /// <summary>The Game Master's emotional state, read from how the run is going. Drives dialogue and animation.</summary>
    public enum GameMasterMood
    {
        Calm,
        Amused,
        Irritated,
        Suspicious,
        Angry,
        Desperate
    }

    /// <summary>
    /// HOUSE AUTHORITY: the Game Master's currency for interventions. Gained over time, from the
    /// player's winnings, from special events and from loopholes the player exploits; spent on rules.
    /// </summary>
    public sealed class AuthorityPool
    {
        public int Current { get; private set; }
        public int TotalGained { get; private set; }
        public int TotalSpent { get; private set; }
        public int Cap { get; set; } = 30;

        public event Action<int, string> Changed;

        public AuthorityPool(int starting = 0)
        {
            Current = Math.Max(0, starting);
        }

        public void Gain(int amount, string reason)
        {
            if (amount <= 0) return;
            int before = Current;
            Current = Math.Min(Cap, Current + amount);
            TotalGained += Current - before;
            Changed?.Invoke(Current - before, reason);
        }

        public bool TrySpend(int amount, string reason)
        {
            if (amount < 0 || amount > Current) return false;
            Current -= amount;
            TotalSpent += amount;
            Changed?.Invoke(-amount, reason);
            return true;
        }

        public void Restore(int current, int gained, int spent)
        {
            Current = Math.Max(0, current);
            TotalGained = gained;
            TotalSpent = spent;
        }
    }

    /// <summary>
    /// A Game Master personality: how he weighs his options. Personalities are data; runs pick one
    /// (and unlocks add more), changing which interventions he prefers without changing the rules.
    /// </summary>
    public sealed class GameMasterPersonality
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        private readonly Dictionary<RuleCategory, float> _categoryWeights = new Dictionary<RuleCategory, float>();
        private readonly Dictionary<string, float> _tagWeights = new Dictionary<string, float>();

        public GameMasterPersonality(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }

        public GameMasterPersonality Weight(RuleCategory category, float weight) { _categoryWeights[category] = weight; return this; }
        public GameMasterPersonality Weight(string aiTag, float weight) { _tagWeights[aiTag] = weight; return this; }

        public float WeightFor(RuleDefinition rule)
        {
            float w;
            float result = _categoryWeights.TryGetValue(rule.Category, out w) ? w : 1f;
            foreach (string tag in rule.AiTags) if (_tagWeights.TryGetValue(tag, out w)) result *= w;
            return result;
        }

        public static readonly GameMasterPersonality Showman = new GameMasterPersonality("showman", "The Showman", "Theatrical. Loves to transform the wheel in front of you.")
            .Weight(RuleCategory.Wheel, 1.6f).Weight("theatre", 1.4f);
        public static readonly GameMasterPersonality Accountant = new GameMasterPersonality("accountant", "The Accountant", "Cold. Taxes your winnings and raises your costs.")
            .Weight(RuleCategory.Payout, 1.7f).Weight(RuleCategory.Economy, 1.4f).Weight(RuleCategory.Ability, 1.3f).Weight("theatre", 0.6f);
        public static readonly GameMasterPersonality Brute = new GameMasterPersonality("brute", "The Brute", "Physical. Shakes the table and closes the layout.")
            .Weight(RuleCategory.Table, 2.2f).Weight(RuleCategory.Betting, 1.4f).Weight("anti-stack", 1.5f);
        public static readonly GameMasterPersonality Trickster = new GameMasterPersonality("trickster", "The Trickster", "Sly. Hidden rules, blackouts and sleight of hand.")
            .Weight(RuleCategory.Presentation, 2f).Weight("hidden", 2f).Weight("anti-neighbour", 1.4f);
        public static readonly GameMasterPersonality Collector = new GameMasterPersonality("collector", "The Collector", "Patient. Squeezes your stakes and your debt.")
            .Weight(RuleCategory.Economy, 2f).Weight("pressure", 1.6f).Weight(RuleCategory.Betting, 1.2f);

        public static IReadOnlyList<GameMasterPersonality> All => new[] { Showman, Accountant, Brute, Trickster, Collector };

        public static GameMasterPersonality Find(string id)
        {
            foreach (GameMasterPersonality p in All) if (p.Id == id) return p;
            return Showman;
        }
    }

    /// <summary>Difficulty settings for the Game Master: how fast he earns Authority and how much he may spend per round.</summary>
    public sealed class GameMasterDifficulty
    {
        public int AuthorityPerRound = 1;
        public int MaxInterventionsPerRound = 1;
        /// <summary>Authority per $1,000 the player wins in a round.</summary>
        public int AuthorityPerThousandWon = 1;
        /// <summary>Chance (0..1) he intervenes at all when he can afford to.</summary>
        public double Aggression = 0.6;
        /// <summary>Chance (0..1) of a mid-betting intervention when one is affordable.</summary>
        public double MidBettingChance = 0.25;

        public static GameMasterDifficulty ForStage(int stage)
        {
            switch (stage)
            {
                case 1: return new GameMasterDifficulty { AuthorityPerRound = 1, MaxInterventionsPerRound = 1, AuthorityPerThousandWon = 1, Aggression = 0.55, MidBettingChance = 0.15 };
                case 2: return new GameMasterDifficulty { AuthorityPerRound = 2, MaxInterventionsPerRound = 1, AuthorityPerThousandWon = 1, Aggression = 0.7, MidBettingChance = 0.25 };
                case 3: return new GameMasterDifficulty { AuthorityPerRound = 2, MaxInterventionsPerRound = 2, AuthorityPerThousandWon = 1, Aggression = 0.8, MidBettingChance = 0.3 };
                case 4: return new GameMasterDifficulty { AuthorityPerRound = 3, MaxInterventionsPerRound = 2, AuthorityPerThousandWon = 2, Aggression = 0.9, MidBettingChance = 0.35 };
                default: return new GameMasterDifficulty { AuthorityPerRound = 3, MaxInterventionsPerRound = 3, AuthorityPerThousandWon = 2, Aggression = 1.0, MidBettingChance = 0.4 };
            }
        }
    }

    /// <summary>A scored option the Game Master considered.</summary>
    public readonly struct InterventionCandidate
    {
        public readonly RuleDefinition Rule;
        public readonly float Score;
        public readonly int Parameter;

        public InterventionCandidate(RuleDefinition rule, float score, int parameter)
        {
            Rule = rule;
            Score = score;
            Parameter = parameter;
        }
    }

    /// <summary>
    /// The Game Master's decision system. It reads the <see cref="PlayerProfile"/> and the run's
    /// state, scores every rule it could afford by how well it counters the player right now
    /// (favourite numbers, preferred bet categories, big stacks, ability reliance, a winning streak,
    /// closeness to freedom), weights the scores by personality and difficulty, and picks among the
    /// best few with the seeded RNG so he is purposeful but not perfectly predictable. He never
    /// picks a rule the rule engine would reject as unfair.
    /// </summary>
    public sealed class GameMasterBrain
    {
        public AuthorityPool Authority { get; }
        public GameMasterPersonality Personality { get; set; }
        public GameMasterDifficulty Difficulty { get; set; }

        /// <summary>Limits the rules he may pick (the stage's rule pool). Null allows every rule.</summary>
        public Func<RuleDefinition, bool> RuleFilter { get; set; }

        /// <summary>The intervention he has already decided on for the next round (revealed by Rule Peek).</summary>
        public InterventionCandidate? Planned { get; private set; }

        public GameMasterBrain(AuthorityPool authority, GameMasterPersonality personality)
        {
            Authority = authority ?? throw new ArgumentNullException(nameof(authority));
            Personality = personality ?? GameMasterPersonality.Showman;
            Difficulty = GameMasterDifficulty.ForStage(1);
        }

        /// <summary>Current mood from the run's numbers.</summary>
        public static GameMasterMood MoodFor(PlayerProfile profile, long debt, long startingDebt, long balance)
        {
            if (startingDebt > 0 && debt <= startingDebt / 5) return GameMasterMood.Desperate;
            if (profile.TotalLoopholeExploits >= 2) return GameMasterMood.Angry;
            if (profile.TotalLoopholeExploits == 1 || profile.TotalAbilityUses >= 6) return GameMasterMood.Suspicious;
            if (profile.LastNet > 0 || profile.WinStreak >= 2) return GameMasterMood.Irritated;
            if (profile.LastNet < 0 || profile.LossStreak >= 2) return GameMasterMood.Amused;
            return GameMasterMood.Calm;
        }

        /// <summary>Scores one rule against the player's current behaviour. 0 means "pointless right now".</summary>
        public float Score(RuleDefinition rule, PlayerProfile profile, long balance, long debt, long startingDebt)
        {
            float score = 1f;
            foreach (string tag in rule.AiTags)
            {
                switch (tag)
                {
                    case "anti-favorite": score += (float)profile.FavoriteNumberShare * 6f + (profile.FavoriteNumber > 0 ? 0.5f : -0.8f); break;
                    case "anti-inside": score += (float)profile.InsideShare * 3f; break;
                    case "anti-outside": score += (float)profile.OutsideShare * 3f; break;
                    case "anti-red": score += (float)profile.ShareOf(Betting.BetType.Red) * 6f; break;
                    case "anti-black": score += (float)profile.ShareOf(Betting.BetType.Black) * 6f; break;
                    case "anti-color": score += (float)(profile.ShareOf(Betting.BetType.Red) + profile.ShareOf(Betting.BetType.Black)) * 3f; break;
                    case "anti-special": score += (float)profile.SpecialShare * 5f; break;
                    case "anti-ability": score += Math.Min(3f, profile.TotalAbilityUses * 0.5f); break;
                    case "anti-stack":
                    case "punish-big-bet": score += profile.LastStake > 0 ? Math.Min(3f, profile.LastStake / Math.Max(1f, balance * 0.25f)) : 0f; break;
                    case "anti-small": score += profile.AverageStakePerRound < 100 ? 1.5f : 0f; break;
                    case "punish-win": score += profile.LastNet > 0 ? 1.5f + Math.Min(2f, profile.LastNet / 2000f) : -0.5f; break;
                    case "pressure": score += debt > 0 && startingDebt > 0 ? (float)(1.0 - (double)debt / startingDebt) * 3f : 0f; break;
                    case "theatre": score += 0.6f; break;
                    case "anti-neighbour": score += (float)profile.ShareOf(Betting.BetType.Neighbor) * 4f + 0.2f; break;
                    case "hidden": score += 0.4f; break;
                    case "bait": score += profile.LossStreak >= 2 ? 1.2f : 0f; break;
                    case "variance": score += 0.3f; break;
                    case "reset": score += profile.WinStreak >= 2 ? 1.5f : 0f; break;
                }
            }
            score *= Personality.WeightFor(rule);
            // Cheap rules are favoured when Authority is tight; expensive ones when he is flush.
            score *= 1f + (Authority.Current - rule.AuthorityCost) * 0.04f;
            return Math.Max(0f, score);
        }

        /// <summary>
        /// Chooses up to <see cref="GameMasterDifficulty.MaxInterventionsPerRound"/> interventions he can afford
        /// and the engine accepts. Does not spend Authority or activate anything.
        /// </summary>
        public List<InterventionCandidate> Choose(RuleEngine engine, PlayerProfile profile, IRuleEnvironment env, long startingDebt,
                                                  IRandomSource random, bool midBetting, int maxCount = -1)
        {
            var result = new List<InterventionCandidate>();
            if (maxCount < 0) maxCount = Difficulty.MaxInterventionsPerRound;
            if (maxCount == 0) return result;
            if (random.NextDouble() > (midBetting ? Difficulty.MidBettingChance : Difficulty.Aggression)) return result;

            int budget = Authority.Current;
            var used = new HashSet<string>();
            for (int pick = 0; pick < maxCount; pick++)
            {
                var scored = new List<InterventionCandidate>();
                foreach (RuleDefinition rule in engine.Catalog.All)
                {
                    if (rule.AuthorityCost > budget || rule.AuthorityCost <= 0 && rule.InstantAction == RuleInstantAction.None) continue;
                    if (rule.MinStage > env.Stage || used.Contains(rule.Id)) continue;
                    if (RuleFilter != null && !RuleFilter(rule)) continue;
                    if (midBetting && !rule.MidBetting) continue;
                    if (engine.IsActive(rule.Id) && !rule.Permanent) continue;
                    float score = Score(rule, profile, env.Balance, env.Debt, startingDebt);
                    if (score <= 0.05f) continue;
                    scored.Add(new InterventionCandidate(rule, score, -1));
                }
                scored.Sort((a, b) => b.Score.CompareTo(a.Score));
                // Only the best few need the (more expensive) fairness check.
                var options = new List<InterventionCandidate>();
                foreach (InterventionCandidate candidate in scored)
                {
                    if (!engine.CanActivate(candidate.Rule, -1, 1).Success) continue;
                    options.Add(candidate);
                    if (options.Count == 3) break;
                }
                if (options.Count == 0) break;
                // Weighted pick among the best three keeps him purposeful but not predictable.
                int top = Math.Min(3, options.Count);
                float total = 0f;
                for (int i = 0; i < top; i++) total += options[i].Score;
                double roll = random.NextDouble() * total;
                InterventionCandidate chosen = options[0];
                for (int i = 0; i < top; i++)
                {
                    roll -= options[i].Score;
                    if (roll <= 0) { chosen = options[i]; break; }
                }
                result.Add(chosen);
                used.Add(chosen.Rule.Id);
                budget -= chosen.Rule.AuthorityCost;
            }
            return result;
        }

        /// <summary>Decides now what he will do next round, so Rule Peek can reveal it. Cleared when used or cancelled.</summary>
        public void Plan(RuleEngine engine, PlayerProfile profile, IRuleEnvironment env, long startingDebt, IRandomSource random)
        {
            List<InterventionCandidate> next = Choose(engine, profile, env, startingDebt, random, false, 1);
            Planned = next.Count > 0 ? next[0] : (InterventionCandidate?)null;
        }

        /// <summary>Restores a saved plan.</summary>
        public void SetPlan(InterventionCandidate? plan)
        {
            Planned = plan;
        }

        public void ClearPlan()
        {
            Planned = null;
        }

        /// <summary>Authority earned at the end of a round.</summary>
        public int AuthorityForRound(long playerNet, int loopholesExploited)
        {
            int gain = Difficulty.AuthorityPerRound;
            if (playerNet > 0) gain += (int)(playerNet / 1000) * Difficulty.AuthorityPerThousandWon + 1;
            gain += loopholesExploited * 2;
            return gain;
        }
    }
}

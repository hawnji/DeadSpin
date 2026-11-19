using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Rules
{
    /// <summary>
    /// Every house rule in the game, as data. Each definition is built from reusable
    /// <see cref="RuleEffect"/>s; adding a rule means adding one entry here (or overriding numbers
    /// from Unity via RuleTuningAsset). A fresh catalog is created per run so tuning overrides and
    /// tests never leak into each other.
    /// </summary>
    public sealed class RuleCatalog
    {
        private readonly Dictionary<string, RuleDefinition> _byId = new Dictionary<string, RuleDefinition>();
        private readonly List<RuleDefinition> _all = new List<RuleDefinition>();

        public IReadOnlyList<RuleDefinition> All => _all;

        public RuleDefinition Get(string id)
        {
            RuleDefinition rule;
            return id != null && _byId.TryGetValue(id, out rule) ? rule : null;
        }

        public void Add(RuleDefinition rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (_byId.ContainsKey(rule.Id)) throw new ArgumentException("Duplicate rule id " + rule.Id + ".");
            _byId[rule.Id] = rule;
            _all.Add(rule);
        }

        /// <summary>Overrides the tunable numbers of one rule (from a designer asset). Unknown ids are ignored and reported.</summary>
        public bool Tune(string id, int? cost, int? duration, int? priority, int? minStage)
        {
            RuleDefinition rule = Get(id);
            if (rule == null) return false;
            if (cost.HasValue) rule.AuthorityCost = Math.Max(0, cost.Value);
            if (duration.HasValue) rule.Duration = Math.Max(0, duration.Value);
            if (priority.HasValue) rule.Priority = priority.Value;
            if (minStage.HasValue) rule.MinStage = Math.Max(1, Math.Min(5, minStage.Value));
            return true;
        }

        // Pockets of a colour (red/black after any colour reversal) are tested on the winning pocket itself.
        private static bool IsRed(Pocket p) { return p.Color == PocketColor.Red; }

        private static int FavoriteOrRandom(WheelConfiguration wheel, RuleContext c)
        {
            if (c.FavoriteNumber > 0 && wheel.Contains(c.FavoriteNumber)) return c.FavoriteNumber;
            IRandomSource r = c.Random;
            for (int tries = 0; tries < 50; tries++)
            {
                Pocket p = wheel.Pockets[r.NextInt(wheel.PocketCount)];
                if (!p.IsZero) return p.Number;
            }
            return 1;
        }

        private static WheelChange Chain(WheelConfiguration wheel, Func<WheelConfiguration, WheelChange> first, int times, string description)
        {
            WheelConfiguration current = wheel;
            var sources = new Dictionary<int, int>();
            WheelChangeKind kind = WheelChangeKind.None;
            for (int i = 0; i < times; i++)
            {
                WheelChange step = first(current);
                if (!step.Succeeded) continue;
                foreach (KeyValuePair<int, int> s in step.CloneSources) sources[s.Key] = s.Value;
                current = step.After;
                kind = step.Kind;
            }
            return kind == WheelChangeKind.None ? WheelChange.Refused(wheel, "Nothing changed.") : new WheelChange(wheel, current, kind, description, sources);
        }

        /// <summary>The full rule set from the design: the spec's examples plus every intervention type.</summary>
        public static RuleCatalog CreateDefault()
        {
            var c = new RuleCatalog();

            // ---------------- Wheel interventions (spec costs: move 1, remove 2, add 12 pockets 3, special 3, full transform 5)
            c.Add(new RuleDefinition("move_numbers", "Move Numbers", "Three pairs of numbers trade places on the wheel.", RuleCategory.Wheel, 1, 10, 1,
                new WheelEffect("Three pairs of numbers swap places.", (w, ctx) =>
                {
                    IRandomSource r = ctx.Random;
                    return Chain(w, cur => WheelTransforms.Swap(cur, r.NextInt(cur.PocketCount), r.NextInt(cur.PocketCount)), 3, "Numbers trade places.");
                }))
            { Permanent = true, MidBetting = true, AiTags = new[] { "anti-neighbour", "theatre" }, Announcement = "Let us rearrange the furniture.", VisualCue = "wheel-shift", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("remove_number", "Number Removal", "Your favourite number is torn out of the wheel.", RuleCategory.Wheel, 1, 10, 2,
                new WheelEffect("Your favourite number is removed.", (w, ctx) => WheelTransforms.RemoveNumber(w, FavoriteOrRandom(w, ctx))))
            { Permanent = true, AiTags = new[] { "anti-favorite" }, Announcement = "You seem fond of a number. Let me take it.", VisualCue = "wheel-collapse", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("add_twelve", "Twelve More", "Twelve new numbers emerge from the wheel. The layout grows to match.", RuleCategory.Wheel, 1, 10, 3,
                new WheelEffect("Twelve new numbers are added.", (w, ctx) => WheelTransforms.AddNumbers(w, 12)))
            { Permanent = true, MinStage = 2, AiTags = new[] { "anti-inside", "theatre" }, Announcement = "More numbers. More ways to be wrong.", VisualCue = "wheel-expand", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("special_pocket", "Special Pocket", "A special pocket splits off the wheel.", RuleCategory.Wheel, 1, 10, 3,
                new WheelEffect("A special pocket is added.", (w, ctx) =>
                {
                    SpecialPocketType[] pool = ctx.Stage >= 3
                        ? new[] { SpecialPocketType.Skull, SpecialPocketType.Tax, SpecialPocketType.Curse, SpecialPocketType.Bomb, SpecialPocketType.Mystery, SpecialPocketType.Hourglass, SpecialPocketType.Jackpot, SpecialPocketType.Influence }
                        : new[] { SpecialPocketType.Skull, SpecialPocketType.Tax, SpecialPocketType.Mystery, SpecialPocketType.Hourglass, SpecialPocketType.Jackpot, SpecialPocketType.Influence, SpecialPocketType.DebtRelief, SpecialPocketType.GoldenTicket };
                    IRandomSource r = ctx.Random;
                    return WheelTransforms.InsertSpecial(w, pool[r.NextInt(pool.Length)], r);
                }))
            { Permanent = true, MinStage = 2, AiTags = new[] { "theatre", "pressure" }, Announcement = "A little something extra on the wheel.", VisualCue = "wheel-special", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("full_transformation", "The Transformation", "The whole wheel rebuilds itself at a new size.", RuleCategory.Wheel, 1, 10, 5,
                new WheelEffect("The wheel changes size.", (w, ctx) =>
                {
                    int[] sizes = { 12, 18, 24, 36, 48, 72 };
                    IRandomSource r = ctx.Random;
                    int size = sizes[r.NextInt(sizes.Length)];
                    if (size == w.PocketCount) size = sizes[(Array.IndexOf(sizes, size) + 1) % sizes.Length];
                    return WheelTransforms.Resize(w, size);
                }))
            { Permanent = true, MinStage = 3, ConflictTags = new[] { "size" }, AiTags = new[] { "theatre", "reset" }, Announcement = "I grow bored of this wheel. Let us build another.", VisualCue = "wheel-transform", AudioCue = "mechanism-big" });

            c.Add(new RuleDefinition("expansion", "Expansion", "The wheel grows to 72 pockets for three rounds.", RuleCategory.Wheel, 3, 20, 4,
                new WheelEffect("The wheel has 72 pockets.", (w, ctx) => w.PocketCount == 72 ? WheelChange.Refused(w, "Already 72.") : WheelTransforms.Resize(w, 72)))
            { MinStage = 3, ConflictTags = new[] { "size" }, AiTags = new[] { "anti-inside", "theatre" }, Announcement = "Thirty-six was never enough. Seventy-two.", VisualCue = "wheel-transform", AudioCue = "mechanism-big" });

            c.Add(new RuleDefinition("number_clone", "Number Clone", "Thirteen appears in four separate pockets.", RuleCategory.Wheel, 3, 15, 2,
                new WheelEffect("13 appears four times.", (w, ctx) => WheelTransforms.CloneNumber(w, w.Contains(13) ? 13 : FavoriteOrRandom(w, ctx), 4)))
            { Negative = false, AiTags = new[] { "theatre" }, Announcement = "Thirteen. Four times. Superstitious?", VisualCue = "wheel-split", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("number_shuffle", "Number Shuffle", "Every number moves to a new pocket.", RuleCategory.Wheel, 1, 10, 2,
                new WheelEffect("The numbers are shuffled.", (w, ctx) => WheelTransforms.Shuffle(w, ctx.Random)))
            { Permanent = true, MidBetting = true, AiTags = new[] { "anti-neighbour", "theatre" }, Announcement = "Everything in its place. A new place.", VisualCue = "wheel-shift", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("random_replacement", "Random Replacement", "Two pockets are replaced by copies of other numbers.", RuleCategory.Wheel, 1, 10, 2,
                new WheelEffect("Two pockets are replaced.", (w, ctx) => { IRandomSource r = ctx.Random; return Chain(w, cur => WheelTransforms.ReplaceRandom(cur, r), 2, "Two pockets change their numbers."); }))
            { Permanent = true, MinStage = 2, AiTags = new[] { "anti-favorite" }, Announcement = "Some numbers have been... replaced.", VisualCue = "wheel-shift", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("color_reversal", "Color Reversal", "Red becomes black and black becomes red.", RuleCategory.Wheel, 2, 15, 2,
                new WheelEffect("Colours are reversed.", (w, ctx) => WheelTransforms.ReverseColors(w)))
            { AiTags = new[] { "anti-color" }, Announcement = "Red is black. Black is red. Keep up.", VisualCue = "wheel-flip", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("lucky_numbers", "Lucky Numbers", "Three numbers you never bet on become twice as likely.", RuleCategory.Wheel, 2, 15, 1,
                new WheelEffect("Three numbers weigh double.", (w, ctx) =>
                {
                    IRandomSource r = ctx.Random;
                    return Chain(w, cur =>
                    {
                        Pocket p = cur.Pockets[r.NextInt(cur.PocketCount)];
                        return p.IsZero || p.Number == ctx.FavoriteNumber ? WheelChange.Refused(cur, "skip") : WheelTransforms.SetNumberWeight(cur, p.Number, 2);
                    }, 3, "Three numbers become lucky.");
                }))
            { Negative = false, AiTags = new[] { "anti-favorite" }, Announcement = "Some numbers are luckier than others. Not yours.", VisualCue = "wheel-glow", AudioCue = "chime" });

            c.Add(new RuleDefinition("dead_numbers", "Dead Numbers", "Your favourite number and two others cannot win.", RuleCategory.Wheel, 2, 15, 2,
                new WheelEffect("Three numbers are dead.", (w, ctx) =>
                {
                    IRandomSource r = ctx.Random;
                    int fav = FavoriteOrRandom(w, ctx);
                    WheelChange first = WheelTransforms.SetActive(w, fav, false);
                    WheelConfiguration start = first.Succeeded ? first.After : w;
                    WheelChange rest = Chain(start, cur => { Pocket p = cur.Pockets[r.NextInt(cur.PocketCount)]; return p.IsZero ? WheelChange.Refused(cur, "skip") : WheelTransforms.SetActive(cur, p.Number, false); }, 2, "Numbers die.");
                    WheelConfiguration end = rest.Succeeded ? rest.After : start;
                    return end == w ? WheelChange.Refused(w, "Nothing died.") : new WheelChange(w, end, WheelChangeKind.Reweight, "Three numbers are dead.");
                }))
            { MinStage = 2, AiTags = new[] { "anti-favorite" }, Announcement = "Some numbers have died. Mourn them later.", VisualCue = "wheel-dark", AudioCue = "thud" });

            c.Add(new RuleDefinition("magnetic_pockets", "Magnetic Pockets", "The zero pockets pull the ball: they weigh triple.", RuleCategory.Wheel, 2, 15, 2,
                new WheelEffect("Zero pockets weigh x3.", (w, ctx) => w.Contains(0) ? WheelTransforms.SetNumberWeight(w, 0, 3) : WheelChange.Refused(w, "No zero.")))
            { AiTags = new[] { "pressure" }, Announcement = "The zero has grown... magnetic.", VisualCue = "wheel-glow", AudioCue = "hum" });

            c.Add(new RuleDefinition("uneven_distribution", "Uneven Distribution", "Every pocket gets a random weight from 1 to 3.", RuleCategory.Wheel, 2, 15, 3,
                new WheelEffect("Pockets are unequal.", (w, ctx) => WheelTransforms.RandomWeights(w, ctx.Random, 3)))
            { MinStage = 3, AiTags = new[] { "theatre" }, Announcement = "Not all pockets are created equal.", VisualCue = "wheel-glow", AudioCue = "hum" });

            c.Add(new RuleDefinition("split_wheel", "Split Wheel", "Half the wheel turns a quarter away from the other half.", RuleCategory.Wheel, 2, 15, 3,
                new WheelEffect("The wheel splits.", (w, ctx) => WheelTransforms.RotateSection(w, w.PocketCount / 2, w.PocketCount / 2, w.PocketCount / 4)))
            { MinStage = 3, AiTags = new[] { "anti-neighbour", "theatre" }, Announcement = "The wheel is of two minds.", VisualCue = "wheel-split", AudioCue = "mechanism-big" });

            c.Add(new RuleDefinition("rotating_rings", "Independent Rings", "An outer ring of multipliers spins against the wheel.", RuleCategory.Wheel, 3, 15, 3,
                new WheelEffect("An outer multiplier ring is active.", (w, ctx) => WheelTransforms.SetRing(w, WheelRing.Standard())))
            { MinStage = 2, ConflictTags = new[] { "ring" }, Negative = false, AiTags = new[] { "theatre", "variance" }, Announcement = "Another ring. Another chance. Mostly mine.", VisualCue = "ring", AudioCue = "mechanism-big" });

            // ---------------- Payout rules
            c.Add(new RuleDefinition("red_tax", "Red Tax", "Red numbers pay 50% less.", RuleCategory.Payout, 3, 30, 2,
                new PayoutEffect("Red Tax: winnings on red numbers are halved.", 50, (bet, pocket) => IsRed(pocket),
                    (bet, pocket) => bet.Type == BetType.Combination, "The tax collector only reads plain bets. Combination bets pay in full."))
            { AiTags = new[] { "anti-red", "punish-win" }, Announcement = "Red has become expensive. Fifty percent.", LoopholeHint = "Combination bets are not taxed.", VisualCue = "red-light", AudioCue = "coin" });

            c.Add(new RuleDefinition("black_tax", "Black Tax", "Black numbers pay 50% less.", RuleCategory.Payout, 3, 30, 2,
                new PayoutEffect("Black Tax: winnings on black numbers are halved.", 50, (bet, pocket) => pocket.Color == PocketColor.Black,
                    (bet, pocket) => bet.Type == BetType.Split, "Splits were left out of the decree."))
            { AiTags = new[] { "anti-black", "punish-win" }, Announcement = "Black is out of fashion. Half pay.", LoopholeHint = "Split bets are not taxed.", VisualCue = "dim", AudioCue = "coin" });

            c.Add(new RuleDefinition("payout_squeeze", "Payout Squeeze", "Straight-up bets pay 75%.", RuleCategory.Payout, 2, 30, 2,
                new PayoutEffect("Payout Squeeze: straight bets pay 75%.", 75, (bet, pocket) => bet.Type == BetType.Straight))
            { AiTags = new[] { "anti-inside", "punish-win" }, Announcement = "Long shots are paying a little less tonight.", LoopholeHint = "Only straight bets are squeezed; splits and streets pay in full.", VisualCue = "dim", AudioCue = "coin" });

            c.Add(new RuleDefinition("reversed_fortune", "Reversed Fortune", "Even-money bets pay 60%.", RuleCategory.Payout, 2, 30, 3,
                new PayoutEffect("Reversed Fortune: even-money bets pay 60%.", 60, (bet, pocket) => bet.Type >= BetType.Red && bet.Type <= BetType.High,
                    (bet, pocket) => pocket.Number % 7 == 0, "Pockets divisible by seven were forgotten: even-money bets on them pay in full."))
            { MinStage = 2, AiTags = new[] { "anti-outside", "punish-win" }, Announcement = "Safe bets are for cowards. They pay accordingly.", LoopholeHint = "Multiples of seven are exempt.", VisualCue = "flip", AudioCue = "coin" });

            c.Add(new RuleDefinition("house_cut", "House Cut", "Every winning bet pays 85%.", RuleCategory.Payout, 2, 30, 3,
                new PayoutEffect("House Cut: all winnings x0.85.", 85, (bet, pocket) => true,
                    (bet, pocket) => pocket.Special != SpecialPocketType.None, "The cut is not taken on special pockets."))
            { MinStage = 3, Hidden = true, AiTags = new[] { "punish-win", "hidden" }, Announcement = null, LoopholeHint = "Wins on special pockets are untouched.", VisualCue = "", AudioCue = "" });

            c.Add(new RuleDefinition("neighborhood_watch", "Neighborhood Watch", "Straight bets beside the winning pocket get half back.", RuleCategory.Payout, 2, 30, 2,
                new NeighborConsolationEffect(50))
            { Negative = false, MinStage = 2, AiTags = new[] { "bait" }, Announcement = "Tonight, even the neighbours get a little something.", VisualCue = "glow", AudioCue = "chime" });

            // ---------------- Betting and table rules
            c.Add(new RuleDefinition("house_shake", "House Shake", "The table shakes and unsecured chips slide.", RuleCategory.Table, 1, 5, 2)
            { MidBetting = true, InstantAction = RuleInstantAction.ShakeTable, InstantValue = 70, AiTags = new[] { "anti-stack", "punish-big-bet" }, Announcement = "Oops.", VisualCue = "shake", AudioCue = "rumble" });

            c.Add(new RuleDefinition("tilted_table", "Tilted Table", "The table tilts toward the house. Loose chips slide.", RuleCategory.Table, 1, 5, 2)
            { MidBetting = true, MinStage = 2, InstantAction = RuleInstantAction.TiltTable, InstantValue = 85, AiTags = new[] { "anti-stack" }, Announcement = "The floor is not quite level.", VisualCue = "tilt", AudioCue = "creak" });

            c.Add(new RuleDefinition("higher_stakes", "Higher Stakes", "The minimum wager is multiplied by 2.5.", RuleCategory.Betting, 3, 20, 2,
                new MinimumBetEffect(250))
            { ConflictTags = new[] { "minbet" }, AiTags = new[] { "pressure", "anti-small" }, Announcement = "Small bets bore me. The minimum rises.", VisualCue = "", AudioCue = "bell" });

            c.Add(new RuleDefinition("double_trouble", "Double Trouble", "Two balls are released.", RuleCategory.Betting, 2, 20, 4,
                new BallCountEffect(2))
            { MinStage = 2, ConflictTags = new[] { "balls" }, AiTags = new[] { "theatre", "anti-inside" }, Announcement = "One ball is so lonely.", VisualCue = "two-balls", AudioCue = "rattle" });

            c.Add(new RuleDefinition("closed_section", "Closed Section", "The third of the layout you bet most is closed.", RuleCategory.Betting, 1, 20, 1,
                new CloseDozenEffect())
            { MidBetting = true, AiTags = new[] { "anti-favorite" }, Announcement = "That part of the table is closed for cleaning.", VisualCue = "dim-layout", AudioCue = "thud" });

            c.Add(new RuleDefinition("banned_number", "Banned Number", "No bet may cover your favourite number.", RuleCategory.Betting, 2, 20, 2,
                new CloseNumberEffect())
            { AiTags = new[] { "anti-favorite" }, Announcement = "Your lucky number is no longer welcome at my table.", VisualCue = "dim-layout", AudioCue = "thud" });

            c.Add(new RuleDefinition("no_straights", "No Long Shots", "Straight-up bets are closed.", RuleCategory.Betting, 2, 20, 2,
                new CloseBetTypesEffect(BetType.Straight))
            { MinStage = 2, AiTags = new[] { "anti-inside" }, Announcement = "No more single numbers. You were getting lucky.", VisualCue = "dim-layout", AudioCue = "thud" });

            c.Add(new RuleDefinition("no_takebacks", "No Take-backs", "Chips cannot be removed or moved once placed.", RuleCategory.Betting, 2, 20, 1,
                new NoTakeBacksEffect())
            { AiTags = new[] { "anti-hesitation" }, Announcement = "What you place, you keep placed.", VisualCue = "", AudioCue = "click" });

            c.Add(new RuleDefinition("short_clock", "Short Clock", "Betting time is cut by ten seconds.", RuleCategory.Betting, 2, 20, 1,
                new BettingTimeEffect(-10f))
            { AiTags = new[] { "pressure" }, Announcement = "Time is money. Yours is running out.", VisualCue = "clock", AudioCue = "tick" });

            c.Add(new RuleDefinition("plain_roulette", "Plain Roulette", "Special bets are closed.", RuleCategory.Betting, 2, 20, 1,
                new SpecialBetsEffect(false))
            { MinStage = 2, AiTags = new[] { "anti-special" }, Announcement = "No tricks tonight. Plain roulette.", VisualCue = "dim-layout", AudioCue = "thud" });

            c.Add(new RuleDefinition("mandatory_wager", "Mandatory Wager", "You must wager at least five minimum bets each round.", RuleCategory.Economy, 3, 20, 2,
                new MandatoryWagerEffect(5))
            { MinStage = 3, ConflictTags = new[] { "mandatory" }, AiTags = new[] { "pressure", "anti-small" }, Announcement = "Sitting out is no longer an option.", VisualCue = "", AudioCue = "bell" });

            // ---------------- Ability and presentation rules
            c.Add(new RuleDefinition("no_safety_net", "No Safety Net", "Insurance costs twice as much.", RuleCategory.Ability, 3, 20, 2,
                new AbilityCostEffect(100, 200))
            { AiTags = new[] { "anti-ability" }, Announcement = "Insurance has become... expensive.", VisualCue = "", AudioCue = "coin" });

            c.Add(new RuleDefinition("house_prices", "House Prices", "Every ability costs 50% more Influence.", RuleCategory.Ability, 2, 20, 2,
                new AbilityCostEffect(150, 100))
            { MinStage = 2, AiTags = new[] { "anti-ability" }, Announcement = "Your little tricks have a price. A higher one.", VisualCue = "", AudioCue = "coin" });

            c.Add(new RuleDefinition("blackout", "Blackout", "The casino lights shut down during betting.", RuleCategory.Presentation, 1, 40, 1,
                new PresentationEffect(blackout: true))
            { MinStage = 2, AiTags = new[] { "theatre", "pressure" }, Announcement = "Let us see how well you play in the dark.", VisualCue = "blackout", AudioCue = "power-down" });

            c.Add(new RuleDefinition("reverse_rotation", "Reverse Rotation", "The wheel spins the other way.", RuleCategory.Presentation, 2, 40, 1,
                new SpinEffect(true))
            { Negative = false, AiTags = new[] { "theatre" }, Announcement = "Widdershins.", VisualCue = "", AudioCue = "mechanism" });

            c.Add(new RuleDefinition("turned_tables", "Turned Tables", "The betting layout is turned around.", RuleCategory.Presentation, 1, 40, 1,
                new PresentationEffect(rotated: true))
            { MidBetting = true, MinStage = 2, AiTags = new[] { "theatre", "pressure" }, Announcement = "Let us look at things from my side.", VisualCue = "layout-turn", AudioCue = "mechanism" });

            // ---------------- Rules started by the player, events and the final encounter. They cost no
            // Authority, so the Game Master's brain never picks them himself.
            c.Add(new RuleDefinition("player_lucky_coin", "Lucky Coin", "Your chosen number gains a second pocket for one spin.", RuleCategory.Wheel, 1, 50, 0,
                new WheelEffect("Your coin adds a pocket for your number.", (w, ctx) =>
                {
                    int n = ctx.Instance.Parameter;
                    if (n < 0 || !w.Contains(n)) return WheelChange.Refused(w, "That number is not on the wheel.");
                    return WheelTransforms.CloneNumber(w, n, w.CountOf(n) + 1);
                }))
            { Negative = false, Announcement = "A coin on the wheel? How quaint.", VisualCue = "wheel-split", AudioCue = "coin" });

            c.Add(new RuleDefinition("player_debt_freeze", "Debt Freeze", "Your debt cannot increase this round.", RuleCategory.Economy, 1, 50, 0,
                new DebtFreezeEffect())
            { Negative = false, Announcement = "Frozen. For now.", VisualCue = "frost", AudioCue = "chime" });

            c.Add(new RuleDefinition("event_inspection", "The Inspection", "The Game Master closes the bet you rely on most.", RuleCategory.Betting, 2, 25, 0,
                new CloseParameterBetTypeEffect())
            { Announcement = "I have been reading your history. Let us remove a habit.", VisualCue = "dim-layout", AudioCue = "thud" });

            c.Add(new RuleDefinition("event_generous_house", "The Generous House", "Every winning bet pays 50% more.", RuleCategory.Payout, 1, 25, 0,
                new PayoutEffect("The Generous House: odd pockets pay +50%.", 150, (b, p) => !p.IsZero && p.Number % 2 == 1),
                new PayoutEffect("Hidden condition: even pockets pay only 60%.", 60, (b, p) => p.IsZero || p.Number % 2 == 0, hidden: true))
            { Negative = false, Announcement = "Tonight the house is generous. Mostly.", VisualCue = "glow", AudioCue = "chime", LoopholeHint = "The bonus only applies to odd pockets. Even pockets are quietly taxed." });

            c.Add(new RuleDefinition("event_silent_spin", "The Silent Spin", "No announcements, no countdown.", RuleCategory.Presentation, 1, 45, 0,
                new PresentationEffect(silent: true))
            { Announcement = "", VisualCue = "silence", AudioCue = "" });

            c.Add(new RuleDefinition("final_last_spin", "The Last Spin", "One spin. The stakes rise: minimum x5, maximum x5.", RuleCategory.Betting, 1, 60, 0,
                new MinimumBetEffect(500), new MaximumBetEffect(500))
            { ConflictTags = new[] { "minbet" }, Announcement = "One spin. Everything you have.", VisualCue = "spotlight", AudioCue = "drum" });

            return c;
        }
    }
}

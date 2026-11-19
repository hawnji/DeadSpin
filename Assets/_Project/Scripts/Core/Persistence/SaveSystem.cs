using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace DeadSpin.Core.Persistence
{
    /// <summary>
    /// Saves and loads a run between rounds, and the meta progress between runs, as JSON. The Unity
    /// layer only decides where the text goes (a file under persistentDataPath). A save made between
    /// rounds restores the exact game, including the random sequence.
    /// </summary>
    public static class SaveSystem
    {
        public const int Version = 1;

        public static bool CanSave(CasinoRun run)
        {
            return run != null && !run.Run.IsOver && run.Run.Round.Phase == RoundPhase.Idle;
        }

        // ------------------------------------------------------------------ run

        public static string SaveRun(CasinoRun run)
        {
            if (!CanSave(run)) throw new InvalidOperationException("Runs can only be saved between rounds.");
            RunConfig c = run.Run.Config;
            var setup = new Obj
            {
                { "seed", c.Seed }, { "startingBalance", c.StartingBalance }, { "startingDebt", run.StartingDebt }, { "totalRounds", c.TotalRounds },
                { "minimumBet", c.MinimumBet }, { "maximumBet", c.MaximumBet }, { "announce", (double)c.AnnouncementSeconds }, { "betting", (double)c.BettingSeconds },
                { "summary", (double)c.SummarySeconds }, { "personality", run.GameMaster.Personality.Id }, { "wheel", run.Setup.StartingWheelId },
                { "finalVariant", run.Final.Variant }, { "environment", run.Setup.EnvironmentId }, { "chipDesign", run.Setup.ChipDesignId },
                { "eventWeights", ToObj(run.Setup.EventWeights) }
            };

            var rules = new List<object>();
            foreach (RuleInstance r in run.Rules.Active)
            {
                rules.Add(new Obj { { "id", r.Id }, { "left", r.RoundsRemaining }, { "seed", r.Seed }, { "param", r.Parameter }, { "round", r.ActivatedRound }, { "revealed", r.Revealed } });
            }

            var contracts = new List<object>();
            foreach (ContractInstance k in run.Contracts.Active)
            {
                contracts.Add(new Obj { { "id", k.Definition.Id }, { "round", k.SignedRound }, { "outstanding", k.Outstanding }, { "left", k.RoundsLeft } });
            }

            var challenges = new List<object>();
            int activeIndex = -1;
            for (int i = 0; i < run.Challenges.History.Count; i++)
            {
                ChallengeInstance ch = run.Challenges.History[i];
                if (ch == run.Challenges.Active) activeIndex = i;
                challenges.Add(new Obj
                {
                    { "id", ch.Definition.Id }, { "round", ch.AcceptedRound }, { "left", ch.RoundsLeft }, { "progress", ch.Progress },
                    { "target", ch.Target }, { "acc", ch.Accumulated }, { "status", (int)ch.Status }
                });
            }

            var shelf = new List<object>();
            foreach (ShopOffer o in run.Shop.Shelf) shelf.Add(new Obj { { "id", o.Item.Id }, { "price", o.Price }, { "left", o.Remaining } });

            var settled = new List<object>();
            var missed = new List<object>();
            foreach (Installment i in run.Payments.Installments)
            {
                if (i.Settled) settled.Add(i.Stage);
                if (i.Missed) missed.Add(i.Stage);
            }

            PlayerProfile p = run.Profile;
            var numbers = new Obj();
            foreach (KeyValuePair<int, long> n in p.SnapshotNumbers()) numbers[n.Key.ToString()] = n.Value;
            var types = new Obj();
            foreach (KeyValuePair<BetType, long> t in p.SnapshotTypes()) types[((int)t.Key).ToString()] = t.Value;

            var chips = new Obj();
            foreach (KeyValuePair<ChipKind, int> k in run.Inventory.SnapshotChips()) chips[((int)k.Key).ToString()] = k.Value;

            var root = new Obj
            {
                { "version", Version },
                { "setup", setup },
                { "rng", run.Run.Random is SeededRandom sr ? sr.State : 0UL },
                { "roundsPlayed", run.Run.RoundsPlayed },
                { "ledger", new Obj { { "balance", run.Ledger.Balance }, { "debt", run.Ledger.Debt }, { "stored", run.Ledger.Stored }, { "won", run.Ledger.TotalWinnings }, { "lost", run.Ledger.TotalLosses } } },
                { "baseWheel", WheelToObj(run.Rules.BaseWheel) },
                { "history", new List<object>(Box(run.Table.History)) },
                { "rules", rules },
                { "director", new Obj
                    {
                        { "baseMin", run.BaseMinimumBetInternal }, { "nextBetting", (double)run.NextBettingAdjust }, { "imposed", new List<object>(run.ImposedHistory) },
                        { "lastStage", run.LastStageStarted }, { "started", run.Started }, { "cancelPlanned", run.CancelPlanned }, { "liveTransform", run.LiveTransformPending }
                    }
                },
                { "profile", new Obj
                    {
                        { "numbers", numbers }, { "types", types }, { "dozens", new List<object>(Box(p.SnapshotDozens())) }, { "recent", new List<object>(Box(p.RecentNets)) },
                        { "rounds", p.RoundsObserved }, { "staked", p.TotalStaked }, { "winStreak", p.WinStreak }, { "lossStreak", p.LossStreak },
                        { "biggestWin", p.BiggestWin }, { "loopholes", p.TotalLoopholeExploits }, { "abilities", p.TotalAbilityUses },
                        { "lastNet", p.LastNet }, { "lastStake", p.LastStake }, { "noBet", p.RoundsWithoutBetting }
                    }
                },
                { "authority", new Obj { { "current", run.Authority.Current }, { "gained", run.Authority.TotalGained }, { "spent", run.Authority.TotalSpent } } },
                { "influence", new Obj { { "current", run.Influence.Current }, { "earned", run.Influence.TotalEarned }, { "spent", run.Influence.TotalSpent } } },
                { "inventory", new Obj
                    {
                        { "upgrades", new List<object>(run.Inventory.Upgrades) }, { "charges", ToObj(run.Inventory.SnapshotCharges()) }, { "chips", chips },
                        { "ready", ToObj(run.Inventory.SnapshotReady()) }, { "uses", ToObj(new Dictionary<string, int>(ToDict(run.Inventory.RunUses))) }, { "tickets", run.Inventory.GoldenTickets }
                    }
                },
                { "contracts", contracts },
                { "contractOffer", run.Contracts.Offer != null ? run.Contracts.Offer.Id : null },
                { "challenges", challenges },
                { "activeChallenge", activeIndex },
                { "challengesWon", run.Challenges.Won },
                { "challengeOffer", run.Challenges.Offer != null ? run.Challenges.Offer.Id : null },
                { "plan", run.GameMaster.Planned.HasValue ? new Obj { { "id", run.GameMaster.Planned.Value.Rule.Id }, { "score", (double)run.GameMaster.Planned.Value.Score }, { "param", run.GameMaster.Planned.Value.Parameter } } : null },
                { "events", new List<object>(run.Events.History) },
                { "forcedEvent", run.Events.Forced != null ? run.Events.Forced.Id : null },
                { "shop", new Obj { { "shelf", shelf }, { "spent", run.Shop.TotalSpent } } },
                { "payments", new Obj { { "paid", run.Payments.TotalPaid }, { "missed", run.Payments.MissedCount }, { "settled", settled }, { "missedStages", missed } } },
                { "final", new Obj { { "answered", run.Final.OfferAnswered }, { "answer", run.Final.Answer.HasValue ? (int)run.Final.Answer.Value : -1 }, { "built", run.Final.HisGameBuilt } } },
                { "records", new Obj { { "read", new List<object>(run.Records.ReadIds) }, { "rested", new List<object>(Box(run.Records.RestedStages)) } } }
            };
            return MiniJson.Serialize(root);
        }

        public static CasinoRun LoadRun(string json, MetaProgress meta = null)
        {
            var root = MiniJson.Deserialize(json) as IDictionary<string, object>;
            if (root == null) throw new FormatException("The save is not a JSON object.");
            if (JsonRead.Int(root, "version") > Version) throw new FormatException("The save comes from a newer version of the game.");

            IDictionary<string, object> s = JsonRead.Object(root, "setup");
            var config = RunConfig.CreateDefault();
            config.Seed = JsonRead.ULong(s, "seed", 1);
            config.StartingBalance = JsonRead.Long(s, "startingBalance", config.StartingBalance);
            config.StartingDebt = JsonRead.Long(s, "startingDebt", config.StartingDebt);
            config.TotalRounds = JsonRead.Int(s, "totalRounds", config.TotalRounds);
            config.MinimumBet = JsonRead.Long(s, "minimumBet", config.MinimumBet);
            config.MaximumBet = JsonRead.Long(s, "maximumBet", config.MaximumBet);
            config.AnnouncementSeconds = (float)JsonRead.Double(s, "announce", config.AnnouncementSeconds);
            config.BettingSeconds = (float)JsonRead.Double(s, "betting", config.BettingSeconds);
            config.SummarySeconds = (float)JsonRead.Double(s, "summary", config.SummarySeconds);

            var setup = new RunSetup
            {
                Config = config,
                Personality = GameMasterPersonality.Find(JsonRead.String(s, "personality", "showman")) ?? GameMasterPersonality.Showman,
                StartingWheelId = JsonRead.String(s, "wheel", "european"),
                FinalVariant = JsonRead.Int(s, "finalVariant"),
                EnvironmentId = JsonRead.String(s, "environment", "classic"),
                ChipDesignId = JsonRead.String(s, "chipDesign", "classic"),
                IsUnlocked = id => meta != null && meta.IsUnlocked(id)
            };
            IDictionary<string, object> weights = JsonRead.Object(s, "eventWeights");
            if (weights != null) foreach (KeyValuePair<string, object> w in weights) setup.EventWeights[w.Key] = JsonRead.Double(weights, w.Key, 1);

            var run = new CasinoRun(setup, meta, false);

            // Money and progress.
            IDictionary<string, object> l = JsonRead.Object(root, "ledger");
            run.Ledger.Restore(JsonRead.Long(l, "balance"), JsonRead.Long(l, "debt"), JsonRead.Long(l, "stored"), JsonRead.Long(l, "won"), JsonRead.Long(l, "lost"));
            run.Run.RestoreProgress(JsonRead.Int(root, "roundsPlayed"));
            if (run.Run.Random is SeededRandom random && JsonRead.Get(root, "rng") != null) random.State = JsonRead.ULong(root, "rng");

            // Wheel and rules.
            WheelConfiguration baseWheel = WheelFromObj(JsonRead.Object(root, "baseWheel"));
            var active = new List<RuleInstance>();
            foreach (object o in JsonRead.Array(root, "rules"))
            {
                var r = o as IDictionary<string, object>;
                RuleDefinition def = run.Rules.Catalog.Get(JsonRead.String(r, "id"));
                if (def == null) continue;
                var instance = new RuleInstance(def, JsonRead.ULong(r, "seed", 1), JsonRead.Int(r, "param", -1), JsonRead.Int(r, "round"))
                {
                    RoundsRemaining = JsonRead.Int(r, "left"),
                    Revealed = JsonRead.Bool(r, "revealed")
                };
                active.Add(instance);
            }
            run.Rules.Restore(baseWheel, active);
            var history = new List<int>();
            foreach (object h in JsonRead.Array(root, "history")) history.Add((int)JsonRead.AsLong(h));
            run.Table.Restore(run.Rules.EffectiveWheel, history);

            IDictionary<string, object> d = JsonRead.Object(root, "director");
            run.BaseMinimumBetInternal = JsonRead.Long(d, "baseMin", config.MinimumBet);
            run.NextBettingAdjust = (float)JsonRead.Double(d, "nextBetting");
            run.ImposedHistory.Clear();
            foreach (object o in JsonRead.Array(d, "imposed")) if (o is string id) run.ImposedHistory.Add(id);
            run.LastStageStarted = JsonRead.Int(d, "lastStage");
            run.Started = JsonRead.Bool(d, "started");
            run.CancelPlanned = JsonRead.Bool(d, "cancelPlanned");
            run.LiveTransformPending = JsonRead.Bool(d, "liveTransform");

            // The player.
            IDictionary<string, object> p = JsonRead.Object(root, "profile");
            var numbers = new Dictionary<int, long>();
            IDictionary<string, object> nmap = JsonRead.Object(p, "numbers");
            if (nmap != null) foreach (KeyValuePair<string, object> n in nmap) numbers[int.Parse(n.Key)] = JsonRead.AsLong(n.Value);
            var types = new Dictionary<BetType, long>();
            IDictionary<string, object> tmap = JsonRead.Object(p, "types");
            if (tmap != null) foreach (KeyValuePair<string, object> t in tmap) types[(BetType)int.Parse(t.Key)] = JsonRead.AsLong(t.Value);
            var dozens = new List<long>();
            foreach (object o in JsonRead.Array(p, "dozens")) dozens.Add(JsonRead.AsLong(o));
            var recent = new List<long>();
            foreach (object o in JsonRead.Array(p, "recent")) recent.Add(JsonRead.AsLong(o));
            run.Profile.Restore(numbers, types, dozens.ToArray(), recent, JsonRead.Int(p, "rounds"), JsonRead.Long(p, "staked"), JsonRead.Int(p, "winStreak"),
                JsonRead.Int(p, "lossStreak"), JsonRead.Long(p, "biggestWin"), JsonRead.Int(p, "loopholes"), JsonRead.Int(p, "abilities"),
                JsonRead.Long(p, "lastNet"), JsonRead.Long(p, "lastStake"), JsonRead.Int(p, "noBet"));

            IDictionary<string, object> a = JsonRead.Object(root, "authority");
            run.Authority.Restore(JsonRead.Int(a, "current"), JsonRead.Int(a, "gained"), JsonRead.Int(a, "spent"));
            IDictionary<string, object> inf = JsonRead.Object(root, "influence");
            run.Influence.Restore(JsonRead.Int(inf, "current"), JsonRead.Int(inf, "earned"), JsonRead.Int(inf, "spent"));

            IDictionary<string, object> inv = JsonRead.Object(root, "inventory");
            var upgrades = new List<string>();
            foreach (object o in JsonRead.Array(inv, "upgrades")) if (o is string u) upgrades.Add(u);
            var chips = new Dictionary<ChipKind, int>();
            IDictionary<string, object> cmap = JsonRead.Object(inv, "chips");
            if (cmap != null) foreach (KeyValuePair<string, object> c in cmap) chips[(ChipKind)int.Parse(c.Key)] = (int)JsonRead.AsLong(c.Value);
            run.Inventory.Restore(upgrades, ToIntDict(JsonRead.Object(inv, "charges")), chips, ToIntDict(JsonRead.Object(inv, "ready")),
                ToIntDict(JsonRead.Object(inv, "uses")), JsonRead.Int(inv, "tickets"));

            // Contracts, challenges, events, shop, payments, the final encounter, records.
            var contracts = new List<ContractInstance>();
            foreach (object o in JsonRead.Array(root, "contracts"))
            {
                var k = o as IDictionary<string, object>;
                ContractDefinition def = ContractCatalog.Get(JsonRead.String(k, "id"));
                if (def == null) continue;
                contracts.Add(new ContractInstance(def, JsonRead.Int(k, "round")) { Outstanding = JsonRead.Long(k, "outstanding"), RoundsLeft = JsonRead.Int(k, "left") });
            }
            run.Contracts.Restore(contracts);
            string offer = JsonRead.String(root, "contractOffer");
            run.Contracts.Offer = offer != null ? ContractCatalog.Get(offer) : null;

            var challengeHistory = new List<ChallengeInstance>();
            ChallengeInstance activeChallenge = null;
            int activeIndex = JsonRead.Int(root, "activeChallenge", -1);
            List<object> ch = JsonRead.Array(root, "challenges");
            for (int i = 0; i < ch.Count; i++)
            {
                var c = ch[i] as IDictionary<string, object>;
                ChallengeDefinition def = run.Challenges.Get(JsonRead.String(c, "id"));
                if (def == null) continue;
                var instance = new ChallengeInstance(def, JsonRead.Int(c, "round"))
                {
                    RoundsLeft = JsonRead.Int(c, "left"), Progress = JsonRead.Int(c, "progress"), Target = JsonRead.Long(c, "target"),
                    Accumulated = JsonRead.Long(c, "acc"), Status = (ChallengeStatus)JsonRead.Int(c, "status")
                };
                challengeHistory.Add(instance);
                if (i == activeIndex) activeChallenge = instance;
            }
            string challengeOffer = JsonRead.String(root, "challengeOffer");
            run.Challenges.Restore(challengeHistory, activeChallenge, JsonRead.Int(root, "challengesWon"), challengeOffer != null ? run.Challenges.Get(challengeOffer) : null);

            var events = new List<string>();
            foreach (object o in JsonRead.Array(root, "events")) if (o is string e) events.Add(e);
            run.Events.RestoreHistory(events);
            string forced = JsonRead.String(root, "forcedEvent");
            if (forced != null && run.Events.Get(forced) != null) run.Events.Force(run.Events.Get(forced));

            IDictionary<string, object> plan = JsonRead.Object(root, "plan");
            RuleDefinition planned = plan != null ? run.Rules.Catalog.Get(JsonRead.String(plan, "id")) : null;
            run.GameMaster.SetPlan(planned != null ? new InterventionCandidate(planned, (float)JsonRead.Double(plan, "score", 1), JsonRead.Int(plan, "param", -1)) : (InterventionCandidate?)null);

            IDictionary<string, object> shop = JsonRead.Object(root, "shop");
            var shelf = new List<ShopOffer>();
            foreach (object o in JsonRead.Array(shop, "shelf"))
            {
                var so = o as IDictionary<string, object>;
                ShopItem item = run.Shop.Find(JsonRead.String(so, "id"));
                if (item != null) shelf.Add(new ShopOffer(item, JsonRead.Long(so, "price"), JsonRead.Int(so, "left")));
            }
            run.Shop.RestoreShelf(shelf, JsonRead.Long(shop, "spent"));

            IDictionary<string, object> pay = JsonRead.Object(root, "payments");
            var settledStages = new List<int>();
            foreach (object o in JsonRead.Array(pay, "settled")) settledStages.Add((int)JsonRead.AsLong(o));
            var missedStages = new List<int>();
            foreach (object o in JsonRead.Array(pay, "missedStages")) missedStages.Add((int)JsonRead.AsLong(o));
            run.Payments.Restore(JsonRead.Long(pay, "paid"), JsonRead.Int(pay, "missed"), settledStages, missedStages);

            IDictionary<string, object> fin = JsonRead.Object(root, "final");
            run.Final.OfferAnswered = JsonRead.Bool(fin, "answered");
            int answer = JsonRead.Int(fin, "answer", -1);
            run.Final.Answer = answer >= 0 ? (FinalOfferChoice?)answer : null;
            run.Final.HisGameBuilt = JsonRead.Bool(fin, "built");

            IDictionary<string, object> rec = JsonRead.Object(root, "records");
            var read = new List<string>();
            foreach (object o in JsonRead.Array(rec, "read")) if (o is string id) read.Add(id);
            var rested = new List<int>();
            foreach (object o in JsonRead.Array(rec, "rested")) rested.Add((int)JsonRead.AsLong(o));
            run.Records.Restore(read, rested);

            run.RefreshStageSettings();
            run.SyncTable();
            run.Run.EvaluateOutcome();
            return run;
        }

        // ------------------------------------------------------------------ meta

        public static string SaveMeta(MetaProgress meta)
        {
            var root = new Obj
            {
                { "version", Version }, { "runsStarted", meta.RunsStarted }, { "runsFinished", meta.RunsFinished }, { "marks", meta.Marks },
                { "bestRound", meta.BestRound }, { "bestDebtPaid", meta.BestDebtPaid }, { "unlocked", new List<object>(meta.Unlocked) },
                { "endings", ToObj(new Dictionary<string, int>(ToDict(meta.Endings))) }
            };
            return MiniJson.Serialize(root);
        }

        public static MetaProgress LoadMeta(string json)
        {
            var meta = new MetaProgress();
            if (string.IsNullOrEmpty(json)) return meta;
            var root = MiniJson.Deserialize(json) as IDictionary<string, object>;
            if (root == null) return meta;
            meta.RunsStarted = JsonRead.Int(root, "runsStarted");
            meta.RunsFinished = JsonRead.Int(root, "runsFinished");
            meta.Marks = JsonRead.Int(root, "marks");
            meta.BestRound = JsonRead.Int(root, "bestRound");
            meta.BestDebtPaid = JsonRead.Long(root, "bestDebtPaid");
            var unlocked = new List<string>();
            foreach (object o in JsonRead.Array(root, "unlocked")) if (o is string id) unlocked.Add(id);
            meta.Restore(unlocked, ToIntDict(JsonRead.Object(root, "endings")));
            return meta;
        }

        // ------------------------------------------------------------------ helpers

        public static Obj WheelToObj(WheelConfiguration wheel)
        {
            var pockets = new List<object>();
            foreach (Pocket p in wheel.Pockets)
            {
                pockets.Add(new List<object> { p.Id, p.Number, (int)p.Color, p.Weight, p.IsActive, (int)p.Special, p.PayoutPercent });
            }
            List<object> ring = null;
            if (wheel.Ring != null)
            {
                ring = new List<object>();
                foreach (RingSegment seg in wheel.Ring.Segments) ring.Add(new List<object> { seg.MultiplierPercent, seg.Weight });
            }
            return new Obj { { "name", wheel.Name }, { "pockets", pockets }, { "ring", ring } };
        }

        public static WheelConfiguration WheelFromObj(IDictionary<string, object> map)
        {
            if (map == null) throw new FormatException("The save has no wheel.");
            var pockets = new List<Pocket>();
            foreach (object o in JsonRead.Array(map, "pockets"))
            {
                var a = o as List<object>;
                if (a == null || a.Count < 7) throw new FormatException("A saved pocket is malformed.");
                pockets.Add(new Pocket((int)JsonRead.AsLong(a[0]), (int)JsonRead.AsLong(a[1]), (PocketColor)JsonRead.AsLong(a[2]), (int)JsonRead.AsLong(a[3]),
                    a[4] is bool active && active, (SpecialPocketType)JsonRead.AsLong(a[5]), (int)JsonRead.AsLong(a[6])));
            }
            WheelRing ring = null;
            if (JsonRead.Get(map, "ring") is List<object> r)
            {
                var segments = new List<RingSegment>();
                foreach (object o in r)
                {
                    var a = (List<object>)o;
                    segments.Add(new RingSegment((int)JsonRead.AsLong(a[0]), (int)JsonRead.AsLong(a[1])));
                }
                ring = new WheelRing(segments);
            }
            var wheel = new WheelConfiguration(JsonRead.String(map, "name", "Saved wheel"), pockets, ring);
            wheel.ThrowIfInvalid();
            return wheel;
        }

        private static IEnumerable<object> Box<T>(IEnumerable<T> values)
        {
            foreach (T v in values) yield return v;
        }

        private static Obj ToObj<T>(IDictionary<string, T> map)
        {
            var o = new Obj();
            foreach (KeyValuePair<string, T> p in map) o[p.Key] = p.Value;
            return o;
        }

        private static IDictionary<string, int> ToDict(IReadOnlyDictionary<string, int> map)
        {
            var d = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> p in map) d[p.Key] = p.Value;
            return d;
        }

        private static Dictionary<string, int> ToIntDict(IDictionary<string, object> map)
        {
            var d = new Dictionary<string, int>();
            if (map == null) return d;
            foreach (KeyValuePair<string, object> p in map) d[p.Key] = (int)JsonRead.AsLong(p.Value);
            return d;
        }
    }
}

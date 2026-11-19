using System;
using System.Collections.Generic;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Specials
{
    /// <summary>
    /// Configuration and behaviour of one special pocket type. Every number is a settable property
    /// so designers can tune it; <see cref="Resolve"/> is the outcome when a ball lands there. The
    /// presentation (two-letter code, colour, animation and audio cue) lives here too so the view
    /// and the audio system never hard-code pocket types.
    /// </summary>
    public sealed class SpecialPocketDefinition
    {
        public SpecialPocketType Type { get; }
        public string Name { get; }
        public string Code { get; }
        public string Description { get; set; }
        /// <summary>Main amount (dollars, Influence, seconds) used by the outcome.</summary>
        public long Amount { get; set; }
        /// <summary>Main percentage used by the outcome.</summary>
        public int Percent { get; set; }
        /// <summary>RGB hex for the pocket's marker.</summary>
        public string ColorHex { get; set; }
        public string AnimationCue { get; set; }
        public string AudioCue { get; set; }

        private readonly Func<SpecialPocketDefinition, IGameHost, RoundResult, Pocket, string> _resolve;

        public SpecialPocketDefinition(SpecialPocketType type, string name, string code, string description, long amount, int percent,
                                       string colorHex, string animationCue, string audioCue,
                                       Func<SpecialPocketDefinition, IGameHost, RoundResult, Pocket, string> resolve)
        {
            Type = type;
            Name = name;
            Code = code;
            Description = description;
            Amount = amount;
            Percent = percent;
            ColorHex = colorHex;
            AnimationCue = animationCue;
            AudioCue = audioCue;
            _resolve = resolve;
        }

        /// <summary>Applies the pocket's outcome. Returns the message shown to the player.</summary>
        public string Resolve(IGameHost host, RoundResult result, Pocket pocket)
        {
            return _resolve(this, host, result, pocket);
        }
    }

    /// <summary>What happened when a ball hit a special pocket (for the HUD and the animation).</summary>
    public sealed class SpecialPocketOutcome
    {
        public SpecialPocketDefinition Definition { get; }
        public Pocket Pocket { get; }
        public string Message { get; }

        public SpecialPocketOutcome(SpecialPocketDefinition definition, Pocket pocket, string message)
        {
            Definition = definition;
            Pocket = pocket;
            Message = message;
        }
    }

    /// <summary>The ten special pockets from the design.</summary>
    public sealed class SpecialPocketCatalog
    {
        private readonly Dictionary<SpecialPocketType, SpecialPocketDefinition> _byType = new Dictionary<SpecialPocketType, SpecialPocketDefinition>();

        public IEnumerable<SpecialPocketDefinition> All => _byType.Values;

        public SpecialPocketDefinition Get(SpecialPocketType type)
        {
            SpecialPocketDefinition d;
            return _byType.TryGetValue(type, out d) ? d : null;
        }

        public void Add(SpecialPocketDefinition definition) { _byType[definition.Type] = definition; }

        /// <summary>Two-letter label for a pocket marker.</summary>
        public static string CodeOf(SpecialPocketType type)
        {
            switch (type)
            {
                case SpecialPocketType.Skull: return "SK";
                case SpecialPocketType.Jackpot: return "JP";
                case SpecialPocketType.Mystery: return "??";
                case SpecialPocketType.Bomb: return "BM";
                case SpecialPocketType.Hourglass: return "HG";
                case SpecialPocketType.Tax: return "TX";
                case SpecialPocketType.Influence: return "IN";
                case SpecialPocketType.DebtRelief: return "DR";
                case SpecialPocketType.Curse: return "CU";
                case SpecialPocketType.GoldenTicket: return "GT";
                default: return "";
            }
        }

        /// <summary>Negative rules a curse can impose.</summary>
        public static readonly string[] CursePool = { "house_cut", "short_clock", "no_takebacks", "payout_squeeze", "blackout", "higher_stakes", "plain_roulette" };

        public static SpecialPocketCatalog CreateDefault()
        {
            var c = new SpecialPocketCatalog();

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Skull, "Skull", "SK", "A penalty: the house takes a fine that grows with each stage.", 250, 8,
                "D8D8D0", "skull-flash", "skull", (d, host, r, p) =>
                {
                    long fine = Math.Max(d.Amount * host.Stage, host.Ledger.Balance * d.Percent / 100);
                    long taken = host.Ledger.Balance > 0 ? host.Ledger.ForceDebit(fine, TransactionKind.Penalty, "Skull pocket") : 0;
                    long rest = fine - taken;
                    long added = rest > 0 ? host.IncreaseDebt(rest, "Skull pocket (unpaid fine)") : 0;
                    return "Skull. The house fines you $" + fine.ToString("N0") + (added > 0 ? " ($" + added.ToString("N0") + " added to your debt)" : "") + ".";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Jackpot, "Jackpot", "JP", "A large cash prize, or a rare item.", 1500, 50,
                "F2C14E", "jackpot-burst", "jackpot", (d, host, r, p) =>
                {
                    if (host.Random.NextInt(100) < d.Percent)
                    {
                        long prize = d.Amount * host.Stage;
                        host.Ledger.Credit(prize, TransactionKind.Reward, "Jackpot pocket");
                        return "JACKPOT. $" + prize.ToString("N0") + ".";
                    }
                    string[] items = { "bet_insurance", "double_down", "chip_recall", "debt_freeze" };
                    string item = items[host.Random.NextInt(items.Length)];
                    host.Inventory.AddCharges(item, 1);
                    host.Inventory.AddChips(ChipKind.Protected, 2);
                    return "Jackpot: a free " + item.Replace('_', ' ') + " charge and two protected chips.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Mystery, "Mystery", "??", "Something happens. Nobody knows what.", 0, 0,
                "8E5CC9", "mystery-swirl", "mystery", (d, host, r, p) =>
                {
                    host.TriggerRandomEvent();
                    return "Mystery pocket: the casino stirs.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Bomb, "Bomb", "BM", "Destroys the pockets on either side, then goes inert.", 0, 0,
                "E05A2A", "bomb-blast", "explosion", (d, host, r, p) =>
                {
                    int destroyed = 0;
                    bool ok = host.ChangeBaseWheel(wheel =>
                    {
                        int index = wheel.IndexOfId(p.Id);
                        if (index < 0) return WheelChange.Refused(wheel, "The bomb was not part of the house's wheel.");
                        WheelConfiguration current = wheel;
                        WheelChange inert = WheelTransforms.SetSpecial(current, index, SpecialPocketType.None);
                        if (inert.Succeeded) current = inert.After;
                        foreach (int side in new[] { 1, -1 })
                        {
                            int i = current.IndexOfId(p.Id);
                            int n = current.PocketCount;
                            int target = ((i + side) % n + n) % n;
                            if (current.Pockets[target].IsZero) continue;
                            WheelChange removed = WheelTransforms.RemoveAt(current, target);
                            if (!removed.Succeeded) continue;
                            current = removed.After;
                            destroyed++;
                        }
                        return new WheelChange(wheel, current, WheelChangeKind.Remove, "A bomb pocket explodes.");
                    });
                    return ok && destroyed > 0 ? "BOOM. " + destroyed + " neighbouring pocket" + (destroyed == 1 ? " is" : "s are") + " destroyed." : "The bomb fizzles.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Hourglass, "Hourglass", "HG", "The next betting window is longer or shorter.", 10, 50,
                "6FB7D6", "hourglass-flip", "sand", (d, host, r, p) =>
                {
                    bool longer = host.Random.NextInt(100) < d.Percent;
                    host.AdjustNextBettingTime(longer ? d.Amount : -d.Amount);
                    return "Hourglass: next betting window " + (longer ? "+" : "-") + d.Amount + " seconds.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Tax, "Tax", "TX", "The house takes a percentage of your balance.", 0, 10,
                "7A8A6A", "tax-stamp", "coins-out", (d, host, r, p) =>
                {
                    long tax = host.Ledger.Balance * d.Percent / 100;
                    if (tax <= 0) return "Tax pocket: nothing left to tax.";
                    host.Ledger.ForceDebit(tax, TransactionKind.Penalty, "Tax pocket");
                    return "Tax: " + d.Percent + "% of your balance ($" + tax.ToString("N0") + ").";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Influence, "Influence", "IN", "You gain Influence.", 2, 0,
                "5BC28B", "influence-glow", "whisper", (d, host, r, p) =>
                {
                    host.Influence.Earn((int)d.Amount, "Influence pocket");
                    return "+" + d.Amount + " Influence.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.DebtRelief, "Debt Relief", "DR", "Part of your debt disappears.", 1000, 0,
                "A3D977", "relief-rise", "chime", (d, host, r, p) =>
                {
                    long relief = host.Ledger.ForgiveDebt(d.Amount * host.Stage, "Debt relief pocket");
                    return "Debt relief: $" + relief.ToString("N0") + " forgiven.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.Curse, "Curse", "CU", "A negative rule takes hold, free of charge.", 0, 0,
                "5E2A5E", "curse-smoke", "curse", (d, host, r, p) =>
                {
                    var pool = new List<string>(CursePool);
                    while (pool.Count > 0)
                    {
                        int i = host.Random.NextInt(pool.Count);
                        string id = pool[i];
                        pool.RemoveAt(i);
                        if (host.Rules.IsActive(id)) continue;
                        if (host.ActivateRule(id, true)) return "Cursed: " + host.Rules.Catalog.Get(id).Name + ".";
                    }
                    return "The curse finds nothing new to take.";
                }));

            c.Add(new SpecialPocketDefinition(SpecialPocketType.GoldenTicket, "Golden Ticket", "GT", "Cancels the Game Master's next intervention.", 1, 0,
                "FFD700", "ticket-shine", "ticket", (d, host, r, p) =>
                {
                    host.Inventory.AddGoldenTicket((int)d.Amount);
                    return "A golden ticket. His next intervention will be cancelled.";
                }));

            return c;
        }

        /// <summary>Resolves every special pocket the balls landed on, in order.</summary>
        public List<SpecialPocketOutcome> ResolveAll(IGameHost host, RoundResult result)
        {
            var outcomes = new List<SpecialPocketOutcome>();
            foreach (Pocket pocket in result.Pockets)
            {
                if (pocket.Special == SpecialPocketType.None) continue;
                SpecialPocketDefinition def = Get(pocket.Special);
                if (def == null) continue;
                string message = def.Resolve(host, result, pocket);
                outcomes.Add(new SpecialPocketOutcome(def, pocket, message));
                host.Say(DialogueTrigger.SpecialPocket, pocket.Special.ToString());
                host.Notify(message);
            }
            return outcomes;
        }
    }
}

using System;
using System.Collections.Generic;
using DeadSpin.Core.Common;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Progression
{
    public enum FinalPhase
    {
        None,
        /// <summary>He brings back the modifications you have already survived.</summary>
        HisHouse,
        /// <summary>New configurations: rings, duplicated numbers, special pockets.</summary>
        HisGame,
        /// <summary>A dangerous contract in exchange for a chance to escape.</summary>
        HisFinalOffer,
        /// <summary>One final high-stakes spin.</summary>
        TheLastSpin
    }

    public enum FinalOfferChoice
    {
        /// <summary>Sign "His Final Offer": the debt disappears and the player works for the house (The Deal).</summary>
        TheDeal,
        /// <summary>Sign "His Escape Clause": winnings on the last spin clear debt three to one; losing it costs everything.</summary>
        EscapeClause,
        /// <summary>Refuse both and play the last spin as it is.</summary>
        Refuse
    }

    /// <summary>
    /// THE FINAL GAME MASTER ENCOUNTER, in four phases across the rounds of stage 5. Which wheel he
    /// builds for "His Game" is randomised per run (<see cref="Variant"/>).
    /// </summary>
    public sealed class FinalEncounter
    {
        public const int VariantCount = 3;

        public int FirstRound { get; }
        public int LastRound { get; }
        public int Variant { get; }
        public bool OfferAnswered { get; set; }
        public FinalOfferChoice? Answer { get; set; }
        public bool HisGameBuilt { get; set; }
        public bool HisHouseDone { get; set; }

        public FinalEncounter(int firstRound, int lastRound, int variant)
        {
            FirstRound = firstRound;
            LastRound = lastRound;
            Variant = ((variant % VariantCount) + VariantCount) % VariantCount;
        }

        public FinalPhase PhaseFor(int round)
        {
            if (round < FirstRound || round > LastRound) return FinalPhase.None;
            if (round == LastRound) return FinalPhase.TheLastSpin;
            if (round == FirstRound) return FinalPhase.HisHouse;
            return FinalPhase.HisGame;
        }

        /// <summary>The offer comes before the last spin: when the last round is about to begin and it has not been answered.</summary>
        public bool OfferPending(int nextRound) { return nextRound == LastRound && !OfferAnswered; }

        public static string VariantName(int variant)
        {
            switch (variant)
            {
                case 0: return "Seventy-Two and the Ring";
                case 1: return "Two Balls, Forty-Eight Pockets";
                default: return "Ninety-Nine";
            }
        }

        /// <summary>Builds "His Game": a new configuration with rings, duplicated numbers and special pockets. Each step is validated; a refused step is skipped.</summary>
        public WheelChange BuildHisGame(WheelConfiguration current, IRandomSource random)
        {
            WheelConfiguration w = current;
            Func<WheelChange, bool> take = c =>
            {
                if (c == null || !c.Succeeded) return false;
                w = c.After;
                return true;
            };
            switch (Variant)
            {
                case 0:
                    take(WheelTransforms.Resize(w, 72));
                    take(WheelTransforms.CloneNumber(w, w.Contains(13) ? 13 : 7, 3));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Skull, random));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Jackpot, random));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Curse, random));
                    take(WheelTransforms.SetRing(w, WheelRing.Standard()));
                    break;
                case 1:
                    take(WheelTransforms.Resize(w, 48));
                    take(WheelTransforms.CloneNumber(w, w.Contains(8) ? 8 : 1, 2));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Bomb, random));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Jackpot, random));
                    take(WheelTransforms.SetRing(w, WheelRing.Standard()));
                    break;
                default:
                    take(WheelTransforms.Resize(w, 99));
                    take(WheelTransforms.CloneNumber(w, w.Contains(66) ? 66 : 6, 3));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Tax, random));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Mystery, random));
                    take(WheelTransforms.InsertSpecial(w, SpecialPocketType.Jackpot, random));
                    take(WheelTransforms.SetRing(w, WheelRing.Standard()));
                    break;
            }
            return ReferenceEquals(w, current)
                ? WheelChange.Refused(current, "His Game could not be built.")
                : new WheelChange(current, w, WheelChangeKind.Replace, "His Game: " + VariantName(Variant) + ".");
        }
    }

    /// <summary>Facts the ending rules look at.</summary>
    public sealed class EndingContext
    {
        public RunOutcome RawOutcome;
        public long Balance;
        public long Stored;
        public long Debt;
        public long StartingDebt;
        public int Stage;
        public int LoopholeEvidence;
        public bool ReachedFinalTable;
    }

    /// <summary>
    /// One ending. Endings are checked in priority order against the raw outcome; the first whose
    /// condition holds is the ending. Add an ending by adding a definition (and, if it needs a new
    /// terminal state, a <see cref="RunOutcome"/> value).
    /// </summary>
    public sealed class EndingDefinition
    {
        public string Id { get; }
        public RunOutcome Outcome { get; }
        public string Title { get; }
        public string Text { get; }
        public int Priority { get; }
        /// <summary>When this ending applies; it can turn one raw outcome into another (Freedom into The New Owner).</summary>
        public Func<EndingContext, bool> Condition { get; }
        public bool IsVictory { get; }

        public EndingDefinition(string id, RunOutcome outcome, string title, string text, int priority, bool victory, Func<EndingContext, bool> condition)
        {
            Id = id;
            Outcome = outcome;
            Title = title;
            Text = text;
            Priority = priority;
            IsVictory = victory;
            Condition = condition;
        }
    }

    public sealed class EndingCatalog
    {
        /// <summary>Evidence (loopholes exploited, hidden rules exposed, secrets found) needed for The Loophole.</summary>
        public const int LoopholeEvidenceNeeded = 6;
        /// <summary>The Loophole still needs the debt brought down to this share of where it started.</summary>
        public const double LoopholeDebtShare = 0.75;

        private readonly List<EndingDefinition> _endings = new List<EndingDefinition>();
        public IReadOnlyList<EndingDefinition> All => _endings;

        public void Add(EndingDefinition ending)
        {
            _endings.Add(ending);
            _endings.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public EndingDefinition Get(string id)
        {
            foreach (EndingDefinition e in _endings) if (e.Id == id) return e;
            return null;
        }

        public static EndingCatalog CreateDefault()
        {
            var c = new EndingCatalog();
            c.Add(new EndingDefinition("the_deal", RunOutcome.TheDeal, "The Deal",
                "You signed. The debt is gone, and so is the door. Tomorrow night you will deal the cards for someone else.", 100, false,
                ctx => ctx.RawOutcome == RunOutcome.TheDeal));
            c.Add(new EndingDefinition("new_owner", RunOutcome.NewOwner, "The New Owner",
                "You paid every cent at his own final table and still had the house's money in your pockets. He slides the keys across the felt.", 90, true,
                ctx => ctx.RawOutcome == RunOutcome.Freedom && ctx.ReachedFinalTable && ctx.Balance + ctx.Stored >= ctx.StartingDebt / 4));
            c.Add(new EndingDefinition("freedom", RunOutcome.Freedom, "Freedom",
                "Paid in full. The door at the top of the stairs is unlocked. You do not look back.", 80, true,
                ctx => ctx.RawOutcome == RunOutcome.Freedom));
            c.Add(new EndingDefinition("loophole", RunOutcome.Loophole, "The Loophole",
                "You read his rules more closely than he did. The contract never said what he thought it said. You walk out owing money that can never be collected.", 70, true,
                ctx => (ctx.RawOutcome == RunOutcome.DeadlineMissed || ctx.RawOutcome == RunOutcome.Loophole)
                       && ctx.LoopholeEvidence >= LoopholeEvidenceNeeded && ctx.Debt <= (long)(ctx.StartingDebt * LoopholeDebtShare)));
            c.Add(new EndingDefinition("house_always_wins", RunOutcome.Bankrupt, "The House Always Wins",
                "Your last chip rolls into the wrong pocket. The lights go out one by one.", 10, false,
                ctx => ctx.RawOutcome == RunOutcome.Bankrupt));
            c.Add(new EndingDefinition("house_always_wins_deadline", RunOutcome.DeadlineMissed, "The House Always Wins",
                "The last round ends with the debt still on the board. The staff are already setting your place for tomorrow.", 5, false,
                ctx => ctx.RawOutcome == RunOutcome.DeadlineMissed));
            return c;
        }

        /// <summary>The ending for a raw outcome.</summary>
        public EndingDefinition Resolve(EndingContext context)
        {
            foreach (EndingDefinition e in _endings) if (e.Condition(context)) return e;
            return null;
        }
    }
}

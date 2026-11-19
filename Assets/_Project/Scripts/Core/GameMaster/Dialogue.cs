using System;
using System.Collections.Generic;
using DeadSpin.Core.Common;

namespace DeadSpin.Core.GameMaster
{
    /// <summary>Moments the Game Master can react to.</summary>
    public enum DialogueTrigger
    {
        RunStart,
        StageStart,
        RoundStart,
        BettingOpen,
        BettingClosed,
        PlayerWinSmall,
        PlayerWinBig,
        PlayerLoss,
        PlayerNoBet,
        NearBankrupt,
        NearFreedom,
        Loophole,
        RepeatedExploit,
        AbilityUsed,
        RuleCancelled,
        ContractOffer,
        ContractAccepted,
        ContractRefused,
        EventStart,
        SpecialPocket,
        ChallengeOffer,
        ChallengeWon,
        ChallengeLost,
        InstallmentPaid,
        FinalPhase,
        Ending,
        Idle
    }

    /// <summary>
    /// One line of Game Master dialogue. <see cref="VoiceClipId"/> names the recorded clip to play
    /// once voice acting exists (the audio layer falls back to synthesised "voice" blips).
    /// </summary>
    public sealed class DialogueLine
    {
        public string Id { get; }
        public DialogueTrigger Trigger { get; }
        public string Text { get; }
        /// <summary>Only used in this mood, or any mood when null.</summary>
        public GameMasterMood? Mood { get; }
        public int MinStage { get; }
        public int MaxStage { get; }
        /// <summary>Only for this personality id, or any when null.</summary>
        public string Personality { get; }
        /// <summary>Extra key for triggers with variants (stage number, final phase, ending id, pocket type).</summary>
        public string Key { get; }
        public string VoiceClipId => "gm_" + Id;

        public DialogueLine(string id, DialogueTrigger trigger, string text, GameMasterMood? mood = null, int minStage = 1, int maxStage = 5, string personality = null, string key = null)
        {
            Id = id;
            Trigger = trigger;
            Text = text;
            Mood = mood;
            MinStage = minStage;
            MaxStage = maxStage;
            Personality = personality;
            Key = key;
        }
    }

    /// <summary>A line chosen for a moment, with the mood it was delivered in (for animation).</summary>
    public sealed class SpokenLine
    {
        public DialogueLine Line { get; }
        public string Text { get; }
        public GameMasterMood Mood { get; }
        public DialogueTrigger Trigger { get; }

        public SpokenLine(DialogueLine line, string text, GameMasterMood mood, DialogueTrigger trigger)
        {
            Line = line;
            Text = text;
            Mood = mood;
            Trigger = trigger;
        }
    }

    /// <summary>
    /// Contextual dialogue: picks a fitting line for a trigger, preferring lines written for the
    /// current mood, stage and personality, and avoiding recent repeats. Text may contain
    /// placeholders ({amount}, {number}, {name}) filled in by the caller.
    /// </summary>
    public sealed class DialogueBank
    {
        private readonly List<DialogueLine> _lines = new List<DialogueLine>();
        private readonly List<string> _recent = new List<string>();

        public IReadOnlyList<DialogueLine> Lines => _lines;

        public void Add(DialogueLine line) { _lines.Add(line); }

        public SpokenLine Pick(DialogueTrigger trigger, GameMasterMood mood, int stage, string personality, IRandomSource random,
                               string key = null, IDictionary<string, string> values = null)
        {
            var best = new List<DialogueLine>();
            int bestScore = -1;
            foreach (DialogueLine line in _lines)
            {
                if (line.Trigger != trigger) continue;
                if (stage < line.MinStage || stage > line.MaxStage) continue;
                if (line.Key != null && line.Key != key) continue;
                if (line.Mood.HasValue && line.Mood.Value != mood) continue;
                if (line.Personality != null && line.Personality != personality) continue;
                int score = (line.Mood.HasValue ? 2 : 0) + (line.Personality != null ? 2 : 0) + (line.Key != null ? 4 : 0) + (_recent.Contains(line.Id) ? -10 : 0);
                if (score > bestScore) { best.Clear(); bestScore = score; }
                if (score == bestScore) best.Add(line);
            }
            if (best.Count == 0) return null;
            DialogueLine chosen = best[random.NextInt(best.Count)];
            _recent.Add(chosen.Id);
            if (_recent.Count > 12) _recent.RemoveAt(0);
            string text = chosen.Text;
            if (values != null) foreach (KeyValuePair<string, string> v in values) text = text.Replace("{" + v.Key + "}", v.Value);
            return new SpokenLine(chosen, text, mood, trigger);
        }

        /// <summary>The default script (placeholder writing, structured for voice acting).</summary>
        public static DialogueBank CreateDefault()
        {
            var b = new DialogueBank();
            int n = 0;
            Func<string> id = () => "l" + (++n).ToString("000");

            b.Add(new DialogueLine(id(), DialogueTrigger.RunStart, "Welcome to my table. You owe me {debt}. You have {rounds} spins to repay it. Shall we?"));
            b.Add(new DialogueLine(id(), DialogueTrigger.RunStart, "Ah. Another guest. The debt is {debt}. The door opens when it reaches zero."));

            b.Add(new DialogueLine(id(), DialogueTrigger.StageStart, "The Invitation. Plain roulette... mostly. Learn the table while I am still being polite.", key: "1"));
            b.Add(new DialogueLine(id(), DialogueTrigger.StageStart, "The House Rules. From now on the wheel is mine to reshape. Special pockets. Special bets.", key: "2"));
            b.Add(new DialogueLine(id(), DialogueTrigger.StageStart, "The Debt. Payments are due. Contracts are on the table. My patience is not.", key: "3"));
            b.Add(new DialogueLine(id(), DialogueTrigger.StageStart, "No Turning Back. Every trick I know, all at once.", key: "4"));
            b.Add(new DialogueLine(id(), DialogueTrigger.StageStart, "The Final Table. Just you and me now.", key: "5"));

            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. The house rules are... as you see them."));
            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. Let us continue."));
            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. You are doing wonderfully. For me.", GameMasterMood.Amused));
            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. Do not get comfortable.", GameMasterMood.Irritated));
            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. I am watching your hands very closely.", GameMasterMood.Suspicious));
            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. You think you are clever.", GameMasterMood.Angry));
            b.Add(new DialogueLine(id(), DialogueTrigger.RoundStart, "Round {round}. ...It is only a game. Isn't it?", GameMasterMood.Desperate));

            b.Add(new DialogueLine(id(), DialogueTrigger.BettingOpen, "Place your bets."));
            b.Add(new DialogueLine(id(), DialogueTrigger.BettingOpen, "The table is open."));
            b.Add(new DialogueLine(id(), DialogueTrigger.BettingOpen, "Bet. Quickly now.", GameMasterMood.Irritated));
            b.Add(new DialogueLine(id(), DialogueTrigger.BettingClosed, "No more bets."));
            b.Add(new DialogueLine(id(), DialogueTrigger.BettingClosed, "Rien ne va plus."));

            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerWinSmall, "{pocket}. A crumb. Enjoy it."));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerWinSmall, "{pocket}. You win {amount}. Don't let it go to your head."));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerWinBig, "{pocket}. ...{amount}. Hm.", GameMasterMood.Irritated));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerWinBig, "{pocket}. {amount}. The house will remember that."));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerWinBig, "{pocket}. {amount}. Luck is a loan, you know. I collect on loans.", personality: "collector"));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerLoss, "{pocket}. The house thanks you for {amount}."));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerLoss, "{pocket}. Ah, what a pity. {amount}, gone.", GameMasterMood.Amused));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerLoss, "{pocket}. Delicious."));
            b.Add(new DialogueLine(id(), DialogueTrigger.PlayerNoBet, "{pocket}. Sitting out? Time is not on your side."));
            b.Add(new DialogueLine(id(), DialogueTrigger.NearBankrupt, "Your pockets are nearly empty. I can smell it."));
            b.Add(new DialogueLine(id(), DialogueTrigger.NearBankrupt, "Careful. One more mistake and you stay here forever."));
            b.Add(new DialogueLine(id(), DialogueTrigger.NearFreedom, "You are... closer than I would like.", GameMasterMood.Desperate));
            b.Add(new DialogueLine(id(), DialogueTrigger.NearFreedom, "The door is right there. Surely one more spin?"));

            b.Add(new DialogueLine(id(), DialogueTrigger.Loophole, "That should not have paid. ...It did. Clever.", GameMasterMood.Suspicious));
            b.Add(new DialogueLine(id(), DialogueTrigger.Loophole, "You read the small print. How rude."));
            b.Add(new DialogueLine(id(), DialogueTrigger.RepeatedExploit, "Again? I will be closing that gap.", GameMasterMood.Angry));
            b.Add(new DialogueLine(id(), DialogueTrigger.RepeatedExploit, "Do not mistake my rules for mistakes."));

            b.Add(new DialogueLine(id(), DialogueTrigger.AbilityUsed, "A little trick. I saw that."));
            b.Add(new DialogueLine(id(), DialogueTrigger.AbilityUsed, "Using your Influence on me? Bold."));
            b.Add(new DialogueLine(id(), DialogueTrigger.RuleCancelled, "You dare cancel my rule? ...Fine. This time."));

            b.Add(new DialogueLine(id(), DialogueTrigger.ContractOffer, "I have an offer for you. Read it carefully. Or don't."));
            b.Add(new DialogueLine(id(), DialogueTrigger.ContractOffer, "Business, then. A contract."));
            b.Add(new DialogueLine(id(), DialogueTrigger.ContractAccepted, "Signed. Pleasure doing business."));
            b.Add(new DialogueLine(id(), DialogueTrigger.ContractRefused, "Wise. Or cowardly. We will see."));

            b.Add(new DialogueLine(id(), DialogueTrigger.EventStart, "{name}."));
            b.Add(new DialogueLine(id(), DialogueTrigger.EventStart, "Something special tonight: {name}."));
            b.Add(new DialogueLine(id(), DialogueTrigger.ChallengeOffer, "A challenge: {name}. Succeed, and I will reward you."));
            b.Add(new DialogueLine(id(), DialogueTrigger.ChallengeWon, "You met my challenge. A deal is a deal."));
            b.Add(new DialogueLine(id(), DialogueTrigger.ChallengeLost, "You failed my little test. As expected."));
            b.Add(new DialogueLine(id(), DialogueTrigger.InstallmentPaid, "Your payment of {amount} is received. On time. How dull."));

            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "The skull. You know what that means.", key: "Skull"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Jackpot. ...The house is generous. Occasionally.", key: "Jackpot"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "A mystery. Even I am curious.", key: "Mystery"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Boom.", key: "Bomb"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Time bends at my table.", key: "Hourglass"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Tax season.", key: "Tax"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Influence. Use it wisely. You won't.", key: "Influence"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Debt relief. Do not thank me.", key: "DebtRelief"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "Cursed. How unfortunate.", key: "Curse"));
            b.Add(new DialogueLine(id(), DialogueTrigger.SpecialPocket, "A golden ticket. Keep it safe. I will want it back.", key: "GoldenTicket"));

            b.Add(new DialogueLine(id(), DialogueTrigger.FinalPhase, "His House. Everything you have seen, all at once.", key: "1"));
            b.Add(new DialogueLine(id(), DialogueTrigger.FinalPhase, "His Game. My wheel, my rules, my rings.", key: "2"));
            b.Add(new DialogueLine(id(), DialogueTrigger.FinalPhase, "His Final Offer. Stay, work for me, and the debt disappears. Or spin once more.", key: "3"));
            b.Add(new DialogueLine(id(), DialogueTrigger.FinalPhase, "The Last Spin. Everything on one turn of the wheel.", key: "4"));

            b.Add(new DialogueLine(id(), DialogueTrigger.Ending, "Paid in full. ...The door is open. Go.", key: "Freedom"));
            b.Add(new DialogueLine(id(), DialogueTrigger.Ending, "You beat me at my own table. The casino... is yours.", key: "NewOwner"));
            b.Add(new DialogueLine(id(), DialogueTrigger.Ending, "The house always wins.", key: "Bankrupt"));
            b.Add(new DialogueLine(id(), DialogueTrigger.Ending, "Time is up. The house always wins.", key: "DeadlineMissed"));
            b.Add(new DialogueLine(id(), DialogueTrigger.Ending, "Welcome to the staff. Your first shift starts now.", key: "TheDeal"));
            b.Add(new DialogueLine(id(), DialogueTrigger.Ending, "...The contract says what it says. Get out before I rewrite it.", key: "Loophole"));

            b.Add(new DialogueLine(id(), DialogueTrigger.Idle, "Take your time. I have all of it."));
            b.Add(new DialogueLine(id(), DialogueTrigger.Idle, "The wheel is waiting. So am I."));
            return b;
        }
    }
}

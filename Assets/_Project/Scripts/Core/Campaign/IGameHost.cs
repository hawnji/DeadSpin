using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Campaign
{
    /// <summary>
    /// The narrow surface that contracts, special pockets, abilities, events and challenges use to
    /// affect the game. Implemented by <see cref="CasinoRun"/>; content classes never hold references
    /// to each other, so new content plugs in without touching existing systems.
    /// </summary>
    public interface IGameHost
    {
        Run Run { get; }
        Ledger Ledger { get; }
        TableGame Table { get; }
        RuleEngine Rules { get; }
        InfluenceWallet Influence { get; }
        Inventory Inventory { get; }
        PlayerProfile Profile { get; }
        AuthorityPool Authority { get; }
        GameMasterBrain GameMaster { get; }
        ContractBook Contracts { get; }
        RoundInsight Insight { get; }
        int Stage { get; }
        IRandomSource Random { get; }
        int CurrentRound { get; }
        long StartingDebt { get; }

        /// <summary>The Game Master speaks (dialogue system picks the line).</summary>
        void Say(DialogueTrigger trigger, string key = null, IDictionary<string, string> values = null);

        /// <summary>A short notice for the HUD.</summary>
        void Notify(string text);

        /// <summary>Activates a rule from the catalog. <paramref name="free"/> skips the Authority cost (curses, events).</summary>
        bool ActivateRule(string ruleId, bool free, int parameter = -1);

        /// <summary>Raises the debt unless a Debt Freeze is active. Returns the amount actually added.</summary>
        long IncreaseDebt(long amount, string reason);

        /// <summary>Adds (or removes) seconds from the next betting window.</summary>
        void AdjustNextBettingTime(float seconds);

        /// <summary>Permanently raises the run's base minimum bet.</summary>
        void RaiseBaseMinimumBet(long amount);

        /// <summary>Starts a random event now (mystery pockets).</summary>
        void TriggerRandomEvent();

        /// <summary>Records that the player slipped through a rule's loophole.</summary>
        void RecordLoophole(string sourceId, string description);

        /// <summary>
        /// Permanently changes the base wheel (bomb pockets, the final encounter). The table follows as
        /// soon as the round allows it.
        /// </summary>
        bool ChangeBaseWheel(Func<WheelConfiguration, WheelChange> transform);

        /// <summary>What the Game Master has planned for the next round, or null (Rule Peek).</summary>
        string PeekNextIntervention();

        /// <summary>Cancels the intervention announced this round, or the planned one. Returns its name, or null.</summary>
        string CancelIntervention();

        /// <summary>Asks for a live wheel transformation during the next betting window (challenges).</summary>
        void ScheduleLiveTransformation();

        /// <summary>Whether this bet was placed after a live transformation began this round.</summary>
        bool PlacedDuringTransformation(BetSelection selection);
    }
}

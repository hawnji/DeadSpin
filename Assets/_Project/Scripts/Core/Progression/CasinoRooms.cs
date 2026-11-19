using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Progression
{
    /// <summary>The rooms of the underground casino. The player stays seated; the other rooms open as panels.</summary>
    public enum RoomId
    {
        RouletteRoom,
        PrivateLounge,
        Shop,
        Vault,
        RecordsRoom,
        Office
    }

    /// <summary>A document in the Records Room.</summary>
    public sealed class RecordEntry
    {
        public string Id { get; }
        public string Title { get; }
        public string Text { get; }
        public int MinStage { get; }
        /// <summary>Secrets give Influence the first time they are read and count as evidence for The Loophole.</summary>
        public bool IsSecret { get; }
        /// <summary>Needs this many loopholes found before it can be read (0: none).</summary>
        public int LoopholesRequired { get; }

        public RecordEntry(string id, string title, string text, int minStage, bool isSecret = false, int loopholesRequired = 0)
        {
            Id = id;
            Title = title;
            Text = text;
            MinStage = minStage;
            IsSecret = isSecret;
            LoopholesRequired = loopholesRequired;
        }
    }

    /// <summary>
    /// The Records Room, the Lounge and the Office: the casino's history, its secrets, its staff and
    /// its owner. Environmental storytelling lives here as data.
    /// </summary>
    public sealed class CasinoRecords
    {
        private readonly List<RecordEntry> _entries = new List<RecordEntry>();
        private readonly HashSet<string> _read = new HashSet<string>();
        private readonly HashSet<int> _restedStages = new HashSet<int>();

        public IReadOnlyList<RecordEntry> Entries => _entries;
        public IEnumerable<string> ReadIds => _read;
        public IEnumerable<int> RestedStages => _restedStages;
        public int SecretsFound { get; private set; }

        public static CasinoRecords CreateDefault()
        {
            var r = new CasinoRecords();
            r._entries.Add(new RecordEntry("ledger_1987", "Guest Ledger, 1987", "Forty-one guests. Forty-one debts. One line at the bottom in a different hand: \"Paid in full: M. Okafor.\" The page after it is torn out.", 1));
            r._entries.Add(new RecordEntry("house_rules", "The House Rules (original)", "Rule 1: The house sets the wheel. Rule 2: The house sets the price. Rule 3: The house may not make a win impossible. Someone has underlined rule 3 twice.", 1, isSecret: true));
            r._entries.Add(new RecordEntry("staff_memo", "Staff Memo", "Do not speak to the guest at table one. Do not look at the wheel when he changes it. Do not ask where the last croupier went.", 1));
            r._entries.Add(new RecordEntry("wheel_patent", "Patent Drawing", "A wheel that grows. Pockets that split like cells. The margin notes insist the odds must stay \"honest to the pocket\", or the whole thing collapses.", 2, isSecret: true));
            r._entries.Add(new RecordEntry("names_wall", "Photograph: the East Wall", "Names scratched into the plaster, hundreds of them. Some are crossed out. Some are circled. Yours is already there, very faint.", 2));
            r._entries.Add(new RecordEntry("tax_clause", "Tax Ordinance, Clause 7", "A house tax applies to the colours as printed on the layout. Combination wagers are not printed on the layout.", 2, isSecret: true, loopholesRequired: 0));
            r._entries.Add(new RecordEntry("contract_draft", "Draft Contract", "\"The guest's debt shall not increase while frozen.\" The word \"frozen\" is circled. Someone wrote: \"he never defines how long.\"", 3, isSecret: true));
            r._entries.Add(new RecordEntry("previous_owner", "Letter from the Previous Owner", "If you are reading this, he let you in here on purpose. He wants you to find the loopholes. He wants to see if you will use them.", 3, isSecret: true, loopholesRequired: 1));
            r._entries.Add(new RecordEntry("final_table", "Seating Plan: The Final Table", "One chair for the guest. One for him. A third chair, pushed against the wall, labelled \"next owner\".", 4));
            r._entries.Add(new RecordEntry("the_deed", "The Deed to the Casino", "The casino passes to any guest who clears their debt at the final table with the house's money still in hand.", 4, isSecret: true, loopholesRequired: 2));
            return r;
        }

        public bool CanRead(RecordEntry entry, int stage, int loopholes)
        {
            return stage >= entry.MinStage && loopholes >= entry.LoopholesRequired;
        }

        public bool HasRead(string id) { return _read.Contains(id); }

        /// <summary>Reads an entry. Returns true when it is a secret read for the first time.</summary>
        public bool Read(RecordEntry entry)
        {
            if (!_read.Add(entry.Id)) return false;
            if (!entry.IsSecret) return false;
            SecretsFound++;
            return true;
        }

        public void Restore(IEnumerable<string> read, IEnumerable<int> rested)
        {
            _read.Clear();
            _restedStages.Clear();
            SecretsFound = 0;
            if (read != null)
            {
                foreach (string id in read)
                {
                    if (!_read.Add(id)) continue;
                    RecordEntry e = Get(id);
                    if (e != null && e.IsSecret) SecretsFound++;
                }
            }
            if (rested != null) foreach (int s in rested) _restedStages.Add(s);
        }

        public RecordEntry Get(string id)
        {
            foreach (RecordEntry e in _entries) if (e.Id == id) return e;
            return null;
        }

        /// <summary>The Lounge: one rest per stage.</summary>
        public bool CanRest(int stage) { return !_restedStages.Contains(stage); }

        public bool Rest(int stage) { return _restedStages.Add(stage); }

        /// <summary>What the staff say in the Lounge, by stage (they grow more uncomfortable).</summary>
        public static string StaffLine(int stage, int variant)
        {
            string[][] lines =
            {
                new[] { "\"Drink? On the house. Everything here is on the house, in the end.\"", "The bartender polishes a glass that is already clean." },
                new[] { "\"He changed the wheel again last night. Nobody touched it.\"", "A waiter won't meet your eyes. His name tag has been scratched out." },
                new[] { "\"Pay what he asks. On time. Please.\"", "The bartender pours two glasses and drinks one himself, quickly." },
                new[] { "\"I was a guest once,\" the croupier whispers. \"Like you.\"", "The staff stand very still whenever the lights flicker." },
                new[] { "The lounge is empty. Every glass has been turned upside down.", "A single place is set at the bar, with your name on the card." }
            };
            string[] set = lines[Math.Max(0, Math.Min(4, stage - 1))];
            return set[Math.Abs(variant) % set.Length];
        }

        /// <summary>What happens in the Game Master's Office at each stage (story progression).</summary>
        public static string OfficeScene(int stage)
        {
            switch (stage)
            {
                case 1: return "A desk, a lamp, and a framed photograph of a wheel with no numbers. The chair behind the desk is warm.";
                case 2: return "Blueprints of the wheel cover the desk. Several show pockets you have never seen. One is labelled with your favourite number, crossed out.";
                case 3: return "Your contract lies open on the desk. Someone has written the payment dates in red. The lamp flickers when you touch it.";
                case 4: return "The office is bigger than the room it is in. The photograph on the wall is of you, sitting at table one.";
                default: return "The office is empty. His chair has been carried out to the final table.";
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Betting
{
    /// <summary>The wagers on the table this round, one entry per spot (chips on the same spot stack).</summary>
    public sealed class BetBook
    {
        private readonly Dictionary<string, long> _amounts = new Dictionary<string, long>();
        private readonly Dictionary<string, BetSelection> _selections = new Dictionary<string, BetSelection>();
        private readonly List<string> _order = new List<string>();

        public long Total { get; private set; }
        public int Count => _order.Count;

        public long AmountOn(BetSelection selection)
        {
            long amount;
            return selection != null && _amounts.TryGetValue(selection.Key, out amount) ? amount : 0;
        }

        /// <summary>Bets in the order they were first placed.</summary>
        public IEnumerable<KeyValuePair<BetSelection, long>> Entries
        {
            get
            {
                foreach (string key in _order) yield return new KeyValuePair<BetSelection, long>(_selections[key], _amounts[key]);
            }
        }

        public void Add(BetSelection selection, long amount)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            long existing;
            if (_amounts.TryGetValue(selection.Key, out existing))
            {
                _amounts[selection.Key] = existing + amount;
            }
            else
            {
                _amounts[selection.Key] = amount;
                _selections[selection.Key] = selection;
                _order.Add(selection.Key);
            }
            Total += amount;
        }

        /// <summary>Removes up to <paramref name="amount"/> from a spot; returns what was removed.</summary>
        public long Remove(BetSelection selection, long amount)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            long existing;
            if (amount <= 0 || !_amounts.TryGetValue(selection.Key, out existing)) return 0;
            long removed = Math.Min(existing, amount);
            if (removed == existing)
            {
                _amounts.Remove(selection.Key);
                _selections.Remove(selection.Key);
                _order.Remove(selection.Key);
            }
            else
            {
                _amounts[selection.Key] = existing - removed;
            }
            Total -= removed;
            return removed;
        }

        public void Clear()
        {
            _amounts.Clear();
            _selections.Clear();
            _order.Clear();
            Total = 0;
        }
    }
}

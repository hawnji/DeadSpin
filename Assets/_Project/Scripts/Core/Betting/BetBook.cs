using System;
using System.Collections.Generic;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.Betting
{
    /// <summary>One stack of chips on one spot.</summary>
    public sealed class Wager
    {
        public BetSelection Selection { get; }
        public long Amount { get; internal set; }
        public ChipKind Kind { get; internal set; }

        /// <summary>Locked chips cannot be moved by table manipulation (Chip Anchor, protected chips).</summary>
        public bool Locked { get; internal set; }

        /// <summary>Percent of the stake refunded if this bet loses (Bet Insurance).</summary>
        public int InsuredPercent { get; internal set; }

        internal Wager(BetSelection selection, long amount, ChipKind kind)
        {
            Selection = selection;
            Amount = amount;
            Kind = kind;
        }

        public bool IsImmovable => Locked || Kind == ChipKind.Sticky || Kind == ChipKind.Protected;
    }

    /// <summary>The wagers on the table this round, one entry per spot (chips on the same spot stack).</summary>
    public sealed class BetBook
    {
        private readonly Dictionary<string, Wager> _wagers = new Dictionary<string, Wager>();
        private readonly List<string> _order = new List<string>();

        public long Total { get; private set; }
        public int Count => _order.Count;

        public long AmountOn(BetSelection selection)
        {
            Wager wager;
            return selection != null && _wagers.TryGetValue(selection.Key, out wager) ? wager.Amount : 0;
        }

        public Wager Get(BetSelection selection)
        {
            Wager wager;
            return selection != null && _wagers.TryGetValue(selection.Key, out wager) ? wager : null;
        }

        /// <summary>Bets in the order they were first placed.</summary>
        public IEnumerable<KeyValuePair<BetSelection, long>> Entries
        {
            get
            {
                foreach (string key in _order) yield return new KeyValuePair<BetSelection, long>(_wagers[key].Selection, _wagers[key].Amount);
            }
        }

        /// <summary>Snapshot of the wagers in placement order (safe to modify the book while iterating it).</summary>
        public List<Wager> Wagers
        {
            get
            {
                var list = new List<Wager>(_order.Count);
                foreach (string key in _order) list.Add(_wagers[key]);
                return list;
            }
        }

        public Wager Add(BetSelection selection, long amount, ChipKind kind = ChipKind.Standard)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Wager wager;
            if (_wagers.TryGetValue(selection.Key, out wager))
            {
                wager.Amount += amount;
                wager.Kind = ChipProperties.Combine(wager.Kind, kind);
            }
            else
            {
                wager = new Wager(selection, amount, kind);
                _wagers[selection.Key] = wager;
                _order.Add(selection.Key);
            }
            Total += amount;
            return wager;
        }

        /// <summary>Removes up to <paramref name="amount"/> from a spot; returns what was removed.</summary>
        public long Remove(BetSelection selection, long amount)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            Wager wager;
            if (amount <= 0 || !_wagers.TryGetValue(selection.Key, out wager)) return 0;
            long removed = Math.Min(wager.Amount, amount);
            if (removed == wager.Amount)
            {
                _wagers.Remove(selection.Key);
                _order.Remove(selection.Key);
            }
            else
            {
                wager.Amount -= removed;
            }
            Total -= removed;
            return removed;
        }

        public void SetLocked(BetSelection selection, bool locked)
        {
            Wager wager = Get(selection);
            if (wager != null) wager.Locked = locked;
        }

        public void SetInsured(BetSelection selection, int percent)
        {
            Wager wager = Get(selection);
            if (wager != null) wager.InsuredPercent = Math.Max(0, Math.Min(100, percent));
        }

        public void Clear()
        {
            _wagers.Clear();
            _order.Clear();
            Total = 0;
        }
    }
}

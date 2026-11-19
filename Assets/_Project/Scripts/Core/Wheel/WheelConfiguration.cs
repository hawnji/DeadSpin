using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// The single source of truth for the wheel: the ordered ring of pockets (index 0 sits at the
    /// rotor's zero angle, indices increase clockwise seen from above). The visual wheel is always
    /// rebuilt from this object and never edited directly. Immutable; validate before use.
    /// </summary>
    public sealed class WheelConfiguration
    {
        private readonly Pocket[] _pockets;

        public string Name { get; }

        public IReadOnlyList<Pocket> Pockets => _pockets;

        public int PocketCount => _pockets.Length;

        /// <summary>Highest number printed on the wheel; the betting layout covers 1..this.</summary>
        public int HighestNumber { get; }

        /// <summary>Sum of effective weights. The denominator of every probability.</summary>
        public long TotalWeight { get; }

        public WheelConfiguration(string name, IEnumerable<Pocket> pockets)
        {
            if (pockets == null) throw new ArgumentNullException(nameof(pockets));
            Name = string.IsNullOrEmpty(name) ? "Custom" : name;
            _pockets = new List<Pocket>(pockets).ToArray();

            int highest = 0;
            long total = 0;
            foreach (Pocket pocket in _pockets)
            {
                if (pocket == null) throw new ArgumentException("A wheel cannot contain null pockets.", nameof(pockets));
                if (pocket.Number > highest) highest = pocket.Number;
                total += pocket.EffectiveWeight;
            }
            HighestNumber = highest;
            TotalWeight = total;
        }

        /// <summary>Every problem with this configuration; empty means it can be used for a spin.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (_pockets.Length < 2) errors.Add("A wheel needs at least two pockets.");
            if (TotalWeight <= 0) errors.Add("No active pocket has any weight, so no spin can resolve.");

            var ids = new HashSet<int>();
            foreach (Pocket pocket in _pockets)
            {
                if (!ids.Add(pocket.Id)) errors.Add("Duplicate pocket id " + pocket.Id + ".");
                if (pocket.Number == 0 && pocket.Color != PocketColor.Green) errors.Add("Zero pockets must be green (pocket " + pocket.Id + ").");
            }

            bool anyWinnable = false;
            foreach (Pocket pocket in _pockets)
            {
                if (pocket.EffectiveWeight > 0 && !pocket.IsZero) { anyWinnable = true; break; }
            }
            if (!anyWinnable) errors.Add("Only zero pockets can win, so the player has no winnable bet.");
            return errors;
        }

        public bool IsValid => Validate().Count == 0;

        public void ThrowIfInvalid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0) throw new InvalidOperationException("Invalid wheel '" + Name + "': " + string.Join(" ", errors));
        }

        /// <summary>Index of the pocket with this id, or -1.</summary>
        public int IndexOfId(int id)
        {
            for (int i = 0; i < _pockets.Length; i++) if (_pockets[i].Id == id) return i;
            return -1;
        }
    }
}

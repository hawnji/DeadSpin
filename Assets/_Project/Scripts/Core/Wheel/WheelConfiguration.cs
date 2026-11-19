using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// The single source of truth for the wheel: the ordered ring of pockets (index 0 sits at the
    /// rotor's zero angle, indices increase clockwise seen from above) and an optional outer bonus
    /// ring. The visual wheel is always rebuilt from this object and never edited directly.
    /// Immutable; validate before use.
    /// </summary>
    public sealed class WheelConfiguration
    {
        public const int MinPockets = 2;
        public const int MaxPockets = 128;

        /// <summary>The betting layout cannot show numbers above this.</summary>
        public const int MaxNumber = 120;

        private readonly Pocket[] _pockets;

        public string Name { get; }

        public IReadOnlyList<Pocket> Pockets => _pockets;

        public int PocketCount => _pockets.Length;

        /// <summary>Highest number printed on the wheel; the betting layout covers 1..this.</summary>
        public int HighestNumber { get; }

        /// <summary>Sum of effective weights. The denominator of every probability.</summary>
        public long TotalWeight { get; }

        /// <summary>Outer bonus ring, or null when only the pocket ring is active.</summary>
        public WheelRing Ring { get; }

        public WheelConfiguration(string name, IEnumerable<Pocket> pockets, WheelRing ring = null)
        {
            if (pockets == null) throw new ArgumentNullException(nameof(pockets));
            Name = string.IsNullOrEmpty(name) ? "Custom" : name;
            _pockets = new List<Pocket>(pockets).ToArray();
            Ring = ring;

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

        /// <summary>Same ring of pockets under a new name or with different pockets/ring.</summary>
        public WheelConfiguration With(IEnumerable<Pocket> pockets, string name = null)
        {
            return new WheelConfiguration(name ?? Name, pockets, Ring);
        }

        public WheelConfiguration WithRing(WheelRing ring)
        {
            return new WheelConfiguration(Name, _pockets, ring);
        }

        /// <summary>Every problem with this configuration; empty means it can be used for a spin.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (_pockets.Length < MinPockets) errors.Add("A wheel needs at least " + MinPockets + " pockets.");
            if (_pockets.Length > MaxPockets) errors.Add("A wheel can hold at most " + MaxPockets + " pockets.");
            if (HighestNumber > MaxNumber) errors.Add("Numbers above " + MaxNumber + " cannot fit on the betting layout.");
            if (TotalWeight <= 0) errors.Add("No active pocket has any weight, so no spin can resolve.");
            if (TotalWeight > 1000000) errors.Add("Pocket weights are too large.");

            var ids = new HashSet<int>();
            foreach (Pocket pocket in _pockets)
            {
                if (!ids.Add(pocket.Id)) errors.Add("Duplicate pocket id " + pocket.Id + ".");
                if (pocket.Number == 0 && pocket.Color != PocketColor.Green) errors.Add("Zero pockets must be green (pocket " + pocket.Id + ").");
                if (pocket.Number != 0 && pocket.Color == PocketColor.Green) errors.Add("Only zero pockets may be green (pocket " + pocket.Id + ").");
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

        /// <summary>Indices of every pocket showing this number.</summary>
        public List<int> IndicesOfNumber(int number)
        {
            var result = new List<int>();
            for (int i = 0; i < _pockets.Length; i++) if (_pockets[i].Number == number) result.Add(i);
            return result;
        }

        /// <summary>Number of pockets showing this number.</summary>
        public int CountOf(int number)
        {
            int count = 0;
            foreach (Pocket p in _pockets) if (p.Number == number) count++;
            return count;
        }

        /// <summary>A fresh id not used by any pocket.</summary>
        public int NextId()
        {
            int max = -1;
            foreach (Pocket p in _pockets) if (p.Id > max) max = p.Id;
            return max + 1;
        }

        /// <summary>Colour of the first pocket showing this number, used by the betting layout. Green if absent.</summary>
        public PocketColor ColorOf(int number)
        {
            foreach (Pocket p in _pockets) if (p.Number == number) return p.Color;
            return number == 0 ? PocketColor.Green : PocketColor.Black;
        }

        public bool Contains(int number)
        {
            foreach (Pocket p in _pockets) if (p.Number == number) return true;
            return false;
        }

        public bool HasSpecial(SpecialPocketType type)
        {
            foreach (Pocket p in _pockets) if (p.Special == type && p.EffectiveWeight > 0) return true;
            return false;
        }
    }
}

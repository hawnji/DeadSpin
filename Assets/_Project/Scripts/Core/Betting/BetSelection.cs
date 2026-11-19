using System;
using System.Linq;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// What a bet covers. Inside bets and numbered special bets list their numbers; outside bets are
    /// rules on the pocket (colour, parity, range, dozen, column, combination, jackpot) evaluated
    /// against the layout's highest number, so they keep working when the wheel changes. Value type
    /// semantics: two selections covering the same thing are equal, which lets chips on the same
    /// spot stack into one bet.
    /// </summary>
    public sealed class BetSelection : IEquatable<BetSelection>
    {
        private static readonly int[] NoNumbers = new int[0];

        public BetType Type { get; }

        /// <summary>Covered numbers for numbered bets, sorted ascending. Empty for rule-based bets.</summary>
        public int[] Numbers { get; }

        /// <summary>0-2 for dozens and columns, 0-3 for combinations; 0 otherwise.</summary>
        public int Group { get; }

        /// <summary>The number a special bet was built around (neighbour centre, previous result), or -1.</summary>
        public int Anchor { get; }

        /// <summary>Stable key, e.g. "Split:17-20" or "Dozen:2".</summary>
        public string Key { get; }

        private BetSelection(BetType type, int[] numbers, int group, int anchor)
        {
            Type = type;
            Numbers = numbers;
            Group = group;
            Anchor = anchor;
            Key = BuildKey(type, numbers, group);
        }

        public bool IsInside => Type == BetType.Straight || Type == BetType.Split || Type == BetType.Street || Type == BetType.Corner || Type == BetType.SixLine;

        public bool IsSpecial => Type >= BetType.Neighbor;

        public bool IsOutside => !IsInside && !IsSpecial;

        /// <summary>Covers an explicit list of numbers (inside bets and numbered specials).</summary>
        public bool IsNumbered => Numbers.Length > 0;

        /// <summary>Mystery bets keep their numbers hidden from the player until the spin resolves.</summary>
        public bool IsHidden => Type == BetType.Mystery;

        public static BetSelection Inside(BetType type, params int[] numbers)
        {
            if (numbers == null || numbers.Length == 0) throw new ArgumentException("Inside bets need numbers.", nameof(numbers));
            int expected = ExpectedCount(type);
            if (expected == 0) throw new ArgumentException(type + " is not an inside bet.", nameof(type));
            int[] sorted = numbers.Distinct().OrderBy(n => n).ToArray();
            if (sorted.Length != expected) throw new ArgumentException(type + " covers exactly " + expected + " distinct numbers.", nameof(numbers));
            if (sorted[0] < 0) throw new ArgumentException("Numbers cannot be negative.", nameof(numbers));
            return new BetSelection(type, sorted, 0, -1);
        }

        public static BetSelection Outside(BetType type, int group = 0)
        {
            bool traditionalOutside = type >= BetType.Red && type <= BetType.Column;
            if (!traditionalOutside) throw new ArgumentException(type + " is not an outside bet.", nameof(type));
            bool grouped = type == BetType.Dozen || type == BetType.Column;
            if (grouped && (group < 0 || group > 2)) throw new ArgumentOutOfRangeException(nameof(group), "Dozens and columns are numbered 0-2.");
            if (!grouped && group != 0) throw new ArgumentOutOfRangeException(nameof(group), type + " has no group.");
            return new BetSelection(type, NoNumbers, group, -1);
        }

        public static BetSelection Straight(int number) { return Inside(BetType.Straight, number); }

        /// <summary>A numbered special bet (neighbour, sequence, repeat, survivor, mystery) covering the given numbers.</summary>
        public static BetSelection Special(BetType type, int anchor, params int[] numbers)
        {
            if (type != BetType.Neighbor && type != BetType.Sequence && type != BetType.Repeat && type != BetType.Survivor && type != BetType.Mystery)
                throw new ArgumentException(type + " is not a numbered special bet.", nameof(type));
            if (numbers == null || numbers.Length == 0) throw new ArgumentException("Special bets need at least one number.", nameof(numbers));
            int[] sorted = numbers.Distinct().OrderBy(n => n).ToArray();
            if (sorted[0] < 0) throw new ArgumentException("Numbers cannot be negative.", nameof(numbers));
            return new BetSelection(type, sorted, 0, anchor);
        }

        /// <summary>Colour and parity together: 0 red-odd, 1 red-even, 2 black-odd, 3 black-even.</summary>
        public static BetSelection Combination(int group)
        {
            if (group < 0 || group > 3) throw new ArgumentOutOfRangeException(nameof(group));
            return new BetSelection(BetType.Combination, NoNumbers, group, -1);
        }

        public static BetSelection Jackpot()
        {
            return new BetSelection(BetType.Jackpot, NoNumbers, 0, -1);
        }

        /// <summary>Whether a pocket wins this bet. <paramref name="highestNumber"/> is the layout's top number (36 on a standard table).</summary>
        public bool Covers(Pocket pocket, int highestNumber)
        {
            if (pocket == null) throw new ArgumentNullException(nameof(pocket));
            int n = pocket.Number;
            if (IsNumbered) return Array.BinarySearch(Numbers, n) >= 0;
            if (Type == BetType.Jackpot) return pocket.Special == SpecialPocketType.Jackpot;

            // Zero and anything off the layout lose every rule-based bet.
            if (n <= 0 || n > highestNumber) return false;
            int half = highestNumber / 2;
            int third = highestNumber / 3;
            switch (Type)
            {
                case BetType.Red: return pocket.Color == PocketColor.Red;
                case BetType.Black: return pocket.Color == PocketColor.Black;
                case BetType.Odd: return n % 2 == 1;
                case BetType.Even: return n % 2 == 0;
                case BetType.Low: return n <= half;
                case BetType.High: return n > half;
                case BetType.Dozen: return third > 0 && Math.Min((n - 1) / third, 2) == Group;
                case BetType.Column: return (n - 1) % 3 == Group;
                case BetType.Combination:
                    PocketColor color = Group < 2 ? PocketColor.Red : PocketColor.Black;
                    bool odd = Group % 2 == 0;
                    return pocket.Color == color && (n % 2 == 1) == odd;
                default: return false;
            }
        }

        public static int ExpectedCount(BetType type)
        {
            switch (type)
            {
                case BetType.Straight: return 1;
                case BetType.Split: return 2;
                case BetType.Street: return 3;
                case BetType.Corner: return 4;
                case BetType.SixLine: return 6;
                default: return 0;
            }
        }

        /// <summary>Readable label for the HUD, e.g. "Split 17/20" or "2nd dozen". Mystery bets never reveal their numbers here.</summary>
        public string DisplayName
        {
            get
            {
                switch (Type)
                {
                    case BetType.Straight: return "Straight " + Numbers[0];
                    case BetType.Dozen: return Ordinal(Group + 1) + " dozen";
                    case BetType.Column: return Ordinal(Group + 1) + " column";
                    case BetType.Low: return "Low half";
                    case BetType.High: return "High half";
                    case BetType.Neighbor: return "Neighbours of " + Anchor;
                    case BetType.Sequence: return "Sequence from " + Anchor;
                    case BetType.Repeat: return "Repeat " + Anchor;
                    case BetType.Survivor: return "Survivors (" + Numbers.Length + " numbers)";
                    case BetType.Mystery: return "Mystery (" + Numbers.Length + " hidden numbers)";
                    case BetType.Combination: return CombinationName(Group);
                    case BetType.Jackpot: return "Jackpot";
                    default:
                        return IsInside ? Type + " " + string.Join("/", Numbers) : Type.ToString();
                }
            }
        }

        public static string CombinationName(int group)
        {
            switch (group)
            {
                case 0: return "Red + Odd";
                case 1: return "Red + Even";
                case 2: return "Black + Odd";
                default: return "Black + Even";
            }
        }

        private static string Ordinal(int n)
        {
            return n == 1 ? "1st" : n == 2 ? "2nd" : "3rd";
        }

        private static string BuildKey(BetType type, int[] numbers, int group)
        {
            if (numbers.Length > 0) return type + ":" + string.Join("-", numbers);
            return type == BetType.Dozen || type == BetType.Column || type == BetType.Combination ? type + ":" + group : type.ToString();
        }

        public bool Equals(BetSelection other) { return other != null && other.Key == Key; }
        public override bool Equals(object obj) { return Equals(obj as BetSelection); }
        public override int GetHashCode() { return Key.GetHashCode(); }
        public override string ToString() { return Key; }
    }
}

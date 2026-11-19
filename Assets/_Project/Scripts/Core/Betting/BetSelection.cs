using System;
using System.Linq;
using DeadSpin.Core.Wheel;

namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// What a bet covers. Inside bets list their numbers; outside bets are rules on the pocket
    /// (colour, parity, range, dozen, column) evaluated against the layout's highest number, so they
    /// keep working when the Game Master changes the wheel. Value type semantics: two selections
    /// covering the same thing are equal, which lets chips on the same spot stack into one bet.
    /// </summary>
    public sealed class BetSelection : IEquatable<BetSelection>
    {
        private static readonly int[] NoNumbers = new int[0];

        public BetType Type { get; }

        /// <summary>Covered numbers for inside bets, sorted ascending. Empty for outside bets.</summary>
        public int[] Numbers { get; }

        /// <summary>0, 1 or 2 for dozens and columns; 0 otherwise.</summary>
        public int Group { get; }

        /// <summary>Stable key, e.g. "Split:17-20" or "Dozen:2".</summary>
        public string Key { get; }

        private BetSelection(BetType type, int[] numbers, int group)
        {
            Type = type;
            Numbers = numbers;
            Group = group;
            Key = BuildKey(type, numbers, group);
        }

        public bool IsInside => Type == BetType.Straight || Type == BetType.Split || Type == BetType.Street || Type == BetType.Corner || Type == BetType.SixLine;

        public static BetSelection Inside(BetType type, params int[] numbers)
        {
            if (numbers == null || numbers.Length == 0) throw new ArgumentException("Inside bets need numbers.", nameof(numbers));
            int expected = ExpectedCount(type);
            if (expected == 0) throw new ArgumentException(type + " is not an inside bet.", nameof(type));
            int[] sorted = numbers.Distinct().OrderBy(n => n).ToArray();
            if (sorted.Length != expected) throw new ArgumentException(type + " covers exactly " + expected + " distinct numbers.", nameof(numbers));
            if (sorted[0] < 0) throw new ArgumentException("Numbers cannot be negative.", nameof(numbers));
            return new BetSelection(type, sorted, 0);
        }

        public static BetSelection Outside(BetType type, int group = 0)
        {
            if (ExpectedCount(type) != 0) throw new ArgumentException(type + " is an inside bet.", nameof(type));
            bool grouped = type == BetType.Dozen || type == BetType.Column;
            if (grouped && (group < 0 || group > 2)) throw new ArgumentOutOfRangeException(nameof(group), "Dozens and columns are numbered 0-2.");
            if (!grouped && group != 0) throw new ArgumentOutOfRangeException(nameof(group), type + " has no group.");
            return new BetSelection(type, NoNumbers, group);
        }

        public static BetSelection Straight(int number) { return Inside(BetType.Straight, number); }

        /// <summary>Whether a pocket wins this bet. <paramref name="highestNumber"/> is the layout's top number (36 on a standard table).</summary>
        public bool Covers(Pocket pocket, int highestNumber)
        {
            if (pocket == null) throw new ArgumentNullException(nameof(pocket));
            int n = pocket.Number;
            if (IsInside) return Array.BinarySearch(Numbers, n) >= 0;

            // Zero and anything off the layout lose every outside bet.
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

        /// <summary>Readable label for the HUD, e.g. "Split 17/20" or "2nd dozen".</summary>
        public string DisplayName
        {
            get
            {
                switch (Type)
                {
                    case BetType.Straight: return "Straight " + Numbers[0];
                    case BetType.Dozen: return Ordinal(Group + 1) + " dozen";
                    case BetType.Column: return Ordinal(Group + 1) + " column";
                    case BetType.Low: return "Low (1-18)";
                    case BetType.High: return "High (19-36)";
                    default:
                        return IsInside ? Type + " " + string.Join("/", Numbers) : Type.ToString();
                }
            }
        }

        private static string Ordinal(int n)
        {
            return n == 1 ? "1st" : n == 2 ? "2nd" : "3rd";
        }

        private static string BuildKey(BetType type, int[] numbers, int group)
        {
            if (numbers.Length > 0) return type + ":" + string.Join("-", numbers);
            return type == BetType.Dozen || type == BetType.Column ? type + ":" + group : type.ToString();
        }

        public bool Equals(BetSelection other) { return other != null && other.Key == Key; }
        public override bool Equals(object obj) { return Equals(obj as BetSelection); }
        public override int GetHashCode() { return Key.GetHashCode(); }
        public override string ToString() { return Key; }
    }
}

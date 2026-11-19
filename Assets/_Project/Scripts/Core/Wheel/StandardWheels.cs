using System.Collections.Generic;

namespace DeadSpin.Core.Wheel
{
    /// <summary>Factory for traditional wheel layouts.</summary>
    public static class StandardWheels
    {
        /// <summary>Clockwise pocket order of a single-zero (European) wheel, starting at zero.</summary>
        public static readonly int[] EuropeanOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
            5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
        };

        private static readonly HashSet<int> RedNumbers = new HashSet<int>
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
        };

        /// <summary>Traditional colour of a number on a 1-36 layout.</summary>
        public static PocketColor TraditionalColor(int number)
        {
            if (number == 0) return PocketColor.Green;
            return RedNumbers.Contains(number) ? PocketColor.Red : PocketColor.Black;
        }

        /// <summary>The classic 37-pocket single-zero wheel. Pocket ids equal their numbers.</summary>
        public static WheelConfiguration European()
        {
            var pockets = new List<Pocket>(EuropeanOrder.Length);
            foreach (int number in EuropeanOrder)
            {
                pockets.Add(new Pocket(number, number, TraditionalColor(number)));
            }
            return new WheelConfiguration("European (37)", pockets);
        }
    }
}

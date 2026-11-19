using System.Collections.Generic;

namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// The traditional single-zero betting layout: 0, numbers 1-36 in twelve columns of three, column
    /// bets, dozens and the six even-money boxes, plus every legal split, street, corner and six-line.
    /// Numbers run 1 (near row) / 2 / 3 (far row) in the first column, then 4/5/6 and so on.
    /// </summary>
    public sealed class StandardLayout
    {
        public const int Columns = 12;
        public const int Rows = 3;
        public const int HighestNumber = 36;
        public const float BorderSpotSize = 0.32f;

        private readonly List<BetSpot> _spots = new List<BetSpot>();
        private readonly Dictionary<string, BetSpot> _byKey = new Dictionary<string, BetSpot>();

        public IReadOnlyList<BetSpot> Spots => _spots;

        public const float MinX = 0f;
        public const float MaxX = 14f;
        public const float MinY = -2f;
        public const float MaxY = 3f;

        public StandardLayout()
        {
            BuildBoxes();
            BuildBorderSpots();
        }

        public static int NumberAt(int column, int row) { return column * Rows + row + 1; }
        public static int ColumnOf(int number) { return (number - 1) / Rows; }
        public static int RowOf(int number) { return (number - 1) % Rows; }

        public bool TryGetSpot(BetSelection selection, out BetSpot spot)
        {
            return _byKey.TryGetValue(selection.Key, out spot);
        }

        /// <summary>True when the selection is a legal bet on this layout (e.g. a split of touching numbers).</summary>
        public bool IsLegal(BetSelection selection)
        {
            return selection != null && _byKey.ContainsKey(selection.Key);
        }

        private void BuildBoxes()
        {
            Add(BetSelection.Straight(0), 0.5f, 1.5f, 1f, 3f, "0");
            for (int c = 0; c < Columns; c++)
            {
                for (int r = 0; r < Rows; r++)
                {
                    int n = NumberAt(c, r);
                    Add(BetSelection.Straight(n), c + 1.5f, r + 0.5f, 1f, 1f, n.ToString());
                }
            }
            for (int r = 0; r < Rows; r++)
            {
                Add(BetSelection.Outside(BetType.Column, r), 13.5f, r + 0.5f, 1f, 1f, "2 to 1");
            }
            string[] dozenLabels = { "1st 12", "2nd 12", "3rd 12" };
            for (int d = 0; d < 3; d++)
            {
                Add(BetSelection.Outside(BetType.Dozen, d), 1f + d * 4f + 2f, -0.5f, 4f, 1f, dozenLabels[d]);
            }
            BetType[] evenMoney = { BetType.Low, BetType.Even, BetType.Red, BetType.Black, BetType.Odd, BetType.High };
            string[] evenLabels = { "1 to 18", "EVEN", "RED", "BLACK", "ODD", "19 to 36" };
            for (int i = 0; i < evenMoney.Length; i++)
            {
                Add(BetSelection.Outside(evenMoney[i]), 1f + i * 2f + 1f, -1.5f, 2f, 1f, evenLabels[i]);
            }
        }

        private void BuildBorderSpots()
        {
            float s = BorderSpotSize;
            for (int c = 0; c < Columns; c++)
            {
                for (int r = 0; r < Rows; r++)
                {
                    int n = NumberAt(c, r);
                    // Split with the number in the next column (shared vertical border).
                    if (c + 1 < Columns)
                    {
                        Add(BetSelection.Inside(BetType.Split, n, NumberAt(c + 1, r)), c + 2f, r + 0.5f, s, 1f - s, null);
                    }
                    // Split with the number in the next row (shared horizontal border).
                    if (r + 1 < Rows)
                    {
                        Add(BetSelection.Inside(BetType.Split, n, n + 1), c + 1.5f, r + 1f, 1f - s, s, null);
                    }
                    // Corner where four numbers meet.
                    if (c + 1 < Columns && r + 1 < Rows)
                    {
                        Add(BetSelection.Inside(BetType.Corner, n, n + 1, NumberAt(c + 1, r), NumberAt(c + 1, r + 1)), c + 2f, r + 1f, s, s, null);
                    }
                }

                // Street: the whole column of three, placed on its near edge.
                int first = NumberAt(c, 0);
                Add(BetSelection.Inside(BetType.Street, first, first + 1, first + 2), c + 1.5f, 0f, 1f - s, s, null);

                // Six line: two adjacent streets, placed where their near edges meet.
                if (c + 1 < Columns)
                {
                    Add(BetSelection.Inside(BetType.SixLine, first, first + 1, first + 2, first + 3, first + 4, first + 5), c + 2f, 0f, s, s, null);
                }
            }

            // Splits between zero and 1, 2, 3.
            for (int r = 0; r < Rows; r++)
            {
                Add(BetSelection.Inside(BetType.Split, 0, r + 1), 1f, r + 0.5f, s, 1f - s, null);
            }
        }

        private void Add(BetSelection selection, float x, float y, float w, float h, string label)
        {
            var spot = new BetSpot(selection, x, y, w, h, label);
            _spots.Add(spot);
            _byKey.Add(selection.Key, spot);
        }
    }
}

using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// The betting layout for any wheel: a zero box, numbers 1..H in columns of three, column bets,
    /// thirds ("dozens") and the six even-money boxes, plus every legal split, street, corner and
    /// six-line. With H = 36 this is the traditional table. Numbers run 1 (near row) / 2 / 3 (far row)
    /// in the first column, then 4/5/6 and so on.
    ///
    /// Readability on big wheels: when there are more than <see cref="MaxColumnsPerBand"/> columns the
    /// number grid wraps into two stacked bands, so a 100-pocket layout is about as wide as a
    /// standard one. Splits, corners and six-lines are not offered across the seam between bands.
    ///
    /// Layout units: each number box is 1 x 1. Band 0's rows occupy y 0..3; band 1 sits above it at
    /// y <see cref="BandPitch"/>.. The zero box is x 0..1; columns start at x 1; dozens are at y -1..0
    /// and the even-money row at y -2..-1.
    /// </summary>
    public sealed class BettingLayout
    {
        public const int Rows = 3;
        public const int StandardHighestNumber = 36;
        public const int MaxColumnsPerBand = 18;
        public const float BandPitch = 3.25f;
        public const float BorderSpotSize = 0.32f;

        private readonly List<BetSpot> _spots = new List<BetSpot>();
        private readonly Dictionary<string, BetSpot> _byKey = new Dictionary<string, BetSpot>();

        public IReadOnlyList<BetSpot> Spots => _spots;

        /// <summary>Highest number on the layout; outside bets are evaluated against it.</summary>
        public int HighestNumber { get; }

        /// <summary>Number of three-number columns (the last may be partly empty).</summary>
        public int Columns { get; }

        /// <summary>1 for normal wheels, 2 when the grid wraps.</summary>
        public int Bands { get; }

        public int ColumnsPerBand { get; }

        public float MinX => 0f;
        public float MaxX { get; }
        public float MinY => -2f;
        public float MaxY { get; }

        public BettingLayout() : this(StandardHighestNumber) { }

        public BettingLayout(int highestNumber)
        {
            if (highestNumber < 1) throw new ArgumentOutOfRangeException(nameof(highestNumber));
            HighestNumber = highestNumber;
            Columns = (highestNumber + Rows - 1) / Rows;
            Bands = Columns > MaxColumnsPerBand ? 2 : 1;
            ColumnsPerBand = (Columns + Bands - 1) / Bands;
            MaxX = ColumnsPerBand + 2f;
            MaxY = Rows + (Bands - 1) * BandPitch;

            BuildBoxes();
            BuildBorderSpots();
        }

        public static int NumberAt(int column, int row) { return column * Rows + row + 1; }
        public static int ColumnOf(int number) { return (number - 1) / Rows; }
        public static int RowOf(int number) { return (number - 1) % Rows; }

        public int BandOf(int column) { return column / ColumnsPerBand; }

        /// <summary>Layout-space centre of a number box.</summary>
        public void CellCenter(int number, out float x, out float y)
        {
            int column = ColumnOf(number);
            int band = BandOf(column);
            x = 1f + (column - band * ColumnsPerBand) + 0.5f;
            y = RowOf(number) + 0.5f + band * BandPitch;
        }

        public bool TryGetSpot(BetSelection selection, out BetSpot spot)
        {
            return _byKey.TryGetValue(selection.Key, out spot);
        }

        /// <summary>True when the selection is a legal bet on this layout (e.g. a split of touching numbers).</summary>
        public bool IsLegal(BetSelection selection)
        {
            return selection != null && _byKey.ContainsKey(selection.Key);
        }

        /// <summary>Text for an outside box, adapted to the layout size.</summary>
        public string OutsideLabel(BetType type, int group)
        {
            int third = HighestNumber / 3;
            int half = HighestNumber / 2;
            switch (type)
            {
                case BetType.Dozen:
                    if (HighestNumber == StandardHighestNumber) return new[] { "1st 12", "2nd 12", "3rd 12" }[group];
                    int start = group * third + 1;
                    int end = group == 2 ? HighestNumber : (group + 1) * third;
                    return start + " to " + end;
                case BetType.Column: return "2 to 1";
                case BetType.Low: return "1 to " + half;
                case BetType.High: return (half + 1) + " to " + HighestNumber;
                case BetType.Even: return "EVEN";
                case BetType.Odd: return "ODD";
                case BetType.Red: return "RED";
                case BetType.Black: return "BLACK";
                default: return type.ToString();
            }
        }

        private bool Exists(int number) { return number >= 1 && number <= HighestNumber; }

        private void BuildBoxes()
        {
            // Zero spans every band.
            Add(BetSelection.Straight(0), 0.5f, MaxY * 0.5f, 1f, MaxY, "0");
            for (int c = 0; c < Columns; c++)
            {
                for (int r = 0; r < Rows; r++)
                {
                    int n = NumberAt(c, r);
                    if (!Exists(n)) continue;
                    float x, y;
                    CellCenter(n, out x, out y);
                    Add(BetSelection.Straight(n), x, y, 1f, 1f, n.ToString());
                }
            }

            float columnX = ColumnsPerBand + 1.5f;
            for (int r = 0; r < Rows; r++)
            {
                Add(BetSelection.Outside(BetType.Column, r), columnX, r + 0.5f, 1f, 1f, OutsideLabel(BetType.Column, r));
            }

            if (HighestNumber >= 3)
            {
                float dozenWidth = ColumnsPerBand / 3f;
                for (int d = 0; d < 3; d++)
                {
                    Add(BetSelection.Outside(BetType.Dozen, d), 1f + d * dozenWidth + dozenWidth * 0.5f, -0.5f, dozenWidth, 1f, OutsideLabel(BetType.Dozen, d));
                }
            }

            BetType[] evenMoney = { BetType.Low, BetType.Even, BetType.Red, BetType.Black, BetType.Odd, BetType.High };
            float evenWidth = ColumnsPerBand / 6f;
            for (int i = 0; i < evenMoney.Length; i++)
            {
                if (HighestNumber < 2 && (evenMoney[i] == BetType.Low || evenMoney[i] == BetType.High)) continue;
                Add(BetSelection.Outside(evenMoney[i]), 1f + i * evenWidth + evenWidth * 0.5f, -1.5f, evenWidth, 1f, OutsideLabel(evenMoney[i], 0));
            }
        }

        private void BuildBorderSpots()
        {
            float s = BorderSpotSize;
            for (int c = 0; c < Columns; c++)
            {
                bool sameBandAsNext = c + 1 < Columns && BandOf(c) == BandOf(c + 1);
                for (int r = 0; r < Rows; r++)
                {
                    int n = NumberAt(c, r);
                    if (!Exists(n)) continue;
                    float x, y;
                    CellCenter(n, out x, out y);
                    int right = NumberAt(c + 1, r);

                    // Split with the number in the next column (shared vertical border).
                    if (sameBandAsNext && Exists(right))
                    {
                        Add(BetSelection.Inside(BetType.Split, n, right), x + 0.5f, y, s, 1f - s, null);
                    }
                    // Split with the number in the next row (shared horizontal border).
                    if (r + 1 < Rows && Exists(n + 1))
                    {
                        Add(BetSelection.Inside(BetType.Split, n, n + 1), x, y + 0.5f, 1f - s, s, null);
                    }
                    // Corner where four numbers meet.
                    if (sameBandAsNext && r + 1 < Rows && Exists(n + 1) && Exists(right) && Exists(right + 1))
                    {
                        Add(BetSelection.Inside(BetType.Corner, n, n + 1, right, right + 1), x + 0.5f, y + 0.5f, s, s, null);
                    }
                }

                // Street: the whole column of three, placed on its near edge.
                int first = NumberAt(c, 0);
                if (Exists(first + 2))
                {
                    float x, y;
                    CellCenter(first, out x, out y);
                    Add(BetSelection.Inside(BetType.Street, first, first + 1, first + 2), x, y - 0.5f, 1f - s, s, null);

                    // Six line: two adjacent full streets, placed where their near edges meet.
                    if (sameBandAsNext && Exists(first + 5))
                    {
                        Add(BetSelection.Inside(BetType.SixLine, first, first + 1, first + 2, first + 3, first + 4, first + 5), x + 0.5f, y - 0.5f, s, s, null);
                    }
                }
            }

            // Splits between zero and 1, 2, 3.
            for (int r = 0; r < Rows; r++)
            {
                if (!Exists(r + 1)) continue;
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

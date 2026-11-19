namespace DeadSpin.Core.Betting
{
    /// <summary>
    /// A place on the felt where chips can go, with its position in layout units so the table
    /// view and the rules always agree. Layout units: each number box is 1 x 1; x runs from the zero
    /// box (0..1) along the twelve columns (1..13) to the column bets (13..14); y runs from the
    /// even-money row (-2..-1) and dozens (-1..0) up through the three number rows (0..3).
    /// </summary>
    public sealed class BetSpot
    {
        public BetSelection Selection { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float Width { get; }
        public float Height { get; }

        /// <summary>Text printed on the felt for boxes ("17", "2 to 1", "RED"); null for line and corner spots.</summary>
        public string Label { get; }

        /// <summary>Line and corner spots sit on the borders between boxes and take priority when clicked.</summary>
        public bool IsBorderSpot => Label == null;

        public BetSpot(BetSelection selection, float centerX, float centerY, float width, float height, string label)
        {
            Selection = selection;
            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
            Label = label;
        }
    }
}

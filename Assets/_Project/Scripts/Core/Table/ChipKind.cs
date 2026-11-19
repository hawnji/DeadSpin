using System;

namespace DeadSpin.Core.Table
{
    /// <summary>Special chip types. Standard chips are free; the others come from the shop or abilities.</summary>
    public enum ChipKind
    {
        Standard,
        /// <summary>Clings to the felt: resists shaking strongly, but magnet interventions can drag it.</summary>
        Magnetic,
        /// <summary>Never slides, but once placed it cannot be taken back or moved.</summary>
        Sticky,
        /// <summary>Heavy: rarely moves when the table shakes.</summary>
        Weighted,
        /// <summary>Pays 10% more profit, but shatters (is lost) if a shake knocks it off the layout.</summary>
        Fragile,
        /// <summary>Immune to every table manipulation.</summary>
        Protected
    }

    /// <summary>Physical properties of a chip kind, used by table manipulation.</summary>
    public readonly struct ChipProperties
    {
        public readonly float Weight;
        public readonly float Friction;
        public readonly float MagneticResistance;
        /// <summary>0..1: chance multiplier that the chip stays put when shaken (1 = never moves).</summary>
        public readonly float Stability;
        public readonly bool CanBeRemoved;
        public readonly int ProfitBonusPercent;

        public ChipProperties(float weight, float friction, float magneticResistance, float stability, bool canBeRemoved, int profitBonusPercent)
        {
            Weight = weight;
            Friction = friction;
            MagneticResistance = magneticResistance;
            Stability = stability;
            CanBeRemoved = canBeRemoved;
            ProfitBonusPercent = profitBonusPercent;
        }

        public static ChipProperties Of(ChipKind kind)
        {
            switch (kind)
            {
                case ChipKind.Magnetic: return new ChipProperties(1.0f, 0.6f, 0.0f, 0.85f, true, 0);
                case ChipKind.Sticky: return new ChipProperties(1.0f, 1.0f, 1.0f, 1.0f, false, 0);
                case ChipKind.Weighted: return new ChipProperties(3.0f, 0.7f, 0.8f, 0.8f, true, 0);
                case ChipKind.Fragile: return new ChipProperties(0.6f, 0.3f, 0.5f, 0.2f, true, 10);
                case ChipKind.Protected: return new ChipProperties(1.0f, 1.0f, 1.0f, 1.0f, true, 0);
                default: return new ChipProperties(1.0f, 0.4f, 0.5f, 0.35f, true, 0);
            }
        }

        /// <summary>When chips of two kinds share a spot, the steadier kind holds the stack.</summary>
        public static ChipKind Combine(ChipKind a, ChipKind b)
        {
            if (a == b) return a;
            if (a == ChipKind.Protected || b == ChipKind.Protected) return ChipKind.Protected;
            if (a == ChipKind.Sticky || b == ChipKind.Sticky) return ChipKind.Sticky;
            return Of(a).Stability >= Of(b).Stability ? a : b;
        }

        public static string Describe(ChipKind kind)
        {
            switch (kind)
            {
                case ChipKind.Magnetic: return "Magnetic chips cling to the felt.";
                case ChipKind.Sticky: return "Sticky chips never slide, but cannot be taken back.";
                case ChipKind.Weighted: return "Weighted chips barely move when the table shakes.";
                case ChipKind.Fragile: return "Fragile chips pay 10% more, but shatter if knocked off.";
                case ChipKind.Protected: return "Protected chips ignore all table tricks.";
                default: return "Standard chips.";
            }
        }
    }
}

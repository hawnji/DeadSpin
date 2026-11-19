using System;

namespace DeadSpin.Core.Common
{
    /// <summary>
    /// Deterministic PCG32 (XSH-RR) generator. Identical seeds produce identical sequences on
    /// every platform, unlike System.Random whose algorithm is not guaranteed across runtimes.
    /// Integer ranges use rejection sampling so results are unbiased, which matters for the
    /// probability guarantees of the roulette system.
    /// </summary>
    public sealed class SeededRandom : IRandomSource
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;

        private ulong _state;

        public ulong Seed { get; }

        /// <summary>The generator's internal state, for save games. Restoring it continues the exact sequence.</summary>
        public ulong State
        {
            get { return _state; }
            set { _state = value; }
        }

        public SeededRandom(ulong seed)
        {
            Seed = seed;
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>Creates a generator seeded from the current time; use only where reproducibility is not needed.</summary>
        public static SeededRandom FromTime()
        {
            return new SeededRandom((ulong)DateTime.UtcNow.Ticks);
        }

        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + Increment);
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotation = (int)(oldState >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be positive.");
            }

            uint bound = (uint)maxExclusive;
            // Rejection threshold removes modulo bias: values below it are discarded.
            uint threshold = unchecked((uint)(-(int)bound)) % bound;
            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold)
                {
                    return (int)(value % bound);
                }
            }
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be greater than lower bound.");
            }

            long range = (long)maxExclusive - minInclusive;
            if (range > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range too large.");
            }

            return minInclusive + NextInt((int)range);
        }

        public double NextDouble()
        {
            // 53 random bits mapped to [0, 1).
            ulong high = NextUInt();
            ulong low = NextUInt();
            ulong bits = ((high << 32) | low) >> 11;
            return bits * (1.0 / (1UL << 53));
        }
    }
}

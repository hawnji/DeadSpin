namespace DeadSpin.Core.Common
{
    /// <summary>
    /// Abstraction over random number generation so every gameplay system can be driven
    /// by a deterministic, seedable source (reproducible tests, replays, seeded runs).
    /// Gameplay code must never call System.Random or UnityEngine.Random directly.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>The seed this source was created with.</summary>
        ulong Seed { get; }

        /// <summary>Returns a uniformly distributed integer in [0, maxExclusive).</summary>
        int NextInt(int maxExclusive);

        /// <summary>Returns a uniformly distributed integer in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Returns a uniformly distributed double in [0, 1).</summary>
        double NextDouble();
    }
}

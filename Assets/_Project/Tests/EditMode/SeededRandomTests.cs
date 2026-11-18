using System;
using DeadSpin.Core.Common;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new SeededRandom(12345);
            var b = new SeededRandom(12345);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextInt(1000), b.NextInt(1000));
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new SeededRandom(1);
            var b = new SeededRandom(2);
            int same = 0;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextInt(1000000) == b.NextInt(1000000)) same++;
            }
            Assert.Less(same, 5);
        }

        [Test]
        public void NextInt_StaysInRange()
        {
            var rng = new SeededRandom(7);
            for (int i = 0; i < 10000; i++)
            {
                int value = rng.NextInt(-5, 5);
                Assert.GreaterOrEqual(value, -5);
                Assert.Less(value, 5);
            }
        }

        [Test]
        public void NextDouble_StaysInUnitInterval()
        {
            var rng = new SeededRandom(99);
            for (int i = 0; i < 10000; i++)
            {
                double value = rng.NextDouble();
                Assert.GreaterOrEqual(value, 0.0);
                Assert.Less(value, 1.0);
            }
        }

        [Test]
        public void NextInt_IsUniformWithinStatisticalTolerance()
        {
            // Chi-squared test over 37 buckets (a classic single-zero wheel). 200k samples.
            const int buckets = 37;
            const int samples = 200000;
            var counts = new int[buckets];
            var rng = new SeededRandom(2026);
            for (int i = 0; i < samples; i++) counts[rng.NextInt(buckets)]++;

            double expected = (double)samples / buckets;
            double chiSquared = 0;
            for (int i = 0; i < buckets; i++)
            {
                double diff = counts[i] - expected;
                chiSquared += diff * diff / expected;
            }

            // Critical value for 36 degrees of freedom at p = 0.001 is about 67.98.
            Assert.Less(chiSquared, 67.98, "Distribution is not uniform enough: chi^2 = " + chiSquared);
        }

        [Test]
        public void InvalidBounds_Throw()
        {
            var rng = new SeededRandom(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using DeadSpin.Core.Common;
using DeadSpin.Core.Wheel;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class WheelTests
    {
        [Test]
        public void European_HasClassicLayout()
        {
            WheelConfiguration wheel = StandardWheels.European();
            Assert.AreEqual(37, wheel.PocketCount);
            Assert.AreEqual(36, wheel.HighestNumber);
            Assert.AreEqual(37, wheel.TotalWeight);
            Assert.IsEmpty(wheel.Validate());
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 37), wheel.Pockets.Select(p => p.Number));
            Assert.AreEqual(18, wheel.Pockets.Count(p => p.Color == PocketColor.Red));
            Assert.AreEqual(18, wheel.Pockets.Count(p => p.Color == PocketColor.Black));
            Assert.AreEqual(PocketColor.Green, wheel.Pockets[0].Color);
            Assert.AreEqual(32, wheel.Pockets[1].Number, "32 sits clockwise next to zero.");
        }

        [Test]
        public void European_ColoursAlternateAroundTheWheel()
        {
            WheelConfiguration wheel = StandardWheels.European();
            for (int i = 1; i < wheel.PocketCount - 1; i++)
            {
                Assert.AreNotEqual(wheel.Pockets[i].Color, wheel.Pockets[i + 1].Color, "Pockets " + i + " and " + (i + 1));
            }
        }

        [Test]
        public void Validation_CatchesBrokenWheels()
        {
            var noWeight = new WheelConfiguration("dead", new[] { new Pocket(0, 0, PocketColor.Green, 0), new Pocket(1, 1, PocketColor.Red, 0) });
            Assert.IsNotEmpty(noWeight.Validate());

            var zerosOnly = new WheelConfiguration("zeros", new[] { new Pocket(0, 0, PocketColor.Green), new Pocket(1, 1, PocketColor.Red, 1, false) });
            Assert.IsNotEmpty(zerosOnly.Validate());

            var duplicateIds = new WheelConfiguration("dupes", new[] { new Pocket(1, 1, PocketColor.Red), new Pocket(1, 2, PocketColor.Black) });
            Assert.IsNotEmpty(duplicateIds.Validate());

            var redZero = new WheelConfiguration("red zero", new[] { new Pocket(0, 0, PocketColor.Red), new Pocket(1, 1, PocketColor.Red) });
            Assert.IsNotEmpty(redZero.Validate());

            Assert.Throws<InvalidOperationException>(() => noWeight.ThrowIfInvalid());
        }

        [Test]
        public void Odds_CountEveryClonedPocket()
        {
            // 72 pockets: 1..36 twice, but 13 appears four times (two extra clones replace 14 and 15).
            var pockets = new List<Pocket>();
            int id = 0;
            for (int copy = 0; copy < 2; copy++)
            {
                for (int n = 1; n <= 36; n++)
                {
                    int number = copy == 1 && (n == 14 || n == 15) ? 13 : n;
                    pockets.Add(new Pocket(id++, number, StandardWheels.TraditionalColor(number)));
                }
            }
            var wheel = new WheelConfiguration("72 with clones", pockets);
            Assert.AreEqual(72, wheel.TotalWeight);
            Assert.AreEqual(4.0 / 72, WheelOdds.NumberProbability(wheel, 13), 1e-12);
            Assert.AreEqual(1.0 / 72, WheelOdds.NumberProbability(wheel, 14), 1e-12);
            Assert.AreEqual(2.0 / 72, WheelOdds.NumberProbability(wheel, 20), 1e-12);
        }

        [Test]
        public void Odds_RespectWeightsAndClosedPockets()
        {
            var wheel = new WheelConfiguration("weighted", new[]
            {
                new Pocket(0, 0, PocketColor.Green),
                new Pocket(1, 1, PocketColor.Red, 3),
                new Pocket(2, 2, PocketColor.Black),
                new Pocket(3, 3, PocketColor.Red, 1, false)
            });
            Assert.AreEqual(5, wheel.TotalWeight);
            Assert.AreEqual(0.6, WheelOdds.PocketProbability(wheel, 1), 1e-12);
            Assert.AreEqual(0.0, WheelOdds.PocketProbability(wheel, 3), 1e-12);
            double sum = 0;
            for (int i = 0; i < wheel.PocketCount; i++) sum += WheelOdds.PocketProbability(wheel, i);
            Assert.AreEqual(1.0, sum, 1e-12);
        }

        [Test]
        public void OutcomeSelector_IsReproducibleWithSeed()
        {
            WheelConfiguration wheel = StandardWheels.European();
            var a = new SeededRandom(77);
            var b = new SeededRandom(77);
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(OutcomeSelector.SelectPocketIndex(wheel, a), OutcomeSelector.SelectPocketIndex(wheel, b));
            }
        }

        [Test]
        public void OutcomeSelector_NeverPicksClosedOrZeroWeightPockets()
        {
            var wheel = new WheelConfiguration("gaps", new[]
            {
                new Pocket(0, 0, PocketColor.Green, 0),
                new Pocket(1, 1, PocketColor.Red),
                new Pocket(2, 2, PocketColor.Black, 1, false),
                new Pocket(3, 3, PocketColor.Red)
            });
            var rng = new SeededRandom(5);
            for (int i = 0; i < 5000; i++)
            {
                int index = OutcomeSelector.SelectPocketIndex(wheel, rng);
                Assert.That(index == 1 || index == 3, "Picked pocket " + index);
            }
        }

        [Test]
        public void Simulation_European_MatchesUniformOdds()
        {
            // 740,000 spins: chi-squared over 37 pockets, 36 degrees of freedom, p = 0.001 critical value 67.98.
            WheelConfiguration wheel = StandardWheels.European();
            AssertMatchesOdds(wheel, 740000, 2026, 67.98);
        }

        [Test]
        public void Simulation_WeightedAndClonedWheel_MatchesCalculatedOdds()
        {
            // 12 pockets with weights 1..4 and a cloned number: 11 degrees of freedom, p = 0.001 critical value 31.26.
            var pockets = new List<Pocket>();
            for (int i = 0; i < 12; i++)
            {
                int number = i == 11 ? 5 : i;
                PocketColor color = number == 0 ? PocketColor.Green : (number % 2 == 1 ? PocketColor.Red : PocketColor.Black);
                pockets.Add(new Pocket(i, number, color, 1 + i % 4));
            }
            var wheel = new WheelConfiguration("weighted 12", pockets);
            AssertMatchesOdds(wheel, 400000, 99, 31.26);
        }

        private static void AssertMatchesOdds(WheelConfiguration wheel, int spins, ulong seed, double criticalValue)
        {
            var counts = new int[wheel.PocketCount];
            var rng = new SeededRandom(seed);
            for (int i = 0; i < spins; i++) counts[OutcomeSelector.SelectPocketIndex(wheel, rng)]++;

            double chiSquared = 0;
            for (int i = 0; i < wheel.PocketCount; i++)
            {
                double expected = spins * WheelOdds.PocketProbability(wheel, i);
                if (expected <= 0)
                {
                    Assert.AreEqual(0, counts[i], "A pocket with zero probability came up.");
                    continue;
                }
                double diff = counts[i] - expected;
                chiSquared += diff * diff / expected;
            }
            Assert.Less(chiSquared, criticalValue, "Observed frequencies differ from calculated odds: chi^2 = " + chiSquared);
        }
    }
}

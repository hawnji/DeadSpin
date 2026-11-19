using System;
using System.Collections.Generic;
using System.Linq;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    /// <summary>Phase 3: procedural wheels of any size, transformations, rings and the adapting layout.</summary>
    public class DynamicWheelTests
    {
        private static IEnumerable<int> PresetCounts => WheelFactory.Presets.Select(p => p.PocketCount);

        [Test]
        public void EveryPreset_IsValid_WithEachNumberOnce()
        {
            foreach (WheelPreset preset in WheelFactory.Presets)
            {
                WheelConfiguration wheel = WheelFactory.Create(preset.HighestNumber, preset.Zeros);
                Assert.IsEmpty(wheel.Validate(), preset.Name);
                Assert.AreEqual(preset.PocketCount, wheel.PocketCount, preset.Name);
                Assert.AreEqual(preset.HighestNumber, wheel.HighestNumber, preset.Name);
                CollectionAssert.AreEquivalent(Enumerable.Range(1, preset.HighestNumber), wheel.Pockets.Where(p => !p.IsZero).Select(p => p.Number), preset.Name);
                Assert.AreEqual(preset.Zeros, wheel.Pockets.Count(p => p.IsZero), preset.Name);
                Assert.AreEqual(wheel.PocketCount, wheel.Pockets.Select(p => p.Id).Distinct().Count(), preset.Name + " ids are unique");
            }
        }

        [Test]
        public void SupportedSizes_IncludeEverySpecCount()
        {
            CollectionAssert.IsSubsetOf(new[] { 12, 18, 24, 36, 37, 48, 72, 100 }, PresetCounts.ToArray());
            Assert.AreEqual(37, WheelFactory.Create(37).PocketCount);
            Assert.AreSame(StandardWheels.European().Name, WheelFactory.Create(37).Name);
            // Custom sizes work too.
            Assert.IsEmpty(WheelFactory.Create(61).Validate());
            Assert.IsEmpty(WheelFactory.Create(120, 2).Validate());
        }

        [Test]
        public void GeneratedWheels_NeighboursNeverShareColourOrFollowEachOther()
        {
            foreach (int count in new[] { 12, 18, 24, 36, 48, 72 })
            {
                WheelConfiguration wheel = WheelFactory.Create(count);
                for (int i = 0; i < wheel.PocketCount - 1; i++)
                {
                    Pocket a = wheel.Pockets[i], b = wheel.Pockets[i + 1];
                    if (!a.IsZero && !b.IsZero)
                    {
                        Assert.AreNotEqual(a.Color, b.Color, count + " pockets: slots " + i + "/" + (i + 1));
                        Assert.AreNotEqual(1, Math.Abs(a.Number - b.Number), count + " pockets: consecutive numbers side by side");
                    }
                }
            }
        }

        [Test]
        public void Simulation_72And100PocketWheels_MatchCalculatedOdds()
        {
            foreach (int count in new[] { 72, 100 })
            {
                WheelConfiguration wheel = WheelFactory.Create(count);
                var random = new SeededRandom(1000 + (ulong)count);
                int spins = count * 4000;
                var hits = new int[wheel.PocketCount];
                for (int i = 0; i < spins; i++) hits[OutcomeSelector.SelectPocketIndex(wheel, random)]++;
                double expected = (double)spins / wheel.PocketCount;
                double chi = hits.Sum(h => (h - expected) * (h - expected) / expected);
                // Upper 0.1% critical value for 99 degrees of freedom is about 149; for 71 about 112.
                Assert.Less(chi, count == 100 ? 149.0 : 112.0, count + "-pocket wheel chi-squared");
            }
        }

        [Test]
        public void Payouts_StayHonestOnEveryWheelSize()
        {
            var policy = PayoutPolicy.Standard;
            foreach (WheelPreset preset in WheelFactory.Presets)
            {
                WheelConfiguration wheel = WheelFactory.Create(preset.HighestNumber, preset.Zeros);
                var layout = new BettingLayout(wheel.HighestNumber);
                foreach (BetSpot spot in layout.Spots)
                {
                    BetSelection bet = spot.Selection;
                    long win = PayoutPolicy.WinningWeight(wheel, bet, layout.HighestNumber);
                    if (win == 0) continue;
                    double p = (double)win / wheel.TotalWeight;
                    decimal ratio = policy.PayoutRatio(wheel, bet, layout.HighestNumber);
                    double returnToPlayer = p * (1.0 + (double)ratio);
                    // True odds minus the 1/37 edge, give or take the cent rounding of the ratio.
                    Assert.AreEqual(36.0 / 37.0, returnToPlayer, p * 0.006 + 1e-9, preset.Name + " " + bet.Key);
                }
            }
        }

        [Test]
        public void MultipleBalls_PayFromTheChanceOfAnyBallHitting()
        {
            WheelConfiguration wheel = StandardWheels.European();
            BetSelection straight = BetSelection.Straight(7);
            double p1 = PayoutPolicy.WinProbability(wheel, straight, 36, 1);
            double p2 = PayoutPolicy.WinProbability(wheel, straight, 36, 2);
            Assert.AreEqual(1.0 / 37, p1, 1e-12);
            Assert.AreEqual(1 - Math.Pow(36.0 / 37, 2), p2, 1e-12);
            decimal r2 = PayoutPolicy.Standard.PayoutRatio(wheel, straight, 36, 2);
            Assert.AreEqual(36.0 / 37.0, p2 * (1 + (double)r2), 0.003);
            Assert.Less(r2, 35m, "Two balls make a straight up easier, so it pays less.");
        }

        [Test]
        public void Clone_AddsPocketsAndRaisesThatNumbersOdds()
        {
            WheelConfiguration wheel = StandardWheels.European();
            WheelChange change = WheelTransforms.CloneNumber(wheel, 13, 4);
            Assert.IsTrue(change.Succeeded, change.Error);
            Assert.AreEqual(40, change.After.PocketCount);
            Assert.AreEqual(4, change.After.CountOf(13));
            Assert.AreEqual(4.0 / 40, WheelOdds.NumberProbability(change.After, 13), 1e-12);
            WheelDiff diff = change.Diff();
            Assert.AreEqual(3, diff.Added);
            Assert.AreEqual(0, diff.Removed);
            Assert.IsTrue(diff.Moves.Where(m => m.IsAdded).All(m => m.SourceIndex == wheel.IndicesOfNumber(13)[0]), "Clones emerge from the original 13.");
        }

        [Test]
        public void Remove_Add_Swap_Shuffle_AreValidatedAndPure()
        {
            WheelConfiguration wheel = StandardWheels.European();
            WheelChange removed = WheelTransforms.RemoveNumber(wheel, 5);
            Assert.IsTrue(removed.Succeeded);
            Assert.AreEqual(36, removed.After.PocketCount);
            Assert.IsFalse(removed.After.Contains(5));
            Assert.AreEqual(37, wheel.PocketCount, "The original wheel is untouched.");

            WheelChange added = WheelTransforms.AddNumbers(wheel, 12);
            Assert.IsTrue(added.Succeeded);
            Assert.AreEqual(48, added.After.HighestNumber);
            Assert.AreEqual(49, added.After.PocketCount);

            WheelChange swapped = WheelTransforms.Swap(wheel, 1, 2);
            Assert.AreEqual(wheel.Pockets[1].Number, swapped.After.Pockets[2].Number);

            WheelChange shuffled = WheelTransforms.Shuffle(wheel, new SeededRandom(3));
            CollectionAssert.AreEquivalent(wheel.Pockets.Select(p => p.Id), shuffled.After.Pockets.Select(p => p.Id));
            Assert.AreEqual(37, shuffled.After.TotalWeight);

            WheelChange rotated = WheelTransforms.RotateSection(wheel, 35, 6, 2);
            Assert.IsTrue(rotated.Succeeded);
            CollectionAssert.AreEquivalent(wheel.Pockets.Select(p => p.Number), rotated.After.Pockets.Select(p => p.Number));
        }

        [Test]
        public void Validation_RefusesChangesThatWouldBreakTheWheel()
        {
            var tiny = new WheelConfiguration("tiny", new[] { new Pocket(0, 0, PocketColor.Green), new Pocket(1, 1, PocketColor.Red) });
            Assert.IsFalse(WheelTransforms.RemoveNumber(tiny, 1).Succeeded, "Removing the last winnable number is refused.");
            Assert.IsFalse(WheelTransforms.SetActive(tiny, 1, false).Succeeded, "Killing the last winnable number is refused.");
            Assert.IsFalse(WheelTransforms.Resize(tiny, 500).Succeeded);
            Assert.IsFalse(WheelTransforms.Clone(WheelFactory.Create(99, 1), 3, 40).Succeeded, "Too many pockets.");
            Assert.IsFalse(WheelTransforms.RemoveNumber(tiny, 9).Succeeded, "Missing numbers cannot be removed.");
            Assert.IsNotEmpty(WheelTransforms.RemoveNumber(tiny, 1).Error);
        }

        [Test]
        public void Resize_KeepsIdsOfSurvivingNumbersSoTheyCanSlide()
        {
            WheelConfiguration wheel = WheelFactory.Create(36);
            WheelChange change = WheelTransforms.Resize(wheel, 72);
            Assert.IsTrue(change.Succeeded, change.Error);
            Assert.AreEqual(72, change.After.PocketCount);
            WheelDiff diff = change.Diff();
            Assert.AreEqual(36, diff.Added, "Numbers 37-72 are new.");
            Assert.AreEqual(0, diff.Removed, "Every old number survives.");
            foreach (Pocket p in wheel.Pockets)
            {
                Assert.AreEqual(p.Number, change.After.Pockets[change.After.IndexOfId(p.Id)].Number, "Pocket " + p.Id + " keeps its number.");
            }

            WheelChange shrink = WheelTransforms.Resize(change.After, 12);
            Assert.AreEqual(60, shrink.Diff().Removed);
        }

        [Test]
        public void ColorReversal_AndWeights()
        {
            WheelConfiguration wheel = StandardWheels.European();
            WheelConfiguration reversed = WheelTransforms.ReverseColors(wheel).After;
            Assert.AreEqual(PocketColor.Black, reversed.ColorOf(1));
            Assert.AreEqual(PocketColor.Green, reversed.ColorOf(0));

            WheelConfiguration lucky = WheelTransforms.SetNumberWeight(wheel, 7, 3).After;
            Assert.AreEqual(39, lucky.TotalWeight);
            Assert.AreEqual(3.0 / 39, WheelOdds.NumberProbability(lucky, 7), 1e-12);
            decimal ratio = PayoutPolicy.Standard.PayoutRatio(lucky, BetSelection.Straight(7), 36);
            Assert.Less(ratio, 35m, "A magnetic pocket pays less because it is more likely.");

            WheelConfiguration dead = WheelTransforms.SetActive(wheel, 7, false).After;
            Assert.AreEqual(0, WheelOdds.NumberProbability(dead, 7));
            Assert.AreEqual(36, dead.TotalWeight);
        }

        [Test]
        public void Ring_DrawMatchesSegmentWidths()
        {
            WheelRing ring = WheelRing.Standard();
            Assert.Less(ring.ExpectedMultiplier, 1.0, "The bonus ring must not hand the player an edge.");
            var random = new SeededRandom(99);
            var hits = new int[ring.Segments.Count];
            const int draws = 180000;
            for (int i = 0; i < draws; i++) hits[OutcomeSelector.SelectRingSegment(ring, random)]++;
            for (int i = 0; i < hits.Length; i++)
            {
                double expected = (double)draws * ring.Segments[i].Weight / ring.TotalWeight;
                Assert.AreEqual(expected, hits[i], 5 * Math.Sqrt(expected), "Segment " + i);
            }
        }

        [Test]
        public void Layout_AdaptsToEveryWheelAndWrapsBigOnes()
        {
            foreach (WheelPreset preset in WheelFactory.Presets)
            {
                var layout = new BettingLayout(preset.HighestNumber);
                for (int n = 1; n <= preset.HighestNumber; n++) Assert.IsTrue(layout.IsLegal(BetSelection.Straight(n)), preset.Name + " straight " + n);
                Assert.IsFalse(layout.IsLegal(BetSelection.Straight(preset.HighestNumber + 1)));
                Assert.IsTrue(layout.IsLegal(BetSelection.Outside(BetType.Red)));
                Assert.LessOrEqual(layout.ColumnsPerBand, BettingLayout.MaxColumnsPerBand, preset.Name);
                Assert.AreEqual(layout.Spots.Count, layout.Spots.Select(s => s.Selection.Key).Distinct().Count());
                foreach (BetSpot spot in layout.Spots)
                {
                    Assert.GreaterOrEqual(spot.CenterX - spot.Width / 2f, layout.MinX - 1e-4f, spot.Selection.Key);
                    Assert.LessOrEqual(spot.CenterX + spot.Width / 2f, layout.MaxX + 1e-4f, spot.Selection.Key);
                    Assert.LessOrEqual(spot.CenterY + spot.Height / 2f, layout.MaxY + 1e-4f, spot.Selection.Key);
                }
            }
            var big = new BettingLayout(99);
            Assert.AreEqual(2, big.Bands);
            Assert.AreEqual("1 to 33", big.OutsideLabel(BetType.Dozen, 0));
            Assert.AreEqual("50 to 99", big.OutsideLabel(BetType.High, 0));
            // No split across the seam between the two bands.
            int lastColumnOfBand0 = big.ColumnsPerBand - 1;
            int n0 = BettingLayout.NumberAt(lastColumnOfBand0, 0);
            Assert.IsFalse(big.IsLegal(BetSelection.Inside(BetType.Split, n0, n0 + 3)));
        }

        [Test]
        public void Table_ExpandsMidRunAndTheLayoutFollows()
        {
            var config = RunConfig.CreateDefault();
            config.AnnouncementSeconds = 0f;
            config.SummarySeconds = 0f;
            config.Seed = 5;
            var game = new TableGame(new Run(config), WheelFactory.Create(36));
            Assert.AreEqual(36, game.HighestNumber);
            Assert.IsTrue(game.ApplyWheelChange(WheelTransforms.Resize(game.Wheel, 72)));
            Assert.AreEqual(72, game.HighestNumber);
            game.TryBeginRound();
            Assert.AreEqual(PlacementResult.Accepted, game.PlaceBet(BetSelection.Straight(70), 10));
            Assert.AreEqual(69.05m, game.PayoutRatio(BetSelection.Straight(70)), "72 pockets: (36/37) x 72 - 1 = 69.05 to 1.");
        }
    }
}

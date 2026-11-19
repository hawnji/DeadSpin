using System;
using System.Linq;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Wheel;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class BettingTests
    {
        private static readonly StandardLayout Layout = new StandardLayout();
        private static readonly WheelConfiguration European = StandardWheels.European();

        private static int[] Winners(BetSelection selection)
        {
            return European.Pockets.Where(p => selection.Covers(p, 36)).Select(p => p.Number).OrderBy(n => n).ToArray();
        }

        [Test]
        public void Layout_HasEveryTraditionalSpot()
        {
            int Count(BetType t) { return Layout.Spots.Count(s => s.Selection.Type == t); }
            Assert.AreEqual(37, Count(BetType.Straight));
            Assert.AreEqual(33 + 24 + 3, Count(BetType.Split), "33 across, 24 up, 3 with zero");
            Assert.AreEqual(12, Count(BetType.Street));
            Assert.AreEqual(22, Count(BetType.Corner));
            Assert.AreEqual(11, Count(BetType.SixLine));
            Assert.AreEqual(3, Count(BetType.Dozen));
            Assert.AreEqual(3, Count(BetType.Column));
            foreach (BetType t in new[] { BetType.Red, BetType.Black, BetType.Odd, BetType.Even, BetType.Low, BetType.High })
            {
                Assert.AreEqual(1, Count(t), t.ToString());
            }
        }

        [Test]
        public void Layout_SpotsStayInsideTheFelt()
        {
            foreach (BetSpot spot in Layout.Spots)
            {
                Assert.GreaterOrEqual(spot.CenterX - spot.Width / 2f, StandardLayout.MinX - 1e-4f, spot.Selection.Key);
                Assert.LessOrEqual(spot.CenterX + spot.Width / 2f, StandardLayout.MaxX + 1e-4f, spot.Selection.Key);
                Assert.GreaterOrEqual(spot.CenterY - spot.Height / 2f, StandardLayout.MinY - 0.2f, spot.Selection.Key);
                Assert.LessOrEqual(spot.CenterY + spot.Height / 2f, StandardLayout.MaxY + 1e-4f, spot.Selection.Key);
            }
        }

        [Test]
        public void Legality_OnlyTouchingNumbersForm_SplitsAndCorners()
        {
            Assert.IsTrue(Layout.IsLegal(BetSelection.Inside(BetType.Split, 17, 20)));
            Assert.IsTrue(Layout.IsLegal(BetSelection.Inside(BetType.Split, 17, 18)));
            Assert.IsTrue(Layout.IsLegal(BetSelection.Inside(BetType.Split, 0, 2)));
            Assert.IsFalse(Layout.IsLegal(BetSelection.Inside(BetType.Split, 3, 4)), "3 and 4 are not adjacent on the layout.");
            Assert.IsFalse(Layout.IsLegal(BetSelection.Inside(BetType.Split, 17, 22)));
            Assert.IsTrue(Layout.IsLegal(BetSelection.Inside(BetType.Corner, 17, 18, 20, 21)));
            Assert.IsFalse(Layout.IsLegal(BetSelection.Inside(BetType.Corner, 2, 3, 4, 5)));
            Assert.IsTrue(Layout.IsLegal(BetSelection.Inside(BetType.Street, 13, 14, 15)));
            Assert.IsFalse(Layout.IsLegal(BetSelection.Inside(BetType.Street, 14, 15, 16)));
            Assert.IsTrue(Layout.IsLegal(BetSelection.Inside(BetType.SixLine, 31, 32, 33, 34, 35, 36)));
        }

        [Test]
        public void Selection_RejectsWrongShapes()
        {
            Assert.Throws<ArgumentException>(() => BetSelection.Inside(BetType.Split, 5));
            Assert.Throws<ArgumentException>(() => BetSelection.Inside(BetType.Corner, 1, 1, 2, 4));
            Assert.Throws<ArgumentException>(() => BetSelection.Outside(BetType.Straight));
            Assert.Throws<ArgumentOutOfRangeException>(() => BetSelection.Outside(BetType.Dozen, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => BetSelection.Outside(BetType.Red, 1));
        }

        [Test]
        public void Selection_EqualityStacksChipsOnTheSameSpot()
        {
            Assert.AreEqual(BetSelection.Inside(BetType.Split, 20, 17), BetSelection.Inside(BetType.Split, 17, 20));
            Assert.AreNotEqual(BetSelection.Outside(BetType.Dozen, 0), BetSelection.Outside(BetType.Dozen, 1));
        }

        [Test]
        public void Coverage_OutsideBets()
        {
            CollectionAssert.AreEqual(new[] { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 }, Winners(BetSelection.Outside(BetType.Red)));
            Assert.AreEqual(18, Winners(BetSelection.Outside(BetType.Black)).Length);
            CollectionAssert.AreEqual(Enumerable.Range(1, 18).ToArray(), Winners(BetSelection.Outside(BetType.Low)));
            CollectionAssert.AreEqual(Enumerable.Range(19, 18).ToArray(), Winners(BetSelection.Outside(BetType.High)));
            CollectionAssert.AreEqual(Enumerable.Range(13, 12).ToArray(), Winners(BetSelection.Outside(BetType.Dozen, 1)));
            CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(i => 3 * i + 3).ToArray(), Winners(BetSelection.Outside(BetType.Column, 2)));
            Assert.IsTrue(Winners(BetSelection.Outside(BetType.Even)).All(n => n % 2 == 0 && n > 0));
            Assert.IsTrue(Winners(BetSelection.Outside(BetType.Odd)).All(n => n % 2 == 1));
        }

        [Test]
        public void Coverage_ZeroLosesEveryOutsideBet_ButWinsItsStraight()
        {
            Pocket zero = European.Pockets[0];
            foreach (BetSpot spot in Layout.Spots.Where(s => !s.Selection.IsInside))
            {
                Assert.IsFalse(spot.Selection.Covers(zero, 36), spot.Selection.Key);
            }
            Assert.IsTrue(BetSelection.Straight(0).Covers(zero, 36));
        }

        [Test]
        public void Payouts_OnStandardWheel_MatchClassicTable()
        {
            var policy = PayoutPolicy.Standard;
            decimal Ratio(BetSelection s) { return policy.PayoutRatio(European, s, 36); }
            Assert.AreEqual(35m, Ratio(BetSelection.Straight(17)));
            Assert.AreEqual(35m, Ratio(BetSelection.Straight(0)));
            Assert.AreEqual(17m, Ratio(BetSelection.Inside(BetType.Split, 17, 20)));
            Assert.AreEqual(11m, Ratio(BetSelection.Inside(BetType.Street, 13, 14, 15)));
            Assert.AreEqual(8m, Ratio(BetSelection.Inside(BetType.Corner, 17, 18, 20, 21)));
            Assert.AreEqual(5m, Ratio(BetSelection.Inside(BetType.SixLine, 1, 2, 3, 4, 5, 6)));
            Assert.AreEqual(2m, Ratio(BetSelection.Outside(BetType.Dozen, 2)));
            Assert.AreEqual(2m, Ratio(BetSelection.Outside(BetType.Column, 0)));
            foreach (BetType t in new[] { BetType.Red, BetType.Black, BetType.Odd, BetType.Even, BetType.Low, BetType.High })
            {
                Assert.AreEqual(1m, Ratio(BetSelection.Outside(t)), t.ToString());
            }
        }

        [Test]
        public void Payouts_EveryLayoutSpot_HasTheSameExpectedReturn()
        {
            // With honest payouts every bet returns (1 - edge) per dollar on the standard wheel.
            var policy = PayoutPolicy.Standard;
            foreach (BetSpot spot in Layout.Spots)
            {
                decimal ratio = policy.PayoutRatio(European, spot.Selection, 36);
                double p = WheelOdds.Probability(European, x => spot.Selection.Covers(x, 36));
                Assert.AreEqual(36.0 / 37.0, p * (double)(ratio + 1m), 1e-9, spot.Selection.Key);
            }
        }

        [Test]
        public void Payouts_AdaptToClonedNumbers()
        {
            // A wheel where 13 sits in 4 of 40 pockets: a straight on 13 is now 4x likelier and must pay less.
            var pockets = European.Pockets.ToList();
            for (int i = 0; i < 3; i++) pockets.Add(new Pocket(100 + i, 13, PocketColor.Black));
            var wheel = new WheelConfiguration("13 cloned", pockets);
            var policy = PayoutPolicy.Standard;
            decimal thirteen = policy.PayoutRatio(wheel, BetSelection.Straight(13), 36);
            decimal seventeen = policy.PayoutRatio(wheel, BetSelection.Straight(17), 36);
            Assert.Less(thirteen, seventeen);
            // (36/37) * 40 / 4 - 1 = 8.7297... -> 8.73
            Assert.AreEqual(8.73m, thirteen);
        }

        [Test]
        public void Payouts_ProfitRoundsDownForTheHouse()
        {
            var policy = PayoutPolicy.Standard;
            Assert.AreEqual(87, policy.Profit(10, 8.73m));
            Assert.AreEqual(350, policy.Profit(10, 35m));
            Assert.AreEqual(0, policy.Profit(10, 0m));
        }

        [Test]
        public void Payouts_UnwinnableBetPaysNothing()
        {
            var noSeventeen = new WheelConfiguration("no 17", European.Pockets.Select(p => p.Number == 17 ? p.WithActive(false) : p));
            Assert.AreEqual(0m, PayoutPolicy.Standard.PayoutRatio(noSeventeen, BetSelection.Straight(17), 36));
        }

        [Test]
        public void BetBook_StacksAndRemoves()
        {
            var book = new BetBook();
            var red = BetSelection.Outside(BetType.Red);
            book.Add(red, 25);
            book.Add(red, 25);
            book.Add(BetSelection.Straight(7), 10);
            Assert.AreEqual(2, book.Count);
            Assert.AreEqual(50, book.AmountOn(red));
            Assert.AreEqual(60, book.Total);
            Assert.AreEqual(50, book.Remove(red, 500));
            Assert.AreEqual(1, book.Count);
            Assert.AreEqual(10, book.Total);
        }
    }
}

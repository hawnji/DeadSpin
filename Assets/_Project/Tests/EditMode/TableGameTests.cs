using System.Linq;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Common;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class TableGameTests
    {
        private static TableGame NewGame(long balance = 2500, ulong seed = 7)
        {
            var config = RunConfig.CreateDefault();
            config.StartingBalance = balance;
            config.AnnouncementSeconds = 0f;
            config.SummarySeconds = 0f;
            config.Seed = seed;
            return new TableGame(new Run(config), StandardWheels.European());
        }

        private static TableGame OpenBetting(long balance = 2500, ulong seed = 7)
        {
            TableGame game = NewGame(balance, seed);
            Assert.IsTrue(game.TryBeginRound());
            Assert.IsTrue(game.Run.Round.IsBettingOpen);
            return game;
        }

        private static RoundResult Spin(TableGame game)
        {
            if (game.Run.Round.IsBettingOpen) game.TryCloseBetting();
            game.StartSpin();
            return game.FinishSpin();
        }

        [Test]
        public void PlaceBet_MovesMoneyToTheTable()
        {
            TableGame game = OpenBetting();
            Assert.AreEqual(PlacementResult.Accepted, game.PlaceBet(BetSelection.Outside(BetType.Red), 100));
            Assert.AreEqual(2400, game.Run.Ledger.Balance);
            Assert.AreEqual(100, game.Bets.Total);
            Assert.AreEqual(100, game.Run.MoneyAtRisk);
        }

        [Test]
        public void PlaceBet_Validation()
        {
            TableGame closed = NewGame();
            Assert.AreEqual(PlacementResult.BettingClosed, closed.PlaceBet(BetSelection.Straight(5), 10));

            TableGame game = OpenBetting(balance: 200);
            Assert.AreEqual(PlacementResult.BelowMinimum, game.PlaceBet(BetSelection.Straight(5), 5));
            Assert.AreEqual(PlacementResult.InvalidAmount, game.PlaceBet(BetSelection.Straight(5), 0));
            Assert.AreEqual(PlacementResult.InsufficientFunds, game.PlaceBet(BetSelection.Straight(5), 500));
            Assert.AreEqual(PlacementResult.IllegalBet, game.PlaceBet(BetSelection.Inside(BetType.Split, 3, 4), 10));
            Assert.AreEqual(200, game.Run.Ledger.Balance, "Rejected bets change nothing.");
        }

        [Test]
        public void PlaceBet_EnforcesMaximumPerSpot()
        {
            TableGame game = OpenBetting(balance: 50000);
            var red = BetSelection.Outside(BetType.Red);
            Assert.AreEqual(PlacementResult.Accepted, game.PlaceBet(red, 10000));
            Assert.AreEqual(PlacementResult.AboveMaximum, game.PlaceBet(red, 10));
            Assert.AreEqual(PlacementResult.Accepted, game.PlaceBet(BetSelection.Outside(BetType.Black), 10000));
        }

        [Test]
        public void PlaceBet_SecondChipMeetsMinimumAsATotal()
        {
            var config = RunConfig.CreateDefault();
            config.AnnouncementSeconds = 0f;
            config.MinimumBet = 25;
            var game = new TableGame(new Run(config), StandardWheels.European());
            game.TryBeginRound();
            Assert.AreEqual(PlacementResult.BelowMinimum, game.PlaceBet(BetSelection.Straight(9), 10));
            Assert.AreEqual(PlacementResult.Accepted, game.PlaceBet(BetSelection.Straight(9), 25));
            Assert.AreEqual(PlacementResult.Accepted, game.PlaceBet(BetSelection.Straight(9), 10), "35 total is above the minimum.");
        }

        [Test]
        public void RemoveAndClear_RefundWhileBettingIsOpen()
        {
            TableGame game = OpenBetting();
            var red = BetSelection.Outside(BetType.Red);
            game.PlaceBet(red, 100);
            game.PlaceBet(BetSelection.Straight(3), 50);
            Assert.AreEqual(PlacementResult.Accepted, game.RemoveBet(red, 95));
            Assert.AreEqual(0, game.Bets.AmountOn(red), "Leaving 5 would be below the minimum, so the spot clears.");
            Assert.IsTrue(game.ClearBets());
            Assert.AreEqual(2500, game.Run.Ledger.Balance);
            Assert.AreEqual(0, game.Run.MoneyAtRisk);
            Assert.AreEqual(PlacementResult.NothingToRemove, game.RemoveBet(red, 10));
        }

        [Test]
        public void BetsAreLockedOnceBettingCloses()
        {
            TableGame game = OpenBetting();
            game.PlaceBet(BetSelection.Straight(3), 50);
            game.TryCloseBetting();
            Assert.AreEqual(PlacementResult.BettingClosed, game.RemoveBet(BetSelection.Straight(3), 50));
            Assert.IsFalse(game.ClearBets());
            Assert.AreEqual(PlacementResult.BettingClosed, game.PlaceBet(BetSelection.Straight(4), 10));
        }

        [Test]
        public void SpinProtocol_RevealsResultOnlyAfterFinish()
        {
            TableGame game = OpenBetting();
            game.PlaceBet(BetSelection.Outside(BetType.Red), 10);
            game.TryCloseBetting();
            int target = game.StartSpin().PocketIndices[0];
            Assert.AreEqual(RoundPhase.Spinning, game.Run.Round.Phase);
            Assert.IsNull(game.LastResult, "Nothing is revealed while the ball is moving.");
            Assert.Throws<System.InvalidOperationException>(() => game.StartSpin());
            RoundResult result = game.FinishSpin();
            Assert.AreEqual(target, result.PocketIndex);
            Assert.AreSame(result, game.LastResult);
            Assert.AreEqual(result.Pocket.Number, game.History[0]);
            Assert.AreEqual(1, game.Run.RoundsPlayed);
        }

        [Test]
        public void Settlement_PaysWinnersAndRecordsLosers()
        {
            TableGame game = OpenBetting(seed: 11);
            // Cover every number with straights: exactly one wins 35 to 1.
            for (int n = 0; n <= 36; n++) game.PlaceBet(BetSelection.Straight(n), 10);
            Assert.AreEqual(2500 - 370, game.Run.Ledger.Balance);

            RoundResult result = Spin(game);
            Assert.AreEqual(1, result.Bets.Count(b => b.Won));
            BetOutcome winner = result.Bets.Single(b => b.Won);
            Assert.AreEqual(result.Pocket.Number, winner.Selection.Numbers[0]);
            Assert.AreEqual(350, winner.Profit);
            Assert.AreEqual(370, result.TotalStaked);
            Assert.AreEqual(360, result.TotalReturned);
            Assert.AreEqual(2490, game.Run.Ledger.Balance);
            Assert.AreEqual(350, game.Run.Ledger.TotalWinnings);
            Assert.AreEqual(360, game.Run.Ledger.TotalLosses);
            Assert.AreEqual(0, game.Bets.Count);
            Assert.AreEqual(0, game.Run.MoneyAtRisk);
        }

        [Test]
        public void SeededRuns_ProduceTheSameResults()
        {
            TableGame a = OpenBetting(seed: 1234);
            TableGame b = OpenBetting(seed: 1234);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(Spin(a).Pocket.Number, Spin(b).Pocket.Number);
                a.TryBeginRound();
                b.TryBeginRound();
            }
        }

        [Test]
        public void LosingEverything_IsBankruptcyAfterTheSpin()
        {
            // Find a seed whose first spin is not 17, then bet the whole balance on 17.
            for (ulong seed = 1; seed < 100; seed++)
            {
                TableGame game = OpenBetting(balance: 100, seed: seed);
                game.PlaceBet(BetSelection.Straight(17), 100);
                Assert.AreEqual(RunOutcome.InProgress, game.Run.Outcome, "Chips on the table keep the run alive.");
                RoundResult result = Spin(game);
                if (result.Pocket.Number == 17) continue;
                Assert.AreEqual(RunOutcome.Bankrupt, game.Run.Outcome);
                return;
            }
            Assert.Fail("No losing seed found.");
        }

        [Test]
        public void WheelChange_DuringBetting_RefundsBetsThatCanNoLongerWin()
        {
            TableGame game = OpenBetting();
            game.PlaceBet(BetSelection.Straight(17), 50);
            game.PlaceBet(BetSelection.Outside(BetType.Red), 40);
            WheelChange change = WheelTransforms.RemoveNumber(game.Wheel, 17);
            Assert.IsTrue(game.ApplyWheelChange(change), "Live transformations are allowed while betting.");
            Assert.AreEqual(0, game.Bets.AmountOn(BetSelection.Straight(17)), "The bet on the removed number is refunded.");
            Assert.AreEqual(40, game.Bets.Total);
            Assert.AreEqual(2500 - 40, game.Run.Ledger.Balance);

            game.TryCloseBetting();
            game.StartSpin();
            Assert.IsFalse(game.TrySetWheel(StandardWheels.European()), "Never while the ball is moving.");
        }

        [Test]
        public void LongSimulation_ReturnToPlayerMatchesTheEdge()
        {
            // 20,000 seeded rounds of $10 on red: return should be close to 36/37 of the stake.
            var config = RunConfig.CreateDefault();
            config.AnnouncementSeconds = 0f;
            config.SummarySeconds = 0f;
            config.TotalRounds = 1000000;
            config.StartingBalance = 10000000;
            config.Seed = 42;
            var game = new TableGame(new Run(config), StandardWheels.European());
            long staked = 0, returned = 0;
            for (int i = 0; i < 20000; i++)
            {
                game.TryBeginRound();
                game.PlaceBet(BetSelection.Outside(BetType.Red), 10);
                RoundResult r = Spin(game);
                staked += r.TotalStaked;
                returned += r.TotalReturned;
            }
            double rtp = (double)returned / staked;
            // Standard error of RTP for even-money bets over 20k spins is about 0.007.
            Assert.AreEqual(36.0 / 37.0, rtp, 0.025, "Return to player " + rtp);
        }
    }
}

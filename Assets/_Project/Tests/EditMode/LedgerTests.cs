using System;
using DeadSpin.Core.Economy;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class LedgerTests
    {
        [Test]
        public void StartsWithConfiguredValues_AndLogsStart()
        {
            var ledger = new Ledger(2500, 50000);
            Assert.AreEqual(2500, ledger.Balance);
            Assert.AreEqual(50000, ledger.Debt);
            Assert.AreEqual(1, ledger.History.Count);
            Assert.AreEqual(TransactionKind.StartingFunds, ledger.History[0].Kind);
        }

        [Test]
        public void TryDebit_FailsWithoutChanges_WhenFundsInsufficient()
        {
            var ledger = new Ledger(100, 0);
            Assert.IsFalse(ledger.TryDebit(101, TransactionKind.WagerPlaced, "too big"));
            Assert.AreEqual(100, ledger.Balance);
            Assert.AreEqual(1, ledger.History.Count);
        }

        [Test]
        public void TryDebit_AllowsSpendingExactBalance()
        {
            var ledger = new Ledger(100, 0);
            Assert.IsTrue(ledger.TryDebit(100, TransactionKind.WagerPlaced, "all in"));
            Assert.AreEqual(0, ledger.Balance);
            Assert.AreEqual(0, ledger.TotalLosses, "A wager is not a loss until it is resolved.");
        }

        [Test]
        public void RecordWagerLost_CountsLossWithoutMovingMoney()
        {
            var ledger = new Ledger(100, 0);
            ledger.TryDebit(40, TransactionKind.WagerPlaced, "red");
            ledger.RecordWagerLost(40, "red on 17");
            Assert.AreEqual(60, ledger.Balance);
            Assert.AreEqual(40, ledger.TotalLosses);
            Assert.AreEqual(TransactionKind.WagerLost, ledger.History[ledger.History.Count - 1].Kind);
        }

        [Test]
        public void Credit_Winnings_TracksTotals()
        {
            var ledger = new Ledger(100, 0);
            ledger.Credit(350, TransactionKind.Winnings, "straight up");
            Assert.AreEqual(450, ledger.Balance);
            Assert.AreEqual(350, ledger.TotalWinnings);
        }

        [Test]
        public void ForceDebit_NeverGoesNegative()
        {
            var ledger = new Ledger(50, 0);
            long taken = ledger.ForceDebit(80, TransactionKind.Penalty, "skull");
            Assert.AreEqual(50, taken);
            Assert.AreEqual(0, ledger.Balance);
            Assert.AreEqual(0, ledger.ForceDebit(10, TransactionKind.Penalty, "nothing left"));
        }

        [Test]
        public void PayDebt_ClampsToDebtAndBalance()
        {
            var ledger = new Ledger(1000, 300);
            Assert.AreEqual(300, ledger.PayDebt(500, "overpay"));
            Assert.AreEqual(700, ledger.Balance);
            Assert.AreEqual(0, ledger.Debt);

            var poor = new Ledger(200, 1000);
            Assert.AreEqual(200, poor.PayDebt(500, "partial"));
            Assert.AreEqual(0, poor.Balance);
            Assert.AreEqual(800, poor.Debt);
        }

        [Test]
        public void IncreaseAndForgiveDebt()
        {
            var ledger = new Ledger(0, 1000);
            ledger.IncreaseDebt(10000, "generous offer");
            Assert.AreEqual(11000, ledger.Debt);
            Assert.AreEqual(11000, ledger.ForgiveDebt(20000, "debt relief"));
            Assert.AreEqual(0, ledger.Debt);
        }

        [Test]
        public void NonPositiveAmounts_Throw()
        {
            var ledger = new Ledger(100, 100);
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Credit(0, TransactionKind.Reward, "zero"));
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.TryDebit(-5, TransactionKind.Fee, "negative"));
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.IncreaseDebt(0, "zero"));
        }

        [Test]
        public void Changed_FiresWithRoundStamp()
        {
            var ledger = new Ledger(100, 0);
            ledger.CurrentRound = 4;
            Transaction? seen = null;
            ledger.Changed += t => seen = t;
            ledger.Credit(10, TransactionKind.Reward, "test");
            Assert.IsTrue(seen.HasValue);
            Assert.AreEqual(4, seen.Value.Round);
            Assert.AreEqual(110, seen.Value.BalanceAfter);
        }
    }
}

using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Economy
{
    /// <summary>
    /// THE single source of truth for the player's money and debt. No other system stores a
    /// balance; everything reads from and writes through this class so the HUD, the Game Master
    /// and the outcome rules can never disagree.
    /// Amounts are whole in-game dollars. Every mutation is validated and logged.
    /// </summary>
    public sealed class Ledger
    {
        private readonly List<Transaction> _history = new List<Transaction>();

        public long Balance { get; private set; }
        public long Debt { get; private set; }
        public long TotalWinnings { get; private set; }
        public long TotalLosses { get; private set; }

        /// <summary>Winnings stored in the Vault: safe from taxes and fines, but not available for betting until withdrawn.</summary>
        public long Stored { get; private set; }

        /// <summary>Round number stamped onto new transactions. Set by the run.</summary>
        public int CurrentRound { get; set; }

        public IReadOnlyList<Transaction> History => _history;

        /// <summary>Raised after every successful change.</summary>
        public event Action<Transaction> Changed;

        public Ledger(long startingBalance, long startingDebt)
        {
            if (startingBalance < 0) throw new ArgumentOutOfRangeException(nameof(startingBalance));
            if (startingDebt < 0) throw new ArgumentOutOfRangeException(nameof(startingDebt));
            Balance = startingBalance;
            Debt = startingDebt;
            Record(TransactionKind.StartingFunds, startingBalance, startingDebt, "Run start");
        }

        public bool CanAfford(long amount)
        {
            return amount >= 0 && Balance >= amount;
        }

        /// <summary>Adds money to the balance.</summary>
        public void Credit(long amount, TransactionKind kind, string reason)
        {
            RequirePositive(amount);
            Balance = checked(Balance + amount);
            if (kind == TransactionKind.Winnings) TotalWinnings = checked(TotalWinnings + amount);
            Record(kind, amount, 0, reason);
        }

        /// <summary>Removes money if the player can afford it. Returns false and changes nothing otherwise.</summary>
        public bool TryDebit(long amount, TransactionKind kind, string reason)
        {
            RequirePositive(amount);
            if (Balance < amount) return false;
            Balance -= amount;
            // Wagers are not losses until they are resolved; see RecordWagerLost.
            if (kind == TransactionKind.Penalty || kind == TransactionKind.Fee)
            {
                TotalLosses = checked(TotalLosses + amount);
            }
            Record(kind, -amount, 0, reason);
            return true;
        }

        /// <summary>
        /// Takes up to <paramref name="amount"/>, never driving the balance below zero.
        /// Used for unavoidable penalties (house taxes, skull pockets). Returns the amount actually taken.
        /// </summary>
        public long ForceDebit(long amount, TransactionKind kind, string reason)
        {
            RequirePositive(amount);
            long taken = Math.Min(amount, Balance);
            if (taken == 0) return 0;
            Balance -= taken;
            TotalLosses = checked(TotalLosses + taken);
            Record(kind, -taken, 0, reason);
            return taken;
        }

        /// <summary>Moves money from the balance to the debt, clamped to both the outstanding debt and the available balance. Returns the amount paid.</summary>
        public long PayDebt(long amount, string reason)
        {
            RequirePositive(amount);
            long payment = Math.Min(amount, Math.Min(Debt, Balance));
            if (payment == 0) return 0;
            Balance -= payment;
            Debt -= payment;
            Record(TransactionKind.DebtPayment, -payment, -payment, reason);
            return payment;
        }

        /// <summary>
        /// Logs a lost wager whose money already left the balance when it was placed. No balance
        /// change; updates loss statistics used by the end screen and (later) the Game Master.
        /// </summary>
        public void RecordWagerLost(long amount, string reason)
        {
            RequirePositive(amount);
            TotalLosses = checked(TotalLosses + amount);
            Record(TransactionKind.WagerLost, 0, 0, reason);
        }

        public void IncreaseDebt(long amount, string reason)
        {
            RequirePositive(amount);
            Debt = checked(Debt + amount);
            Record(TransactionKind.DebtIncrease, 0, amount, reason);
        }

        /// <summary>Reduces debt without spending balance (debt relief pockets, contracts). Returns the amount forgiven.</summary>
        public long ForgiveDebt(long amount, string reason)
        {
            RequirePositive(amount);
            long relief = Math.Min(amount, Debt);
            if (relief == 0) return 0;
            Debt -= relief;
            Record(TransactionKind.DebtRelief, 0, -relief, reason);
            return relief;
        }

        /// <summary>Moves money from the balance into the Vault. Returns the amount stored.</summary>
        public long Deposit(long amount)
        {
            RequirePositive(amount);
            long moved = Math.Min(amount, Balance);
            if (moved == 0) return 0;
            Balance -= moved;
            Stored = checked(Stored + moved);
            Record(TransactionKind.VaultDeposit, -moved, 0, "Vault deposit");
            return moved;
        }

        /// <summary>Moves money from the Vault back to the balance. Returns the amount withdrawn.</summary>
        public long Withdraw(long amount)
        {
            RequirePositive(amount);
            long moved = Math.Min(amount, Stored);
            if (moved == 0) return 0;
            Stored -= moved;
            Balance = checked(Balance + moved);
            Record(TransactionKind.VaultWithdrawal, moved, 0, "Vault withdrawal");
            return moved;
        }

        /// <summary>Pays debt straight from the Vault. Returns the amount paid.</summary>
        public long PayDebtFromVault(long amount, string reason)
        {
            RequirePositive(amount);
            long payment = Math.Min(amount, Math.Min(Debt, Stored));
            if (payment == 0) return 0;
            Stored -= payment;
            Debt -= payment;
            Record(TransactionKind.DebtPayment, 0, -payment, reason);
            return payment;
        }

        /// <summary>Restores totals from a save. The history starts again from this point.</summary>
        public void Restore(long balance, long debt, long stored, long totalWinnings, long totalLosses)
        {
            if (balance < 0 || debt < 0 || stored < 0) throw new ArgumentOutOfRangeException(nameof(balance), "Saved money values cannot be negative.");
            Balance = balance;
            Debt = debt;
            Stored = stored;
            TotalWinnings = totalWinnings;
            TotalLosses = totalLosses;
            _history.Clear();
            Record(TransactionKind.StartingFunds, 0, 0, "Restored from save");
        }

        private void Record(TransactionKind kind, long balanceDelta, long debtDelta, string reason)
        {
            var transaction = new Transaction(CurrentRound, kind, balanceDelta, debtDelta, Balance, Debt, reason);
            _history.Add(transaction);
            Changed?.Invoke(transaction);
        }

        private static void RequirePositive(long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
        }
    }
}

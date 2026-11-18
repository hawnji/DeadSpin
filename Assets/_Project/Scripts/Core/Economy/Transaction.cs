namespace DeadSpin.Core.Economy
{
    /// <summary>One immutable entry in the ledger's history.</summary>
    public readonly struct Transaction
    {
        public readonly int Round;
        public readonly TransactionKind Kind;
        /// <summary>Signed change to the balance (negative = money left the player).</summary>
        public readonly long BalanceDelta;
        /// <summary>Signed change to the debt (negative = debt reduced).</summary>
        public readonly long DebtDelta;
        public readonly long BalanceAfter;
        public readonly long DebtAfter;
        public readonly string Reason;

        public Transaction(int round, TransactionKind kind, long balanceDelta, long debtDelta, long balanceAfter, long debtAfter, string reason)
        {
            Round = round;
            Kind = kind;
            BalanceDelta = balanceDelta;
            DebtDelta = debtDelta;
            BalanceAfter = balanceAfter;
            DebtAfter = debtAfter;
            Reason = reason ?? string.Empty;
        }

        public override string ToString()
        {
            return "[R" + Round + "] " + Kind + " bal " + BalanceDelta.ToString("+#;-#;0") + " debt " + DebtDelta.ToString("+#;-#;0") + " (" + Reason + ")";
        }
    }
}

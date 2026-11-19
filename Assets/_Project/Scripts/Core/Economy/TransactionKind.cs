namespace DeadSpin.Core.Economy
{
    /// <summary>Why money moved. Used for the transaction log, statistics and Game Master analysis.</summary>
    public enum TransactionKind
    {
        StartingFunds,
        WagerPlaced,
        WagerRefunded,
        /// <summary>A winning wager's stake coming back to the player.</summary>
        WagerReturned,
        /// <summary>Record-only entry (no balance change): a resolved wager that lost.</summary>
        WagerLost,
        Winnings,
        Penalty,
        Reward,
        Purchase,
        DebtPayment,
        DebtIncrease,
        DebtRelief,
        ContractPayout,
        Fee
    }
}

using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Economy
{
    /// <summary>One required debt payment: by the end of <see cref="DueRound"/>, at least <see cref="CumulativeAmount"/> must have been paid in total.</summary>
    public sealed class Installment
    {
        public int Stage { get; }
        public int DueRound { get; }
        public long CumulativeAmount { get; }
        public bool Settled { get; internal set; }
        public bool Missed { get; internal set; }

        public Installment(int stage, int dueRound, long cumulativeAmount)
        {
            Stage = stage;
            DueRound = dueRound;
            CumulativeAmount = cumulativeAmount;
        }
    }

    public enum InstallmentResult
    {
        NotDue,
        AlreadyPaid,
        Collected,
        /// <summary>Missed, and the one grace was used: a late fee was added to the debt.</summary>
        Grace,
        /// <summary>Missed with no grace left: bankruptcy.</summary>
        Default
    }

    /// <summary>
    /// MANDATORY PAYMENTS. From stage 2 on, the house expects part of the debt paid by the end of each
    /// stage (2%, 5% and 10% of the starting debt, cumulative). Payments made in the Vault count
    /// toward the requirement; whatever is still due is taken automatically from the balance at the
    /// deadline if the balance covers it. The first missed installment adds a 50% late fee to the
    /// debt (the one grace); a second one is bankruptcy.
    /// </summary>
    public sealed class PaymentSchedule
    {
        private readonly List<Installment> _installments = new List<Installment>();

        public IReadOnlyList<Installment> Installments => _installments;
        /// <summary>Debt paid with the player's own balance so far (not forgiven or frozen debt).</summary>
        public long TotalPaid { get; private set; }
        public int MissedCount { get; private set; }
        public int GraceAllowed { get; set; } = 1;
        public int LateFeePercent { get; set; } = 50;
        public bool Defaulted => MissedCount > GraceAllowed;

        /// <summary>Fractions of the starting debt due by the end of stages 2, 3 and 4 (cumulative).</summary>
        public static readonly double[] DefaultFractions = { 0.0, 0.02, 0.05, 0.10 };

        public PaymentSchedule() { }

        /// <summary>Builds the schedule from the stage end rounds and the starting debt.</summary>
        public static PaymentSchedule Create(long startingDebt, IReadOnlyList<int> stageEndRounds, double[] fractions = null)
        {
            fractions = fractions ?? DefaultFractions;
            var s = new PaymentSchedule();
            for (int stage = 1; stage <= stageEndRounds.Count && stage <= fractions.Length; stage++)
            {
                long amount = (long)(startingDebt * fractions[stage - 1]) / 100 * 100;
                if (amount <= 0) continue;
                s._installments.Add(new Installment(stage, stageEndRounds[stage - 1], amount));
            }
            return s;
        }

        public Installment Next
        {
            get
            {
                foreach (Installment i in _installments) if (!i.Settled && !i.Missed) return i;
                return null;
            }
        }

        /// <summary>Amount still owed for the next installment (0 if covered).</summary>
        public long OutstandingFor(Installment installment)
        {
            return installment == null ? 0 : Math.Max(0, installment.CumulativeAmount - TotalPaid);
        }

        /// <summary>Records a voluntary payment made through the Ledger.</summary>
        public void RecordPayment(long amount)
        {
            if (amount > 0) TotalPaid += amount;
        }

        /// <summary>Called at the end of every round. Collects an installment due this round.</summary>
        public InstallmentResult Collect(int roundJustPlayed, Ledger ledger, Func<long, string, long> increaseDebt, out long amount)
        {
            amount = 0;
            Installment due = null;
            foreach (Installment i in _installments) if (!i.Settled && !i.Missed && i.DueRound <= roundJustPlayed) { due = i; break; }
            if (due == null) return InstallmentResult.NotDue;

            long owed = OutstandingFor(due);
            if (owed <= 0 || ledger.Debt <= 0)
            {
                due.Settled = true;
                return InstallmentResult.AlreadyPaid;
            }
            owed = Math.Min(owed, ledger.Debt);
            if (ledger.Balance >= owed)
            {
                amount = ledger.PayDebt(owed, "Installment (stage " + due.Stage + ")");
                TotalPaid += amount;
                due.Settled = true;
                return InstallmentResult.Collected;
            }

            // A missed installment takes nothing (the player keeps a bankroll to recover with); the
            // first miss adds a late fee to the debt, the next one is a default.
            due.Missed = true;
            MissedCount++;
            if (MissedCount <= GraceAllowed)
            {
                long fee = Math.Max(100, owed * LateFeePercent / 100);
                increaseDebt(fee, "Late fee (stage " + due.Stage + " installment)");
                return InstallmentResult.Grace;
            }
            return InstallmentResult.Default;
        }

        public void Restore(long totalPaid, int missed, IEnumerable<int> settledStages, IEnumerable<int> missedStages)
        {
            TotalPaid = totalPaid;
            MissedCount = missed;
            var settled = new HashSet<int>(settledStages ?? new int[0]);
            var miss = new HashSet<int>(missedStages ?? new int[0]);
            foreach (Installment i in _installments)
            {
                i.Settled = settled.Contains(i.Stage);
                i.Missed = miss.Contains(i.Stage);
            }
        }
    }

    public enum WarningLevel
    {
        None,
        Caution,
        Danger,
        Critical
    }

    /// <summary>One line of the bankruptcy forecast shown on the HUD.</summary>
    public sealed class FinancialWarning
    {
        public WarningLevel Level { get; }
        public string Text { get; }

        public FinancialWarning(WarningLevel level, string text)
        {
            Level = level;
            Text = text;
        }
    }

    /// <summary>
    /// Clear communication when bankruptcy is approaching: looks at the balance, the minimum bet, the
    /// mandatory wager, the next installment and the pace needed to clear the debt in time.
    /// </summary>
    public static class BankruptcyForecast
    {
        public static List<FinancialWarning> Evaluate(long balance, long debt, long minimumBet, long mandatoryWager,
                                                      PaymentSchedule schedule, int currentRound, int roundsRemaining)
        {
            var warnings = new List<FinancialWarning>();
            if (debt <= 0) return warnings;

            long floor = Math.Max(minimumBet, mandatoryWager);
            if (floor > 0)
            {
                long betsLeft = balance / floor;
                if (betsLeft < 1) warnings.Add(new FinancialWarning(WarningLevel.Critical, "You cannot cover the " + (mandatoryWager > minimumBet ? "mandatory wager" : "minimum bet") + ". Win this spin or it is over."));
                else if (betsLeft <= 3) warnings.Add(new FinancialWarning(WarningLevel.Danger, "Only " + betsLeft + " minimum bet" + (betsLeft == 1 ? "" : "s") + " left in your balance."));
                else if (betsLeft <= 8) warnings.Add(new FinancialWarning(WarningLevel.Caution, "Your balance covers " + betsLeft + " minimum bets."));
            }

            if (schedule != null)
            {
                Installment next = schedule.Next;
                long owed = schedule.OutstandingFor(next);
                if (next != null && owed > 0)
                {
                    int roundsToDue = next.DueRound - currentRound + 1;
                    WarningLevel level = balance < owed ? (roundsToDue <= 1 ? WarningLevel.Critical : WarningLevel.Danger) : roundsToDue <= 1 ? WarningLevel.Caution : WarningLevel.None;
                    if (level != WarningLevel.None)
                    {
                        warnings.Add(new FinancialWarning(level, "Installment of $" + owed.ToString("N0") + " due after round " + next.DueRound
                            + (schedule.MissedCount >= schedule.GraceAllowed ? ". Missing it means bankruptcy." : ". Missing it adds a late fee.")));
                    }
                }
            }

            if (roundsRemaining > 0)
            {
                long needed = Math.Max(0, debt - balance);
                long perRound = needed / roundsRemaining;
                if (needed > 0 && perRound > Math.Max(balance, 1) * 2) warnings.Add(new FinancialWarning(WarningLevel.Caution, "You need about $" + perRound.ToString("N0") + " per round to clear the debt in time."));
            }
            return warnings;
        }

        public static WarningLevel Worst(IEnumerable<FinancialWarning> warnings)
        {
            WarningLevel worst = WarningLevel.None;
            foreach (FinancialWarning w in warnings) if (w.Level > worst) worst = w.Level;
            return worst;
        }
    }
}

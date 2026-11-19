using System;
using System.Collections.Generic;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Rules;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.Economy
{
    public enum ContractStatus
    {
        Pending,
        Fulfilled,
        Breached
    }

    /// <summary>
    /// A deal with the house. Each contract is a small class: availability, what happens when it is
    /// signed, what it does to the next spin, and how it resolves. New contracts subclass this and
    /// register in <see cref="ContractCatalog"/>.
    /// </summary>
    public abstract class ContractDefinition
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        /// <summary>The terms in plain words, shown before signing.</summary>
        public abstract string Terms(IGameHost host);
        public virtual int MinStage => 1;
        /// <summary>Only offered by special sources (the Unlucky Guest, the final encounter), never in the Vault.</summary>
        public virtual bool Exclusive => false;

        public virtual bool IsAvailable(IGameHost host) { return host.Stage >= MinStage; }

        /// <summary>Applied when the player signs.</summary>
        public abstract void OnAccept(ContractInstance contract, IGameHost host);

        /// <summary>Called as the spin starts, with every bet locked in (insurance, wagers).</summary>
        public virtual void OnSpinStarting(ContractInstance contract, IGameHost host) { }

        /// <summary>Called after each resolved spin while pending. Return the new status.</summary>
        public virtual ContractStatus OnRoundResolved(ContractInstance contract, RoundResult result, IGameHost host) { return ContractStatus.Fulfilled; }

        /// <summary>Called at the end of each round while pending (installments, deadlines).</summary>
        public virtual ContractStatus OnRoundEnded(ContractInstance contract, IGameHost host) { return contract.Status; }
    }

    /// <summary>A signed contract.</summary>
    public sealed class ContractInstance
    {
        public ContractDefinition Definition { get; }
        public int SignedRound { get; }
        public ContractStatus Status { get; set; } = ContractStatus.Pending;
        /// <summary>Money still owed on loans.</summary>
        public long Outstanding { get; set; }
        public int RoundsLeft { get; set; }

        public ContractInstance(ContractDefinition definition, int signedRound)
        {
            Definition = definition;
            SignedRound = signedRound;
        }
    }

    // ------------------------------------------------------------------ the contracts

    /// <summary>THE GENEROUS OFFER: $5,000 now, debt +$10,000.</summary>
    public sealed class GenerousOffer : ContractDefinition
    {
        public override string Id => "generous_offer";
        public override string Name => "The Generous Offer";
        public override string Terms(IGameHost host) { return "Receive $5,000 now. Your debt increases by $10,000."; }

        public override void OnAccept(ContractInstance contract, IGameHost host)
        {
            host.Ledger.Credit(5000, TransactionKind.ContractPayout, Name);
            long added = host.IncreaseDebt(10000, Name);
            if (added == 0) host.RecordLoophole("debt_freeze", "Signed a debt contract under a Debt Freeze: the debt never arrived.");
            contract.Status = ContractStatus.Fulfilled;
        }
    }

    /// <summary>DOUBLE OR NOTHING: win the next spin and $10,000 of debt disappears; lose and the debt grows by $15,000.</summary>
    public sealed class DoubleOrNothing : ContractDefinition
    {
        public override string Id => "double_or_nothing";
        public override string Name => "Double or Nothing";
        public override string Terms(IGameHost host) { return "Come out ahead on the next spin and $10,000 of debt is removed. Lose (or sit out) and your debt grows by $15,000."; }

        public override void OnAccept(ContractInstance contract, IGameHost host) { contract.RoundsLeft = 1; }

        public override ContractStatus OnRoundResolved(ContractInstance contract, RoundResult result, IGameHost host)
        {
            if (result.Net > 0)
            {
                host.Ledger.ForgiveDebt(10000, Name);
                host.Notify("Double or Nothing: $10,000 of debt removed.");
                return ContractStatus.Fulfilled;
            }
            long added = host.IncreaseDebt(15000, Name);
            host.Notify(added > 0 ? "Double or Nothing: your debt grows by $15,000." : "Double or Nothing lost, but the Debt Freeze held.");
            if (added == 0) host.RecordLoophole("debt_freeze", "Lost Double or Nothing under a Debt Freeze.");
            return ContractStatus.Breached;
        }
    }

    /// <summary>INSURANCE: pay a fee to protect half of every stake on the next spin.</summary>
    public sealed class InsuranceContract : ContractDefinition
    {
        public const int FeePercent = 10;
        public const int ProtectedPercent = 50;

        public override string Id => "insurance";
        public override string Name => "Insurance";

        public override string Terms(IGameHost host)
        {
            int fee = FeePercent * host.Table.Modifiers.InsuranceCostPercent / 100;
            return "When the next spin starts, pay " + fee + "% of your total wager. Every losing bet then returns " + ProtectedPercent + "% of its stake.";
        }

        public override void OnAccept(ContractInstance contract, IGameHost host) { contract.RoundsLeft = 1; }

        public override void OnSpinStarting(ContractInstance contract, IGameHost host)
        {
            long total = host.Table.Bets.Total;
            if (total <= 0) return;
            long fee = Math.Max(1, total * FeePercent * host.Table.Modifiers.InsuranceCostPercent / 10000);
            if (!host.Ledger.TryDebit(fee, TransactionKind.Fee, "Insurance premium"))
            {
                host.Notify("You could not afford the insurance premium. The policy is void.");
                return;
            }
            foreach (Betting.Wager w in host.Table.Bets.Wagers) host.Table.Insure(w.Selection, ProtectedPercent);
            host.Notify("Insured: losing bets return " + ProtectedPercent + "%.");
        }

        public override ContractStatus OnRoundResolved(ContractInstance contract, RoundResult result, IGameHost host) { return ContractStatus.Fulfilled; }
    }

    /// <summary>THE DEVIL'S BARGAIN: the most dangerous active rule is removed; the minimum bet rises by $10 for good.</summary>
    public sealed class DevilsBargain : ContractDefinition
    {
        public override string Id => "devils_bargain";
        public override string Name => "The Devil's Bargain";
        public override int MinStage => 2;

        public override string Terms(IGameHost host)
        {
            RuleInstance target = MostDangerous(host);
            return (target != null ? "The rule \"" + target.Definition.Name + "\" is lifted now." : "The worst active rule is lifted.") + " In exchange the minimum bet rises by $10 for the rest of the run.";
        }

        public override bool IsAvailable(IGameHost host) { return base.IsAvailable(host) && MostDangerous(host) != null; }

        private static RuleInstance MostDangerous(IGameHost host)
        {
            RuleInstance worst = null;
            foreach (RuleInstance r in host.Rules.Active)
            {
                if (!r.Definition.Negative || r.Definition.Permanent || !r.IsVisible) continue;
                if (worst == null || r.Definition.AuthorityCost > worst.Definition.AuthorityCost) worst = r;
            }
            return worst;
        }

        public override void OnAccept(ContractInstance contract, IGameHost host)
        {
            RuleInstance target = MostDangerous(host);
            if (target != null) host.Rules.Cancel(target.Id);
            host.RaiseBaseMinimumBet(10);
            contract.Status = ContractStatus.Fulfilled;
        }
    }

    /// <summary>THE LOAN SHARK: $2,000 now, $3,000 back within three rounds or the debt grows by $6,000.</summary>
    public sealed class LoanShark : ContractDefinition
    {
        public override string Id => "loan_shark";
        public override string Name => "The Loan Shark";
        public override int MinStage => 2;
        public override string Terms(IGameHost host) { return "Receive $2,000 now. $3,000 is taken from your balance at the end of the next three rounds as you can afford it. Anything unpaid after three rounds is added to your debt twice over."; }

        public override void OnAccept(ContractInstance contract, IGameHost host)
        {
            host.Ledger.Credit(2000, TransactionKind.ContractPayout, Name);
            contract.Outstanding = 3000;
            contract.RoundsLeft = 3;
        }

        public override ContractStatus OnRoundResolved(ContractInstance contract, RoundResult result, IGameHost host) { return ContractStatus.Pending; }

        public override ContractStatus OnRoundEnded(ContractInstance contract, IGameHost host)
        {
            long payment = Math.Min(contract.Outstanding, Math.Max(0, host.Ledger.Balance - host.Run.EffectiveMinimumBet));
            if (payment > 0 && host.Ledger.TryDebit(payment, TransactionKind.Fee, "Loan Shark repayment")) contract.Outstanding -= payment;
            if (contract.Outstanding <= 0) return ContractStatus.Fulfilled;
            contract.RoundsLeft--;
            if (contract.RoundsLeft > 0) return ContractStatus.Pending;
            long added = host.IncreaseDebt(contract.Outstanding * 2, "Unpaid loan");
            host.Notify("The Loan Shark adds $" + added.ToString("N0") + " to your debt.");
            return ContractStatus.Breached;
        }
    }

    /// <summary>THE STRANGER'S WAGER (Unlucky Guest only): pay $1,000; if the next ball lands on a zero or a special pocket, receive $15,000.</summary>
    public sealed class StrangersWager : ContractDefinition
    {
        public override string Id => "strangers_wager";
        public override string Name => "The Stranger's Wager";
        public override bool Exclusive => true;

        public override string Terms(IGameHost host)
        {
            double p = Wheel.WheelOdds.Probability(host.Table.Wheel, pk => pk.IsZero || pk.Special != Wheel.SpecialPocketType.None);
            return "Pay $1,000 now. If the next ball lands on a zero or a special pocket (" + (p * 100).ToString("0.0") + "% on this wheel), receive $15,000.";
        }

        public override bool IsAvailable(IGameHost host) { return host.Ledger.Balance >= 1000; }

        public override void OnAccept(ContractInstance contract, IGameHost host)
        {
            if (!host.Ledger.TryDebit(1000, TransactionKind.Purchase, Name)) contract.Status = ContractStatus.Breached;
        }

        public override ContractStatus OnRoundResolved(ContractInstance contract, RoundResult result, IGameHost host)
        {
            if (result.Pocket.IsZero || result.Pocket.Special != Wheel.SpecialPocketType.None)
            {
                host.Ledger.Credit(15000, TransactionKind.ContractPayout, Name);
                host.Notify("The Stranger pays $15,000.");
                return ContractStatus.Fulfilled;
            }
            return ContractStatus.Breached;
        }
    }

    /// <summary>HIS FINAL OFFER (final encounter only): sign and the debt is gone, but you work for the house forever.</summary>
    public sealed class FinalOffer : ContractDefinition
    {
        public override string Id => "final_offer";
        public override string Name => "His Final Offer";
        public override bool Exclusive => true;
        public override string Terms(IGameHost host) { return "Your debt is erased. You never leave. You deal the cards for the next guest. (Ends the run: The Deal.)"; }

        public override void OnAccept(ContractInstance contract, IGameHost host)
        {
            contract.Status = ContractStatus.Fulfilled;
            host.Run.ForceOutcome(RunOutcome.TheDeal);
        }
    }

    /// <summary>HIS ESCAPE CLAUSE (final encounter only): on the last spin every dollar won clears three dollars of debt; finish behind and the house takes everything.</summary>
    public sealed class EscapeClause : ContractDefinition
    {
        public const int DebtPerDollar = 3;
        public override string Id => "escape_clause";
        public override string Name => "His Escape Clause";
        public override bool Exclusive => true;
        public override string Terms(IGameHost host) { return "On the Last Spin, every dollar you win also clears $" + DebtPerDollar + " of debt. Finish the spin behind and the house takes your entire balance and the Vault."; }

        public override void OnAccept(ContractInstance contract, IGameHost host) { contract.RoundsLeft = 1; }

        public override ContractStatus OnRoundResolved(ContractInstance contract, RoundResult result, IGameHost host)
        {
            if (result.Net > 0)
            {
                long cleared = host.Ledger.ForgiveDebt(result.Net * DebtPerDollar, Name);
                host.Notify("The Escape Clause clears $" + cleared.ToString("N0") + " of debt.");
                return ContractStatus.Fulfilled;
            }
            if (host.Ledger.Stored > 0) host.Ledger.Withdraw(host.Ledger.Stored);
            if (host.Ledger.Balance > 0) host.Ledger.ForceDebit(host.Ledger.Balance, TransactionKind.Penalty, Name);
            host.Notify("The Escape Clause: the house takes everything.");
            return ContractStatus.Breached;
        }
    }

    /// <summary>Every contract in the game.</summary>
    public static class ContractCatalog
    {
        public static IReadOnlyList<ContractDefinition> All { get; } = new ContractDefinition[]
        {
            new GenerousOffer(), new DoubleOrNothing(), new InsuranceContract(), new DevilsBargain(), new LoanShark(), new StrangersWager(), new FinalOffer(), new EscapeClause()
        };

        public static ContractDefinition Get(string id)
        {
            foreach (ContractDefinition c in All) if (c.Id == id) return c;
            return null;
        }
    }

    /// <summary>The player's signed contracts and the offer currently on the table.</summary>
    public sealed class ContractBook
    {
        private readonly List<ContractInstance> _active = new List<ContractInstance>();
        private readonly List<ContractInstance> _history = new List<ContractInstance>();

        public IReadOnlyList<ContractInstance> Active => _active;
        public IReadOnlyList<ContractInstance> History => _history;

        /// <summary>The contract the Game Master (or a guest) is currently offering, or null.</summary>
        public ContractDefinition Offer { get; set; }

        public event Action<ContractInstance> Signed;
        public event Action<ContractInstance> Resolved;

        public ContractInstance Sign(ContractDefinition definition, IGameHost host)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var contract = new ContractInstance(definition, host.CurrentRound);
            _history.Add(contract);
            definition.OnAccept(contract, host);
            if (contract.Status == ContractStatus.Pending) _active.Add(contract);
            else Resolved?.Invoke(contract);
            if (Offer == definition) Offer = null;
            Signed?.Invoke(contract);
            return contract;
        }

        public bool Has(string id)
        {
            foreach (ContractInstance c in _active) if (c.Definition.Id == id) return true;
            return false;
        }

        public void SpinStarting(IGameHost host)
        {
            foreach (ContractInstance c in _active.ToArray()) c.Definition.OnSpinStarting(c, host);
        }

        public void RoundResolved(RoundResult result, IGameHost host)
        {
            foreach (ContractInstance c in _active.ToArray())
            {
                if (c.SignedRound > result.RoundNumber) continue;
                c.Status = c.Definition.OnRoundResolved(c, result, host);
                Finish(c);
            }
        }

        public void RoundEnded(IGameHost host)
        {
            foreach (ContractInstance c in _active.ToArray())
            {
                c.Status = c.Definition.OnRoundEnded(c, host);
                Finish(c);
            }
        }

        private void Finish(ContractInstance c)
        {
            if (c.Status == ContractStatus.Pending) return;
            _active.Remove(c);
            Resolved?.Invoke(c);
        }

        public void Restore(IEnumerable<ContractInstance> active)
        {
            _active.Clear();
            if (active != null) _active.AddRange(active);
        }
    }
}

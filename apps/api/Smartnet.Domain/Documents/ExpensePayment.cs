using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One settlement against an <see cref="Expense"/> — money actually paid out for it.
/// </summary>
/// <remarks>
/// An expense is recorded when it is <b>incurred</b>, not when it is paid: what it costs is owed from the
/// moment the bill arrives, and the money leaves later — in full, or in instalments. So the expense owns the
/// obligation and these rows own the money, and what is still outstanding is <b>derived</b>
/// (<see cref="Expense.Amount"/> − Σ payments), never a stored, mutated flag. Partial settlement therefore
/// works, and "settled" is a computed fact rather than something a save has to remember to set.
///
/// <para>There is no supplier here, deliberately: an expense is a flat log of money spent, not a payable
/// owed to a party on file — see <see cref="Expense"/>. Whom it was paid to is the expense's description
/// and, on a cheque, the payee.</para>
///
/// <para><b><see cref="DataOrigin"/> matters to the ledger.</b> A <c>new</c> payment is a real money event
/// and posts to the general ledger (Dr Accounts Payable, Cr Cash/Bank). A <c>migrated</c> one is the
/// backfill for an expense recorded before expenses could be settled separately: it was paid as it was
/// entered, its original GL entry already credited Cash/Bank, and re-posting one here would spend the money
/// twice. Migrated payments therefore record the settlement without posting anything.</para>
/// </remarks>
public class ExpensePayment : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The expense being settled, by surrogate id — this app's own or an adopted legacy one.</summary>
    public long ExpenseId { get; set; }

    /// <summary>The paying entity, copied from the expense so the payment is company-scoped on its own.</summary>
    public long? CompanyId { get; set; }

    /// <summary>The date the money went out.</summary>
    public DateOnly Date { get; set; }

    /// <summary>How much of the expense this settles — part of it, or all that is left.</summary>
    public decimal Amount { get; set; }

    /// <summary>How it was paid — Cash, Bank, Cheque, Online.</summary>
    public string? Method { get; set; }

    /// <summary>A reference — cheque number, transfer reference.</summary>
    public string? Reference { get; set; }

    /// <summary><c>new</c> for a settlement this app recorded; <c>migrated</c> for a backfilled one (see remarks).</summary>
    public string DataOrigin { get; set; } = "new";

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>The origins an <see cref="ExpensePayment"/> can have.</summary>
public static class ExpensePaymentOrigin
{
    /// <summary>Recorded by this app as a real money event; posts to the general ledger.</summary>
    public const string New = "new";

    /// <summary>
    /// Backfilled for an expense that predates separate settlement — it was paid as it was entered, so the
    /// settlement is recorded but nothing is posted (the expense's own entry already credited Cash/Bank).
    /// </summary>
    public const string Migrated = "migrated";
}

using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// An expense (Phase 7, slice 3) — a flat, standalone record of money spent, on the adopted legacy
/// <c>expense_tr</c> table.
/// </summary>
/// <remarks>
/// The legacy app kept expenses as an append-only log feeding one report — no ledger, no balance, money in a
/// <c>varchar</c>, and a hard delete. Adopted additively: the typed columns (amount, date, category link) are
/// the new source of truth and the legacy <c>varchar</c> columns sit beside them, dual-written so the
/// surviving <c>ExpenseReport</c> keeps reading. Unlike the legacy row, the surrogate id is real (the legacy
/// <c>id</c> was <c>0</c> on every row) and delete is soft.
/// </remarks>
public class Expense : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The company the expense belongs to (already a typed column from the multi-company migration).</summary>
    public long? CompanyId { get; set; }

    /// <summary>The category, by surrogate key — see <see cref="Smartnet.Domain.MasterData.ExpenseCategory"/>.</summary>
    public long CategoryId { get; set; }

    /// <summary>The date the expense was incurred.</summary>
    public DateOnly Date { get; set; }

    /// <summary>What it was for.</summary>
    public string Description { get; set; } = null!;

    /// <summary>The net amount, before VAT.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>The VAT rate applied, as a percentage.</summary>
    public decimal TaxRatePercentage { get; set; }

    /// <summary>The VAT amount — derived (<see cref="Amount"/> − <see cref="NetAmount"/>), not stored.</summary>
    public decimal TaxAmount => Amount - NetAmount;

    /// <summary>The total spent, VAT included (the legacy <c>expense_amount</c>).</summary>
    public decimal Amount { get; set; }

    /// <summary>How it was paid — Cash, Cheque, etc. (the legacy <c>paymentm</c>).</summary>
    public string? Method { get; set; }

    /// <summary>A reference (the legacy <c>payment_ref</c>).</summary>
    public string? Reference { get; set; }

    /// <summary><c>new</c> for expenses this app raised; <c>legacy</c> for the adopted rows.</summary>
    public string DataOrigin { get; set; } = "new";

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

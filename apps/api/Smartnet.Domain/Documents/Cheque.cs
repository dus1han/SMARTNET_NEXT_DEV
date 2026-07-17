using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A cheque in the cheque register (Phase 7, slice 2) — a standalone record of a cheque written, on the
/// adopted legacy <c>cheques</c> table.
/// </summary>
/// <remarks>
/// Deliberately standalone: it touches no ledger and no balance — the legacy app never tied a cheque to a
/// payment, and neither does this. It is a written record (who it was to, the bank, the number, the dates,
/// the amount), listed and reprinted. Adopted additively: the typed columns are the new app's source of
/// truth and the legacy <c>varchar</c> columns sit beside them, dual-written so the surviving
/// <c>ChequeReport</c> keeps reading. <b>Printing is Phase 8</b> — <see cref="PrintedAt"/> is recorded, not
/// produced here.
/// </remarks>
public class Cheque : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The drawing entity (already a typed column from the multi-company migration).</summary>
    public long? CompanyId { get; set; }

    /// <summary>The supplier, when <see cref="EntryType"/> is <c>Supplier</c> — resolved from <see cref="SupplierCode"/>.</summary>
    public long? SupplierId { get; set; }

    /// <summary>Whether the payee was entered free-text (<c>Manual</c>) or picked from suppliers (<c>Supplier</c>).</summary>
    public string EntryType { get; set; } = "Manual";

    /// <summary>Who the cheque is payable to.</summary>
    public string PayTo { get; set; } = null!;

    /// <summary>The supplier's legacy code, when a supplier was picked.</summary>
    public string? SupplierCode { get; set; }

    /// <summary>The drawing bank.</summary>
    public string? Bank { get; set; }

    /// <summary>The cheque number.</summary>
    public string? ChequeNumber { get; set; }

    /// <summary>The amount of the cheque.</summary>
    public decimal Amount { get; set; }

    /// <summary>The date on the cheque.</summary>
    public DateOnly? ChequeDate { get; set; }

    /// <summary>The date it may be banked.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>When it was printed, if it has been (printing itself is Phase 8).</summary>
    public DateTime? PrintedAt { get; set; }

    /// <summary>
    /// Where the cheque came from: <c>SupplierPayment</c> or <c>Expense</c> when it was raised as the payment
    /// method of one of those (so it is <b>not</b> a separate money event — the payment/expense is), or
    /// <c>null</c> for a standalone/manual cheque written to anyone.
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>The id of the supplier payment or expense this cheque was raised for; <c>null</c> for a manual cheque.</summary>
    public long? SourceId { get; set; }

    /// <summary><c>new</c> for cheques this app raised; <c>legacy</c> for the adopted rows.</summary>
    public string DataOrigin { get; set; } = "new";

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

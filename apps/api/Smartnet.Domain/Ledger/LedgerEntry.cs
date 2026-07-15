using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Ledger;

/// <summary>
/// Why a customer's receivable moved. The sign of the entry is decided by the type (see
/// <see cref="LedgerEntry.Amount"/>).
/// </summary>
public enum LedgerEntryType
{
    /// <summary>
    /// The stored legacy balance, imported verbatim at cutover — including the 45 that are negative
    /// (Finding 1). The ledger starts from history; it does not recompute it (LEGACY-DATA-POLICY §2).
    /// </summary>
    OpeningBalance,

    /// <summary>An invoice raised — the customer now owes its total.</summary>
    Charge,

    /// <summary>A payment received — reduces what is owed.</summary>
    Payment,

    /// <summary>A credit note — reduces what is owed.</summary>
    Credit,
}

/// <summary>
/// One entry in the receivables ledger — the single reason a customer's balance ever moves.
/// </summary>
/// <remarks>
/// This is ISSUES B3 answered. The legacy app keeps a mutable <c>invoice_h.balance</c> and edits it in
/// place — <c>UPDATE … SET balance = balance - amount</c> — which is the exact mechanism that produced
/// Rs. 1.55M of duplicate payments nobody could reconstruct (Finding 1). Here a balance is <b>never
/// stored and never edited</b>: a customer's balance is <b>derived</b>, always, as the sum of these
/// entries. A mistake is corrected by a second, compensating entry, never by rewriting a number, so the
/// whole history of what a customer owes is always readable.
/// <para>
/// Append-only, exactly like <see cref="Smartnet.Domain.MasterData.StockMovement"/>: it is
/// <see cref="IAuditable"/> (it records who entered it and when) but deliberately <b>not</b>
/// <c>ISoftDeletable</c>. There is no update path and no delete path in the application — a ledger you
/// can edit is not a ledger. A voided document reverses its entries with new ones.
/// </para>
/// </remarks>
public class LedgerEntry : IAuditable
{
    public long Id { get; set; }

    /// <summary>Whose balance this moves, by surrogate key.</summary>
    public long CustomerId { get; set; }

    public LedgerEntryType Type { get; set; }

    /// <summary>
    /// Signed. A positive amount increases the receivable (a <see cref="LedgerEntryType.Charge"/>, an
    /// <see cref="LedgerEntryType.OpeningBalance"/>); a negative one decreases it (a
    /// <see cref="LedgerEntryType.Payment"/>, a <see cref="LedgerEntryType.Credit"/>). The customer's
    /// balance is the sum of these, so the sign is the whole of the arithmetic. An opening balance
    /// carries the legacy figure verbatim — negative if the legacy row was negative.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// The invoice this entry belongs to (by surrogate id — an existing legacy invoice's for an opening
    /// balance, a new invoice's for a charge). Null for an entry tied to no single document.
    /// </summary>
    public long? InvoiceId { get; set; }

    /// <summary>
    /// When the entry's event happened — the invoice date, the payment date, or the cutover date for an
    /// opening balance — as distinct from when the row was written (<see cref="CreatedAt"/>).
    /// </summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>A short note — e.g. "Imported from legacy system; not recalculated" on an import.</summary>
    public string? Note { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // Present because IAuditable carries them and the interceptor stamps uniformly; never written,
    // because a ledger entry is never updated or deleted.
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

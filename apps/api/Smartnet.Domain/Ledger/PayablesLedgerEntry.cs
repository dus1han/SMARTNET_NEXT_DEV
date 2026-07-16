using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Ledger;

/// <summary>
/// Why a supplier's payable moved. The sign of the entry is decided by the type (see
/// <see cref="PayablesLedgerEntry.Amount"/>).
/// </summary>
public enum PayablesLedgerEntryType
{
    /// <summary>
    /// The stored legacy outstanding, imported verbatim at cutover — the amount of a supplier invoice the
    /// old system still had as <c>Pending</c>. The ledger starts from history; it does not recompute it
    /// (LEGACY-DATA-POLICY §2).
    /// </summary>
    OpeningBalance,

    /// <summary>A supplier invoice recorded — we now owe its amount.</summary>
    Purchase,

    /// <summary>A payment made to the supplier — reduces what we owe.</summary>
    Payment,
}

/// <summary>
/// One entry in the payables ledger — the single reason a supplier's balance ever moves. The supply-side
/// mirror of <see cref="LedgerEntry"/> (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// The legacy app has no payables balance at all: supplier outstanding is a date-scoped
/// <c>SUM(amount) WHERE paymentstat='Pending'</c> re-derived per report, and a payment is a binary flip of
/// <c>paymentstat</c> with no partial-payment path (<c>SupplierInvoicesController</c>). Here a supplier's
/// balance is <b>derived</b>, always, as the sum of these entries — never stored, never edited — so partial
/// payments work and "paid" is a computed fact (<c>Σ = 0</c>), not a flag.
/// <para>
/// Append-only, exactly like the receivables ledger and <see cref="Smartnet.Domain.MasterData.StockMovement"/>:
/// it is <see cref="IAuditable"/> (who entered it, when) but deliberately <b>not</b> <c>ISoftDeletable</c>.
/// There is no update or delete path — a ledger you can edit is not a ledger. A voided supplier invoice
/// reverses its entries with new ones.
/// </para>
/// </remarks>
public class PayablesLedgerEntry : IAuditable
{
    public long Id { get; set; }

    /// <summary>Whose balance this moves, by surrogate key.</summary>
    public long SupplierId { get; set; }

    public PayablesLedgerEntryType Type { get; set; }

    /// <summary>
    /// Signed. A positive amount increases the payable (a <see cref="PayablesLedgerEntryType.Purchase"/>,
    /// an <see cref="PayablesLedgerEntryType.OpeningBalance"/>); a negative one decreases it (a
    /// <see cref="PayablesLedgerEntryType.Payment"/>). The supplier's balance is the sum of these, so the
    /// sign is the whole of the arithmetic. An opening balance carries the legacy figure verbatim.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// The supplier invoice this entry belongs to (by surrogate id — an existing legacy invoice's for an
    /// opening balance, a new one's for a purchase or a payment). Null for an entry tied to no single document.
    /// </summary>
    public long? SupplierInvoiceId { get; set; }

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

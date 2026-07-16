namespace Smartnet.Domain.Documents;

/// <summary>One line of a credit note being created — the browser's draft, server-side.</summary>
/// <param name="ItemId">The stock item, or null for a free-typed service line.</param>
/// <param name="Cost">The line's cost basis — item lines only.</param>
public sealed record NewCreditNoteLine(
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>
/// A whole credit note, posted at once — raised against a parent invoice, reversing part or all of it.
/// </summary>
/// <param name="InvoiceId">The parent invoice's surrogate id — the note credits it through the ledger.</param>
/// <param name="InvoiceNumber">
/// The parent invoice's business number, written to the legacy <c>cn_h.invoiceno</c> column (NOT NULL) so
/// the legacy shape is satisfied and a legacy reader still sees which invoice the note belongs to.
/// </param>
/// <param name="ReturnsStock">Whether item lines return their goods to stock (legacy <c>stockposting</c>).</param>
/// <param name="TaxRateId">The parent invoice's tax-rate row (null for a legacy invoice), inherited verbatim.</param>
/// <param name="TaxRatePercentage">
/// The parent invoice's snapshotted VAT percentage, inherited so a full credit nets exactly against the
/// invoice. The engine applies this rather than re-resolving one at the note's own date.
/// </param>
public sealed record NewCreditNote(
    long CompanyId,
    long CustomerId,
    long InvoiceId,
    string InvoiceNumber,
    DateOnly Date,
    bool ReturnsStock,
    long? TaxRateId,
    decimal TaxRatePercentage,
    IReadOnlyList<NewCreditNoteLine> Lines);

/// <summary>What the caller gets back — enough to show a toast and route to the new credit note.</summary>
public sealed record CreditNoteCreated(long Id, string Number, decimal Total);

/// <summary>
/// Creates a credit note — the whole of it, in one transaction (Phase 5, slice 4).
/// </summary>
/// <remarks>
/// The mirror of <see cref="IInvoiceCreator"/>, sharing its pipeline shape: the tax engine values the lines
/// (at the <b>parent invoice's</b> inherited rate), the number allocator reserves the number under a row
/// lock, the header and lines are written with their legacy shadow columns, the receivables ledger is
/// <b>credited</b> (the opposite sign to an invoice's charge), stock is <b>received</b> back for item lines
/// when the note returns goods, and a version-1 snapshot is taken — <b>all or none</b> (B2). There is no
/// cash/credit split and no credit-limit gate: a credit note only ever reduces what a customer owes.
/// </remarks>
public interface ICreditNoteCreator
{
    /// <summary>Creates a credit note in its own transaction.</summary>
    Task<CreditNoteCreated> CreateAsync(NewCreditNote request, CancellationToken cancellationToken = default);
}

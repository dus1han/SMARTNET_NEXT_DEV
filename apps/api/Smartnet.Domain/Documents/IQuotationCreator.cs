namespace Smartnet.Domain.Documents;

/// <summary>One line of a quotation being created — the browser's draft, server-side.</summary>
/// <param name="ItemId">The stock item, or null for a free-typed service line.</param>
/// <param name="Cost">The line's cost basis — item lines only; the basis for the quoted margin.</param>
public sealed record NewQuotationLine(
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>A whole quotation, posted at once — the cart is gone (D4), the same as an invoice.</summary>
/// <param name="Validity">How long the quoted price holds (legacy <c>q_valid</c>), e.g. "30 Days".</param>
/// <param name="DocumentDiscountPercent">
/// A discount on the whole document, after any per-line discounts and before VAT. 0 for none.
/// </param>
/// <param name="DocumentCost">
/// A document-level cost for a <b>service</b> quotation — where cost cannot be derived from item lines, the
/// user enters one figure (the legacy <c>quotation_h.quotecost</c>). Null for an <b>item</b> quotation,
/// whose cost is the sum of the line costs from the item master; carried to the invoice on conversion.
/// </param>
public sealed record NewQuotation(
    long CompanyId,
    long CustomerId,
    DateOnly Date,
    string? ContactPerson,
    string? Validity,
    IReadOnlyList<NewQuotationLine> Lines,
    decimal DocumentDiscountPercent = 0m,
    decimal? DocumentCost = null);

/// <summary>What the caller gets back — enough to show a toast and route to the new quotation.</summary>
public sealed record QuotationCreated(long Id, string Number, decimal Total);

/// <summary>
/// Creates a quotation — the whole of it, in one transaction (Phase 5, slice 3).
/// </summary>
/// <remarks>
/// The invoice pipeline with its two sales-only steps removed: the tax engine values the lines, the
/// number allocator reserves the number under a row lock, the header and lines are written with their
/// legacy shadow columns beside them, and a version-1 snapshot is taken — <b>all or none</b>. There is
/// <b>no ledger charge and no stock issue</b>: a quotation is a priced offer, not a sale. The number is
/// reserved <i>inside</i> the transaction, so a failed save rolls it back rather than burning it (B4).
/// </remarks>
public interface IQuotationCreator
{
    Task<QuotationCreated> CreateAsync(NewQuotation request, CancellationToken cancellationToken = default);
}

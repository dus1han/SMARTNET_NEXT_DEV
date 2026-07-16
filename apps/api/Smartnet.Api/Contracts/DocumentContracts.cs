using FluentValidation;

namespace Smartnet.Api.Contracts;

/// <summary>
/// A new invoice, posted whole — the browser holds the draft and sends it in one request (D4). This is
/// the body the Phase 2 line-item prototype's <c>toPayload</c> produces, once its per-line tax rate is
/// dropped (one company rate per document — the <c>one-vat-rate-per-document</c> decision).
/// </summary>
public sealed record CreateInvoiceRequest(
    long CompanyId,
    long CustomerId,
    string Type,
    DateOnly Date,
    string? PurchaseOrderNo,
    string? ContactPerson,
    IReadOnlyList<CreateInvoiceLineRequest> Lines,
    // A discount on the whole document, after any per-line discounts and before VAT. 0 for none — a
    // discount may be given per line, on the document, or both.
    decimal DocumentDiscountPercent = 0m);

/// <param name="ItemId">The stock item, or null for a free-typed service line.</param>
public sealed record CreateInvoiceLineRequest(
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

public sealed record InvoiceCreatedResponse(long Id, string Number, decimal Total, decimal Outstanding);

/// <summary>
/// The single VAT rate a new invoice would carry for a company on a date — one rate per document (the
/// <c>one-vat-rate-per-document</c> decision). Resolved by the same tax engine the save uses, so the
/// figure the New Invoice screen previews cannot drift from the one it is charged. <see cref="Percentage"/>
/// is 0 and <see cref="TaxRateId"/> null for a company that is not VAT-registered.
/// </summary>
public sealed record InvoiceTaxRate(long? TaxRateId, string Name, decimal Percentage);

/// <summary>One row of the invoice list. <see cref="Outstanding"/> is derived from the ledger.</summary>
public sealed record InvoiceSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? CustomerName,
    string Type,
    decimal Total,
    decimal Outstanding);

public sealed record InvoiceLineDetail(
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal Gross,
    decimal Net,
    decimal? Cost);

/// <summary>One invoice, in full — the read view. <see cref="Outstanding"/> is the derived balance.</summary>
public sealed record InvoiceDetail(
    long Id,
    string Number,
    DateOnly Date,
    string Type,
    string? CustomerName,
    string? CustomerCode,
    string? PurchaseOrderNo,
    string? ContactPerson,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    decimal Outstanding,
    IReadOnlyList<InvoiceLineDetail> Lines);

/// <summary>Server-side validation — the authority, not the hopeful frontend.</summary>
public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.CustomerId).GreaterThan(0);

        RuleFor(r => r.Type)
            .Must(t => t is "Cash" or "Credit")
            .WithMessage("Type must be 'Cash' or 'Credit'.");

        RuleFor(r => r.DocumentDiscountPercent).InclusiveBetween(0m, 100m);

        RuleFor(r => r.Lines).NotEmpty().WithMessage("An invoice needs at least one line.");

        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0m, 100m);

            // A line is either an item line (carries an item) or a service line (carries a description).
            // The legacy bug was an item invoice that kept neither — see InvoiceLine.
            line.RuleFor(l => l)
                .Must(l => l.ItemId is not null || !string.IsNullOrWhiteSpace(l.Description))
                .WithMessage("A line must reference an item or carry a description.");
        });
    }
}

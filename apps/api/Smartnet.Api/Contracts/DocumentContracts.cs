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
    decimal DocumentDiscountPercent = 0m,
    // Set when the person raising the invoice has been shown a credit-limit breach and confirmed it — the
    // confirmation is the override that lets a soft-gated breach save. False on a first, un-confirmed try.
    bool AcknowledgeCreditLimit = false);

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
/// An edit to an issued invoice. The lines carry an <see cref="EditInvoiceLineRequest.Id"/> so the change
/// is reconciled in place, not by deleting and re-inserting. Company, customer, type and date are not
/// editable — those are the invoice's identity. A reason (<c>X-Change-Reason</c>) is required by the endpoint.
/// </summary>
public sealed record EditInvoiceRequest(
    // The row_version loaded with the invoice, echoed back so a concurrent edit is rejected (409).
    int ExpectedRowVersion,
    string? PurchaseOrderNo,
    string? ContactPerson,
    IReadOnlyList<EditInvoiceLineRequest> Lines,
    decimal DocumentDiscountPercent = 0m);

/// <param name="Id">The existing line this maps to, or null for a line the edit adds.</param>
public sealed record EditInvoiceLineRequest(
    long? Id,
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>What an edit returns — the new figures, the derived outstanding, and the version it wrote.</summary>
public sealed record InvoiceEditedResponse(long Id, string Number, decimal Total, decimal Outstanding, int VersionNo);

/// <summary>
/// One row of the deleted-invoice register — the new-side replacement for the legacy
/// <c>DeletedInvoicesController</c>. A soft-deleted invoice, with who voided it, when, and why.
/// </summary>
public sealed record DeletedInvoiceSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? CustomerName,
    decimal Total,
    DateTime DeletedAt,
    string? DeletedByName,
    string? Reason);

/// <summary>
/// A customer's credit standing, for the New Invoice screen's advisory. <see cref="Outstanding"/> is the
/// derived ledger balance (the same figure the server-side gate measures against); <see cref="Enforced"/>
/// is whether the company hard-blocks a breach at save. <see cref="CreditLimit"/> of 0 means "no limit".
/// </summary>
public sealed record CreditStatus(decimal CreditLimit, decimal Outstanding, bool Enforced);

/// <summary>
/// The single VAT rate a new invoice would carry for a company on a date — one rate per document (the
/// <c>one-vat-rate-per-document</c> decision). Resolved by the same tax engine the save uses, so the
/// figure the New Invoice screen previews cannot drift from the one it is charged. <see cref="Percentage"/>
/// is 0 and <see cref="TaxRateId"/> null for a company that is not VAT-registered.
/// </summary>
public sealed record InvoiceTaxRate(long? TaxRateId, string Name, decimal Percentage);

/// <summary>One row of the invoice list. <see cref="Outstanding"/> is derived from the ledger.</summary>
/// <param name="Origin">
/// <c>new</c> for an invoice this app raised (a live, derived outstanding, and a read view); <c>legacy</c>
/// for one adopted from the old system (its stored figures, read-only — no new-side detail view).
/// </param>
public sealed record InvoiceSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? CustomerName,
    string Type,
    decimal Total,
    decimal Outstanding,
    string Origin);

/// <param name="Id">
/// The line's surrogate id, for an edit to reconcile against (new invoice lines only). Null for a legacy
/// line or a quotation/credit-note line, which are not edited through the invoice editor.
/// </param>
public sealed record InvoiceLineDetail(
    long? Id,
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal Gross,
    decimal Net,
    decimal? Cost);

/// <summary>One invoice, in full — the read view.</summary>
/// <param name="Outstanding">
/// For a <c>new</c> invoice, the derived ledger balance; for a <c>legacy</c> one, the old system's
/// stored balance (read-only, as imported).
/// </param>
/// <param name="Origin">
/// <c>new</c> for an invoice this app raised (typed figures, a real change history); <c>legacy</c> for one
/// adopted from the old system (its stored <c>varchar</c> figures, and no new-app history).
/// </param>
public sealed record InvoiceDetail(
    long Id,
    string Number,
    DateOnly Date,
    string Type,
    // The trading entity that raised it (e.g. "Smart Net", "Smart Technologies").
    string? CompanyName,
    // "Item" when any line references a stock item, "Service" when every line is free-typed — the legacy
    // it = ITEM|SERVICE distinction, derived from the lines rather than a separate document type.
    string Kind,
    string? CustomerName,
    string? CustomerCode,
    string? PurchaseOrderNo,
    string? ContactPerson,
    decimal Subtotal,
    decimal DiscountAmount,
    // The whole-document discount rate, so the edit screen can seed it (the lines carry their own).
    decimal DocumentDiscountPercent,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    decimal Outstanding,
    // The row_version the edit screen loads and sends back, so a concurrent edit is rejected rather than
    // silently overwritten. 0 for a legacy invoice (which the new editor does not touch).
    int RowVersion,
    string Origin,
    IReadOnlyList<InvoiceLineDetail> Lines);

// --- Quotations (Phase 5, slice 3) --------------------------------------------------------------

/// <summary>
/// A new quotation, posted whole — the same draft an invoice is, minus the sale. A quotation has no
/// cash/credit <c>Type</c> (it settles nothing) and no PO; it carries a <see cref="Validity"/> (how long
/// the price holds). Its lines are the same <see cref="CreateInvoiceLineRequest"/> draft.
/// </summary>
public sealed record CreateQuotationRequest(
    long CompanyId,
    long CustomerId,
    DateOnly Date,
    string? ContactPerson,
    string? Validity,
    IReadOnlyList<CreateInvoiceLineRequest> Lines,
    decimal DocumentDiscountPercent = 0m);

public sealed record QuotationCreatedResponse(long Id, string Number, decimal Total);

/// <summary>
/// The terms that turn a quotation into an invoice — everything else comes from the quote. The invoice
/// is taxed at its own <see cref="Date"/> (not the quote's) through the same save pipeline.
/// </summary>
public sealed record ConvertQuotationRequest(
    string Type,
    DateOnly Date,
    string? PurchaseOrderNo,
    string? ContactPerson);

/// <summary>One row of the quotation list. <see cref="ConvertedInvoiceId"/> is set once it is converted.</summary>
/// <param name="Origin"><c>new</c> for one this app raised; <c>legacy</c> for one adopted from the old system.</param>
public sealed record QuotationSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? CustomerName,
    decimal Total,
    long? ConvertedInvoiceId,
    string Origin);

/// <summary>One quotation, in full — the read view, with its conversion state and back-link.</summary>
/// <param name="Origin"><c>new</c> (typed figures, a change history) or <c>legacy</c> (the old system's stored figures).</param>
public sealed record QuotationDetail(
    long Id,
    string Number,
    DateOnly Date,
    string? CompanyName,
    string Kind,
    string? CustomerName,
    string? CustomerCode,
    string? ContactPerson,
    string? Validity,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    long? ConvertedInvoiceId,
    string? ConvertedInvoiceNumber,
    string Origin,
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

/// <summary>Server-side validation for an invoice edit — the same line rules as a create.</summary>
public sealed class EditInvoiceRequestValidator : AbstractValidator<EditInvoiceRequest>
{
    public EditInvoiceRequestValidator()
    {
        RuleFor(r => r.DocumentDiscountPercent).InclusiveBetween(0m, 100m);
        RuleFor(r => r.Lines).NotEmpty().WithMessage("An invoice needs at least one line.");

        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0m, 100m);

            line.RuleFor(l => l)
                .Must(l => l.ItemId is not null || !string.IsNullOrWhiteSpace(l.Description))
                .WithMessage("A line must reference an item or carry a description.");
        });
    }
}

/// <summary>Server-side validation for a new quotation — the same line rules as an invoice.</summary>
public sealed class CreateQuotationRequestValidator : AbstractValidator<CreateQuotationRequest>
{
    public CreateQuotationRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.CustomerId).GreaterThan(0);
        RuleFor(r => r.DocumentDiscountPercent).InclusiveBetween(0m, 100m);

        RuleFor(r => r.Lines).NotEmpty().WithMessage("A quotation needs at least one line.");

        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0m, 100m);

            line.RuleFor(l => l)
                .Must(l => l.ItemId is not null || !string.IsNullOrWhiteSpace(l.Description))
                .WithMessage("A line must reference an item or carry a description.");
        });
    }
}

/// <summary>Validation for a conversion — only the sale terms the quote does not itself carry.</summary>
public sealed class ConvertQuotationRequestValidator : AbstractValidator<ConvertQuotationRequest>
{
    public ConvertQuotationRequestValidator()
    {
        RuleFor(r => r.Type)
            .Must(t => t is "Cash" or "Credit")
            .WithMessage("Type must be 'Cash' or 'Credit'.");
    }
}

// --- Credit notes (Phase 5, slice 4) ------------------------------------------------------------

/// <summary>
/// A new credit note, posted whole — raised against a parent invoice, reversing part or all of it. The
/// customer, company and VAT rate come from the parent invoice (not entered here); the caller supplies the
/// lines to credit (a subset or the whole), whether the note <see cref="ReturnsStock"/>, and its date. Its
/// lines are the same <see cref="CreateInvoiceLineRequest"/> draft an invoice uses.
/// </summary>
public sealed record CreateCreditNoteRequest(
    long InvoiceId,
    DateOnly Date,
    bool ReturnsStock,
    IReadOnlyList<CreateInvoiceLineRequest> Lines);

public sealed record CreditNoteCreatedResponse(long Id, string Number, decimal Total);

/// <summary>One row of the credit-note list. <see cref="InvoiceNumber"/> is the invoice it credits.</summary>
/// <param name="Origin"><c>new</c> for one this app raised; <c>legacy</c> for one adopted from the old system.</param>
public sealed record CreditNoteSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? CustomerName,
    string InvoiceNumber,
    decimal Total,
    string Origin);

/// <summary>One credit note, in full — the read view (its parent invoice, and how it was issued).</summary>
/// <param name="Origin"><c>new</c> (typed figures, a change history) or <c>legacy</c> (the old system's stored figures).</param>
public sealed record CreditNoteDetail(
    long Id,
    string Number,
    DateOnly Date,
    string? CompanyName,
    string Kind,
    string? CustomerName,
    string? CustomerCode,
    long? InvoiceId,
    string InvoiceNumber,
    bool ReturnsStock,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    string Origin,
    IReadOnlyList<InvoiceLineDetail> Lines);

/// <summary>Server-side validation for a new credit note — the same line rules as an invoice.</summary>
public sealed class CreateCreditNoteRequestValidator : AbstractValidator<CreateCreditNoteRequest>
{
    public CreateCreditNoteRequestValidator()
    {
        RuleFor(r => r.InvoiceId).GreaterThan(0);

        RuleFor(r => r.Lines).NotEmpty().WithMessage("A credit note needs at least one line.");

        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0m, 100m);

            line.RuleFor(l => l)
                .Must(l => l.ItemId is not null || !string.IsNullOrWhiteSpace(l.Description))
                .WithMessage("A line must reference an item or carry a description.");
        });
    }
}

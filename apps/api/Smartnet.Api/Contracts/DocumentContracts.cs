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
    bool AcknowledgeCreditLimit = false,
    // A service invoice's document-level cost (the legacy service Cost box). Null for an item invoice,
    // whose cost is the sum of its per-line item costs.
    decimal? DocumentCost = null);

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
    decimal DocumentDiscountPercent = 0m,
    // A service invoice's document-level cost; null for an item invoice (cost derived from the lines).
    decimal? DocumentCost = null,
    // The document date. Null leaves it alone. Changing it re-rates the invoice at the new date and moves
    // its ledger and stock entries with it — and is refused once anything depends on the document
    // (see DocumentDateLockedException).
    DateOnly? Date = null);

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
/// One deleted invoice in full — the read view behind a row of the deleted register. The document as it
/// stood when it was voided (header, lines, totals) plus who deleted it, when and why. Serves both a
/// <c>legacy</c> deletion (from <c>del_invoice_h</c>/<c>del_invoice_l</c>, the register the old app kept)
/// and a <c>new</c>-app void (the soft-deleted invoice). Read-only — nothing here can be edited.
/// </summary>
public sealed record DeletedInvoiceDetail(
    string Number,
    DateOnly Date,
    string Type,
    string? CompanyName,
    string Kind,
    string? CustomerName,
    string? CustomerCode,
    string? PurchaseOrderNo,
    string? ContactPerson,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal DocumentDiscountPercent,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    // "legacy" for a deletion the old app recorded in del_invoice_h; "new" for an invoice this app voided.
    string Origin,
    DateTime DeletedAt,
    string? DeletedByName,
    string? Reason,
    IReadOnlyList<InvoiceLineDetail> Lines);

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
    // The document's cost basis — a service invoice's entered figure, or an item invoice's summed line
    // costs. So the edit screen can seed the service Cost field and margin is visible.
    decimal Cost,
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
    decimal DocumentDiscountPercent = 0m,
    // A service quotation's document-level cost (the legacy quotecost). Null for an item quotation, whose
    // cost sums its per-line item costs; carried to the invoice on conversion.
    decimal? DocumentCost = null);

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
    decimal DocumentDiscountPercent,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    // The document's cost basis — a service quotation's entered figure, or an item quotation's summed line
    // costs. Seeds the edit screen's service Cost field.
    decimal Cost,
    long? ConvertedInvoiceId,
    string? ConvertedInvoiceNumber,
    // The row_version the edit screen echoes back (a legacy quote's real version, so an edit adopts it under
    // a concurrency guard).
    int RowVersion,
    string Origin,
    IReadOnlyList<InvoiceLineDetail> Lines);

/// <summary>An edit to a quotation. Lines carry ids to reconcile in place; a reason is required by the endpoint.</summary>
public sealed record EditQuotationRequest(
    int ExpectedRowVersion,
    string? ContactPerson,
    string? Validity,
    IReadOnlyList<EditInvoiceLineRequest> Lines,
    decimal DocumentDiscountPercent = 0m,
    // A service quotation's document-level cost; null for an item quotation (cost derived from the lines).
    decimal? DocumentCost = null,
    // The document date. Null leaves it alone; changing it re-rates the quote at the new date.
    DateOnly? Date = null);

public sealed record QuotationEditedResponse(long Id, string Number, decimal Total, int VersionNo);

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

/// <summary>Server-side validation for a quotation edit — a quotation still needs at least one line.</summary>
public sealed class EditQuotationRequestValidator : AbstractValidator<EditQuotationRequest>
{
    public EditQuotationRequestValidator()
    {
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
    // The row_version the void echoes back, so a stale copy is refused rather than silently reversed.
    int RowVersion,
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

// --- Purchase orders (Phase 6, slice 1) ---------------------------------------------------------

/// <summary>
/// A new purchase order, posted whole — the supply-side counterpart of an invoice, addressed to a
/// supplier. It has no cash/credit <c>Type</c> and no contact (a PO orders from a supplier); its item
/// lines carry an <see cref="CreateInvoiceLineRequest.ItemId"/> so the future goods receipt can receive
/// against them. Its lines are the same <see cref="CreateInvoiceLineRequest"/> draft an invoice uses.
/// </summary>
public sealed record CreatePurchaseOrderRequest(
    long CompanyId,
    long SupplierId,
    DateOnly Date,
    IReadOnlyList<CreateInvoiceLineRequest> Lines,
    decimal DocumentDiscountPercent = 0m);

public sealed record PurchaseOrderCreatedResponse(long Id, string Number, decimal Total);

/// <summary>One row of the purchase-order list.</summary>
/// <param name="Origin"><c>new</c> for one this app raised; <c>legacy</c> for one adopted from the old system.</param>
public sealed record PurchaseOrderSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? SupplierName,
    decimal Total,
    string Origin);

/// <summary>One purchase order, in full — the read view.</summary>
/// <param name="Origin"><c>new</c> (typed figures, a change history) or <c>legacy</c> (the old system's stored figures).</param>
public sealed record PurchaseOrderDetail(
    long Id,
    string Number,
    DateOnly Date,
    string? CompanyName,
    // "Item" when any line references a stock item, "Service" when every line is free-typed — derived from
    // the lines, the same way an invoice's kind is.
    string Kind,
    string? SupplierName,
    string? SupplierCode,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal DocumentDiscountPercent,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Total,
    int RowVersion,
    string Origin,
    IReadOnlyList<InvoiceLineDetail> Lines);

// --- Supplier invoices (Phase 6, slice 2) -------------------------------------------------------

/// <summary>
/// A new supplier invoice — a header-only accounts-payable record. The user enters the supplier's own
/// reference and the figures they billed (no line items, no tax engine — the supplier's numbers).
/// </summary>
public sealed record CreateSupplierInvoiceRequest(
    long CompanyId,
    long SupplierId,
    string? SupplierReference,
    DateOnly Date,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal Amount);

public sealed record SupplierInvoiceCreatedResponse(long Id, string? SupplierReference, decimal Amount);

/// <summary>One row of the supplier-invoice list. <see cref="Outstanding"/> and <see cref="Status"/> are derived from the ledger.</summary>
/// <param name="Origin"><c>new</c> for one this app raised; <c>legacy</c> for one adopted from the old system.</param>
public sealed record SupplierInvoiceSummary(
    long Id,
    string? SupplierReference,
    DateOnly Date,
    string? SupplierName,
    decimal Amount,
    decimal Outstanding,
    string Status,
    string Origin);

/// <summary>A payment made against a supplier invoice — the derived payment history.</summary>
public sealed record SupplierInvoicePaymentLine(DateOnly Date, decimal Amount, string? Method, string? Reference);

/// <summary>One supplier invoice, in full — the read view, with its derived outstanding and payments.</summary>
public sealed record SupplierInvoiceDetail(
    long Id,
    string? SupplierReference,
    DateOnly Date,
    string? CompanyName,
    string? SupplierName,
    string? SupplierCode,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal Amount,
    decimal Outstanding,
    string Status,
    int RowVersion,
    string Origin,
    IReadOnlyList<SupplierInvoicePaymentLine> Payments);

/// <summary>A payment against a supplier invoice — part or all of what is outstanding.</summary>
public sealed record RecordSupplierPaymentRequest(decimal Amount, DateOnly Date, string? Method, string? Reference);

public sealed record SupplierPaymentRecordedResponse(long SupplierInvoiceId, decimal AmountPaid, decimal Outstanding);

// --- Job cards (Phase 6, slice 3) ---------------------------------------------------------------

/// <summary>One serial-tracked line of a job card — one unit of the customer's equipment.</summary>
public sealed record CreateJobCardLineRequest(long? ItemId, string? Description, string? Serial);

/// <summary>A new job card, booked in at once — the fault, the equipment (serial-tracked), the technician.</summary>
public sealed record CreateJobCardRequest(
    long CompanyId,
    long CustomerId,
    DateOnly Date,
    string? ContactPerson,
    string? FaultDescription,
    string? Remarks,
    string? Technician,
    IReadOnlyList<CreateJobCardLineRequest> Lines);

public sealed record JobCardCreatedResponse(long Id, string Number);

/// <summary>One row of the job-card list.</summary>
/// <param name="Origin"><c>new</c> for one this app raised; <c>legacy</c> for one adopted from the old system.</param>
public sealed record JobCardSummary(
    long Id,
    string Number,
    DateOnly Date,
    string? CustomerName,
    string Status,
    string Origin);

/// <summary>One serial-tracked line, for the read view.</summary>
public sealed record JobCardLineDetail(long? ItemId, string? Description, string? Serial);

/// <summary>One job card, in full — the read view, with its lines and (once closed) cost/sell.</summary>
public sealed record JobCardDetail(
    long Id,
    string Number,
    DateOnly Date,
    string? CompanyName,
    string? CustomerName,
    string? CustomerCode,
    string? ContactPerson,
    string? FaultDescription,
    string? Remarks,
    string? Technician,
    string Status,
    decimal? Cost,
    decimal? Sell,
    string? CompletionRemarks,
    int RowVersion,
    string Origin,
    IReadOnlyList<JobCardLineDetail> Lines);

/// <summary>Closing a job — the cost, sell and completion remarks, guarded by the row version.</summary>
public sealed record CloseJobCardRequest(int ExpectedRowVersion, decimal Cost, decimal Sell, string? CompletionRemarks);

/// <summary>One contact a document can be emailed to — a saved contact of that document's customer.</summary>
/// <param name="Selected">
/// Whether the dialog should tick it by default. The customer's document contacts are the people the
/// document would have been handed to on paper; a notifications-only contact is offered but not assumed.
/// </param>
public sealed record DocumentContact(long Id, string? Name, string Email, string Usage, bool Selected);

/// <summary>Who the job sheet can go to, and the message that would be sent — what the dialog renders.</summary>
/// <param name="Blocked">
/// Null when mail is configured and armed; otherwise why sending would fail, said up front rather than
/// after the user has picked recipients and pressed Send.
/// </param>
public sealed record JobSheetRecipients(
    IReadOnlyList<DocumentContact> Contacts,
    string Subject,
    string Body,
    string AttachmentName,
    string? Blocked);

/// <summary>Emailing a document to the chosen saved contacts. The same body for every document.</summary>
public sealed record EmailDocumentRequest(IReadOnlyList<long> ContactIds);

/// <param name="Recipients">The addresses it actually went to, for the confirmation message.</param>
public sealed record EmailDocumentResponse(bool Sent, IReadOnlyList<string> Recipients, string? Error);

/// <summary>An edit to a purchase order. Lines carry ids to reconcile in place; a reason is required.</summary>
public sealed record EditPurchaseOrderRequest(
    int ExpectedRowVersion,
    IReadOnlyList<EditInvoiceLineRequest> Lines,
    decimal DocumentDiscountPercent = 0m,
    decimal? DocumentCost = null,
    // The document date. Null leaves it alone; changing it re-rates the order at the new date.
    DateOnly? Date = null);

public sealed record PurchaseOrderEditedResponse(long Id, string Number, decimal Total, int VersionNo);

/// <summary>Server-side validation for a purchase-order edit — at least one line.</summary>
public sealed class EditPurchaseOrderRequestValidator : AbstractValidator<EditPurchaseOrderRequest>
{
    public EditPurchaseOrderRequestValidator() =>
        RuleFor(r => r.Lines).NotEmpty().WithMessage("A purchase order needs at least one line.");
}

/// <summary>Who a credit note can go to, and the message that would be sent.</summary>
public sealed record CreditNoteRecipients(
    IReadOnlyList<DocumentContact> Contacts,
    string Subject,
    string Body,
    string AttachmentName,
    string? Blocked);

/// <summary>Who a purchase order can go to, and the message that would be sent.</summary>
public sealed record PurchaseOrderRecipients(
    IReadOnlyList<DocumentContact> Contacts,
    string Subject,
    string Body,
    string AttachmentName,
    string? Blocked);

/// <summary>Who a quotation can go to, and the message that would be sent — what its dialog renders.</summary>
public sealed record QuotationRecipients(
    IReadOnlyList<DocumentContact> Contacts,
    string Subject,
    string Body,
    string AttachmentName,
    string? Blocked);

/// <summary>Server-side validation for emailing a job sheet — at least one contact.</summary>
public sealed class EmailDocumentRequestValidator : AbstractValidator<EmailDocumentRequest>
{
    public EmailDocumentRequestValidator() =>
        RuleFor(r => r.ContactIds).NotEmpty().WithMessage("Choose at least one contact to send to.");
}

/// <summary>Server-side validation for a new job card — company/customer required, at least one line.</summary>
public sealed class CreateJobCardRequestValidator : AbstractValidator<CreateJobCardRequest>
{
    public CreateJobCardRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.CustomerId).GreaterThan(0);
        RuleFor(r => r.Lines).NotEmpty().WithMessage("A job card needs at least one line.");
    }
}

/// <summary>Server-side validation for closing a job — non-negative cost and sell.</summary>
public sealed class CloseJobCardRequestValidator : AbstractValidator<CloseJobCardRequest>
{
    public CloseJobCardRequestValidator()
    {
        RuleFor(r => r.Cost).GreaterThanOrEqualTo(0m);
        RuleFor(r => r.Sell).GreaterThanOrEqualTo(0m);
    }
}

/// <summary>Server-side validation for a new supplier invoice — supplier/company required, money non-negative.</summary>
public sealed class CreateSupplierInvoiceRequestValidator : AbstractValidator<CreateSupplierInvoiceRequest>
{
    public CreateSupplierInvoiceRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.SupplierId).GreaterThan(0);
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("A supplier invoice needs an amount.");
        RuleFor(r => r.NetTotal).GreaterThanOrEqualTo(0m);
        RuleFor(r => r.TaxRatePercentage).InclusiveBetween(0m, 100m);
    }
}

/// <summary>Server-side validation for a supplier payment — a positive amount.</summary>
public sealed class RecordSupplierPaymentRequestValidator : AbstractValidator<RecordSupplierPaymentRequest>
{
    public RecordSupplierPaymentRequestValidator() => RuleFor(r => r.Amount).GreaterThan(0m);
}

/// <summary>Server-side validation for a new purchase order — the same line rules as an invoice.</summary>
public sealed class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.SupplierId).GreaterThan(0);
        RuleFor(r => r.DocumentDiscountPercent).InclusiveBetween(0m, 100m);

        RuleFor(r => r.Lines).NotEmpty().WithMessage("A purchase order needs at least one line.");

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

/// <summary>Who an invoice can go to, and the message that would be sent — what its dialog renders.</summary>
public sealed record InvoiceRecipients(
    IReadOnlyList<DocumentContact> Contacts,
    string Subject,
    string Body,
    string AttachmentName,
    string? Blocked);

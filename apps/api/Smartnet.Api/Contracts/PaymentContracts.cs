using FluentValidation;

namespace Smartnet.Api.Contracts;

// --- Customer receipts (Phase 7, slice 1) -------------------------------------------------------
//
// A receipt is money received from a customer, allocated across one or more open invoices. Its truth is
// the receivables ledger; the legacy payments rows + invoice_h.balance are dual-written for the still-live
// legacy reports. An idempotency key makes a resubmit return the first receipt, not take the money twice.

/// <summary>One allocation of a receipt — how much of it settles which invoice.</summary>
public sealed record CreateReceiptAllocationRequest(long InvoiceId, decimal Amount);

/// <summary>A whole receipt, posted at once — the total is the sum of its allocations.</summary>
public sealed record CreateCustomerReceiptRequest(
    long CompanyId,
    long CustomerId,
    DateOnly Date,
    string? Method,
    string? Reference,
    string IdempotencyKey,
    IReadOnlyList<CreateReceiptAllocationRequest> Allocations);

/// <summary>What the caller gets back. <paramref name="AlreadyExisted"/> is true when the idempotency key matched an existing receipt.</summary>
public sealed record CustomerReceiptCreatedResponse(long Id, decimal Amount, bool AlreadyExisted);

/// <summary>One row of the receipts list.</summary>
public sealed record CustomerReceiptSummary(
    long Id,
    DateOnly Date,
    string? CustomerName,
    decimal Amount,
    string? Method,
    string? Reference,
    int Invoices);

/// <summary>One allocation, for the read view.</summary>
public sealed record ReceiptAllocationLine(long InvoiceId, string? InvoiceNumber, decimal Amount);

/// <summary>One receipt, in full — the read view, with its per-invoice allocations.</summary>
public sealed record CustomerReceiptDetail(
    long Id,
    DateOnly Date,
    string? CompanyName,
    string? CustomerName,
    string? CustomerCode,
    decimal Amount,
    string? Method,
    string? Reference,
    int RowVersion,
    IReadOnlyList<ReceiptAllocationLine> Allocations);

/// <summary>One of a customer's open invoices — the picker a receipt is allocated over (new and legacy alike).</summary>
public sealed record OutstandingInvoiceLine(
    long InvoiceId,
    string Number,
    DateOnly Date,
    decimal Total,
    decimal Outstanding,
    string Origin);

/// <summary>Server-side validation for a new receipt — a customer, an idempotency key, and positive allocations.</summary>
public sealed class CreateCustomerReceiptRequestValidator : AbstractValidator<CreateCustomerReceiptRequest>
{
    public CreateCustomerReceiptRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.CustomerId).GreaterThan(0);
        RuleFor(r => r.IdempotencyKey).NotEmpty().WithMessage("A receipt needs an idempotency key.");
        RuleFor(r => r.Allocations).NotEmpty().WithMessage("A receipt must allocate to at least one invoice.");
        RuleForEach(r => r.Allocations).ChildRules(a =>
        {
            a.RuleFor(x => x.InvoiceId).GreaterThan(0);
            a.RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Each allocation must be a positive amount.");
        });
    }
}

using FluentValidation;

namespace Smartnet.Api.Contracts;

// --- Expenses & categories (Phase 7, slice 3) ---------------------------------------------------
//
// What was incurred, on the adopted legacy expense_tr, against a shared exp_cat_m category — recorded
// unpaid, and settled by payments that accumulate against it.

/// <summary>A new expense to record — what was incurred. It is recorded unpaid; payments settle it afterwards.</summary>
/// <param name="VatNumber">The vendor's VAT registration number off the bill — only asked for on a VAT expense.</param>
public sealed record CreateExpenseRequest(
    long CompanyId,
    long CategoryId,
    DateOnly Date,
    string? InvoiceNo,
    string Description,
    decimal NetAmount,
    decimal TaxRatePercentage,
    string? VatNumber,
    decimal Amount);

public sealed record ExpenseCreatedResponse(long Id, decimal Amount);

/// <summary>
/// A settlement against an expense — part of what it still owes, or all of it. When <c>Method</c> is
/// <c>Cheque</c>, the cheque fields raise a printable cheque for this payment.
/// </summary>
public sealed record RecordExpensePaymentRequest(
    decimal Amount,
    DateOnly Date,
    string? Method,
    string? Reference,
    string? ChequePayee = null,
    string? ChequeBank = null,
    string? ChequeNumber = null,
    DateOnly? ChequeDate = null,
    DateOnly? ChequeDueDate = null);

/// <summary>What a settlement returns — including the outstanding left, so the UI can show "settled" at zero.</summary>
public sealed record ExpensePaymentRecordedResponse(long ExpenseId, long PaymentId, decimal AmountPaid, decimal Outstanding);

/// <summary>One settlement against an expense.</summary>
/// <param name="Origin"><c>new</c> for a settlement this app recorded; <c>migrated</c> for one backfilled for an
/// expense that predates separate settlement (it was paid as it was entered).</param>
public sealed record ExpensePaymentSummary(
    long Id,
    DateOnly Date,
    decimal Amount,
    string? Method,
    string? Reference,
    int RowVersion,
    string Origin);

/// <summary>One row of the expense list.</summary>
/// <param name="Origin"><c>new</c> for an expense this app raised; <c>legacy</c> for an adopted one.</param>
/// <param name="PaidAmount">What has been settled so far — the sum of its live payments.</param>
/// <param name="Outstanding">What is still owed (<c>Amount</c> − <c>PaidAmount</c>), derived, never stored.</param>
public sealed record ExpenseSummary(
    long Id,
    DateOnly Date,
    string? InvoiceNo,
    long CategoryId,
    string? Category,
    string Description,
    decimal NetAmount,
    decimal TaxAmount,
    string? VatNumber,
    decimal Amount,
    decimal PaidAmount,
    decimal Outstanding,
    string? Method,
    string? Reference,
    string? CompanyName,
    int RowVersion,
    string Origin);

// ExpenseCategoryDto(long Id, string Name) already lives in ReportContracts (the report's category filter) —
// reused here for the category list.

/// <summary>Add or rename a category.</summary>
public sealed record SaveExpenseCategoryRequest(string Name);

/// <summary>Server-side validation for a new expense — company, category, description and a positive amount.</summary>
public sealed class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.CategoryId).GreaterThan(0).WithMessage("An expense needs a category.");
        RuleFor(r => r.Description).NotEmpty().WithMessage("An expense needs a description.").MaximumLength(100);
        RuleFor(r => r.NetAmount).GreaterThanOrEqualTo(0m);
        RuleFor(r => r.TaxRatePercentage).InclusiveBetween(0m, 100m);
        RuleFor(r => r.VatNumber).MaximumLength(100);
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("An expense needs an amount.");
    }
}

/// <summary>Server-side validation for a settlement — a positive amount, and a payee when it is a cheque.</summary>
public sealed class RecordExpensePaymentRequestValidator : AbstractValidator<RecordExpensePaymentRequest>
{
    public RecordExpensePaymentRequestValidator()
    {
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("A payment needs an amount.");
        RuleFor(r => r.Reference).MaximumLength(200);
        RuleFor(r => r.ChequePayee)
            .NotEmpty()
            .When(r => string.Equals(r.Method, "Cheque", StringComparison.OrdinalIgnoreCase))
            .WithMessage("A cheque payment needs a payee.");
    }
}

/// <summary>Server-side validation for a category — a non-empty name.</summary>
public sealed class SaveExpenseCategoryRequestValidator : AbstractValidator<SaveExpenseCategoryRequest>
{
    public SaveExpenseCategoryRequestValidator() =>
        RuleFor(r => r.Name).NotEmpty().WithMessage("A category needs a name.").MaximumLength(100);
}

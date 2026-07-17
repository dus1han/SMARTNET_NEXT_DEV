using FluentValidation;

namespace Smartnet.Api.Contracts;

// --- Expenses & categories (Phase 7, slice 3) ---------------------------------------------------
//
// A flat log of money spent, on the adopted legacy expense_tr, against a shared exp_cat_m category.

/// <summary>A new expense to record.</summary>
public sealed record CreateExpenseRequest(
    long CompanyId,
    long CategoryId,
    DateOnly Date,
    string Description,
    decimal Amount,
    string? Method,
    string? Reference);

public sealed record ExpenseCreatedResponse(long Id, decimal Amount);

/// <summary>One row of the expense list.</summary>
/// <param name="Origin"><c>new</c> for an expense this app raised; <c>legacy</c> for an adopted one.</param>
public sealed record ExpenseSummary(
    long Id,
    DateOnly Date,
    long CategoryId,
    string? Category,
    string Description,
    decimal Amount,
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
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("An expense needs an amount.");
    }
}

/// <summary>Server-side validation for a category — a non-empty name.</summary>
public sealed class SaveExpenseCategoryRequestValidator : AbstractValidator<SaveExpenseCategoryRequest>
{
    public SaveExpenseCategoryRequestValidator() =>
        RuleFor(r => r.Name).NotEmpty().WithMessage("A category needs a name.").MaximumLength(100);
}

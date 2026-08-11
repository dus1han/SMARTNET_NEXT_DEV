using FluentValidation;

namespace Smartnet.Api.Contracts;

// --- Cheque register (Phase 7, slice 2) ---------------------------------------------------------
//
// A standalone written record of a cheque, on the adopted legacy cheques table. No ledger, no balance.

/// <summary>A new cheque to record. <see cref="SupplierId"/> is set only for a <c>Supplier</c> entry.</summary>
public sealed record CreateChequeRequest(
    long CompanyId,
    string EntryType,
    string PayTo,
    long? SupplierId,
    string? Bank,
    string? ChequeNumber,
    decimal Amount,
    DateOnly? ChequeDate,
    DateOnly? DueDate);

public sealed record ChequeCreatedResponse(long Id, decimal Amount);

/// <summary>
/// The cheques becoming bankable in the next couple of working days — the dashboard warning.
/// </summary>
/// <param name="From">The first day of the window: today, as the server reckons it.</param>
/// <param name="To">
/// The last day: two <b>business</b> days out, so a Friday reaches Tuesday. See
/// <see cref="Smartnet.Domain.Documents.BusinessDays"/>.
/// </param>
/// <remarks>
/// Forward-looking only, and deliberately. A cheque has no cleared or banked state anywhere in this
/// system — the register is a written record, not a lifecycle — so nothing can ever mark one as dealt
/// with. An overdue list would therefore only ever grow, and a warning that is permanently on is a
/// warning nobody reads. What this answers is "what leaves the account next", which is actionable.
/// </remarks>
public sealed record ChequesDueSoon(
    DateOnly From,
    DateOnly To,
    int Count,
    IReadOnlyList<ChequeDueSoonRow> Cheques);

/// <summary>One cheque in the warning — enough to recognise it without opening the register.</summary>
public sealed record ChequeDueSoonRow(
    long Id,
    DateOnly DueDate,
    string PayTo,
    decimal Amount,
    string? CompanyName);

/// <summary>One row of the cheque list.</summary>
/// <param name="Origin"><c>new</c> for a cheque this app raised; <c>legacy</c> for an adopted one.</param>
/// <param name="Source">Where it came from — <c>Manual</c>, <c>Supplier payment</c> or <c>Expense</c>.</param>
public sealed record ChequeSummary(
    long Id,
    DateOnly? ChequeDate,
    DateOnly? DueDate,
    string PayTo,
    string? Bank,
    string? ChequeNumber,
    decimal Amount,
    string? CompanyName,
    string Source,
    string Origin,

    /// <summary>How many times it has been printed — counted from the audit trail, never stored.</summary>
    int PrintCount,

    /// <summary>The most recent print, or null if it has never been printed.</summary>
    DateTime? LastPrintedAt);

/// <summary>One cheque, in full — the read view.</summary>
public sealed record ChequeDetail(
    long Id,
    DateOnly? ChequeDate,
    DateOnly? DueDate,
    string PayTo,
    string EntryType,
    string? SupplierName,
    string? SupplierCode,
    string? Bank,
    string? ChequeNumber,
    decimal Amount,
    string? CompanyName,
    string Source,
    int RowVersion,
    string Origin,

    /// <summary>How many times it has been printed — counted from the audit trail, never stored.</summary>
    int PrintCount,

    /// <summary>The most recent print, or null if it has never been printed.</summary>
    DateTime? LastPrintedAt);

/// <summary>Server-side validation for a new cheque — company, payee and a positive amount.</summary>
public sealed class CreateChequeRequestValidator : AbstractValidator<CreateChequeRequest>
{
    public CreateChequeRequestValidator()
    {
        RuleFor(r => r.CompanyId).GreaterThan(0);
        RuleFor(r => r.PayTo).NotEmpty().WithMessage("A cheque needs a payee.");
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("A cheque needs an amount.");
        RuleFor(r => r.EntryType)
            .Must(e => e is "Manual" or "Supplier")
            .WithMessage("Entry must be Manual or Supplier.");
        RuleFor(r => r.SupplierId).NotNull().When(r => r.EntryType == "Supplier")
            .WithMessage("A Supplier cheque needs a supplier.");
    }
}

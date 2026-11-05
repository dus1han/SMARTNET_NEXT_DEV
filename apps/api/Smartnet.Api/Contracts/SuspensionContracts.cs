using FluentValidation;

namespace Smartnet.Api.Contracts;

// --- The suspended account ------------------------------------------------------------------------
//
// An invoice's outstanding moved off it — so it shows nowhere an outstanding figure does — and later
// brought back, on its own or with the payment that settles it.

/// <summary>Move part or all of an invoice's outstanding to the suspended account.</summary>
public sealed record SuspendReceivableRequest(long InvoiceId, decimal Amount, DateOnly Date, string Reason);

public sealed class SuspendReceivableRequestValidator : AbstractValidator<SuspendReceivableRequest>
{
    public SuspendReceivableRequestValidator()
    {
        RuleFor(r => r.InvoiceId).GreaterThan(0);
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("The amount to suspend must be positive.");

        // It hides a debt, so the record must say why.
        RuleFor(r => r.Reason).NotEmpty().MinimumLength(5).MaximumLength(500)
            .WithMessage("Say why the amount is being suspended (at least 5 characters).");
    }
}

public sealed record SuspensionCreatedResponse(long Id, decimal Amount);

/// <summary>Bring the whole suspended amount back onto its invoice.</summary>
public sealed record ReinstateSuspensionRequest(int RowVersion, string? Note);

/// <summary>Bring the suspended amount back and record the payment against it, in one act.</summary>
public sealed record SettleSuspensionRequest(
    int RowVersion,
    decimal Amount,
    DateOnly Date,
    string? Method,
    string? Reference,
    string IdempotencyKey);

public sealed class SettleSuspensionRequestValidator : AbstractValidator<SettleSuspensionRequest>
{
    public SettleSuspensionRequestValidator()
    {
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("The payment must be a positive amount.");
        RuleFor(r => r.IdempotencyKey).NotEmpty().WithMessage("A payment needs an idempotency key.");
    }
}

/// <summary>One row of the suspended account.</summary>
/// <param name="Status"><c>Suspended</c>, <c>Reinstated</c> or <c>Settled</c>.</param>
/// <param name="Origin"><c>new</c> for an invoice this app raised; <c>legacy</c> for one from the old system.</param>
public sealed record SuspendedReceivableSummary(
    long Id,
    string? CompanyName,
    long CustomerId,
    string? CustomerName,
    string? CustomerCode,
    long InvoiceId,
    string InvoiceNumber,
    DateOnly? InvoiceDate,
    decimal InvoiceTotal,
    decimal Amount,
    DateOnly SuspendedOn,
    string Reason,
    string? SuspendedBy,
    string Status,
    DateTime? ReinstatedAt,
    string? ReinstateNote,
    long? ReceiptId,
    int RowVersion,
    string Origin);

using FluentValidation;

namespace Smartnet.Api.Contracts;

/// <param name="Customers">The customer codes to send an outstanding statement to — the selection off
/// the outstanding report.</param>
/// <param name="ContactIds">
/// Which of the customer's saved contacts to send to. Only meaningful for a single customer — a bulk
/// run has no one contact list to choose from, and each customer there falls back to their own
/// default address. Ignored when more than one customer is selected.
/// </param>
public sealed record DunningRequest(IReadOnlyList<string> Customers, IReadOnlyList<long>? ContactIds = null);

/// <summary>Who a single customer's statement can go to — the same shape the job sheet dialog uses.</summary>
public sealed record StatementRecipients(IReadOnlyList<DocumentContact> Contacts, string? Blocked);

/// <param name="Queued">How many statements were accepted onto the queue and logged.</param>
/// <param name="Skipped">Selected customers with no email address on file — nothing to send.</param>
/// <param name="SendEnabled">Whether the company's mail kill switch is on. When false (the default),
/// the messages are logged but nothing is sent — the gate that holds bulk dunning until the balances
/// are corrected.</param>
public sealed record DunningResponse(int Queued, int Skipped, bool SendEnabled, string Message);

public sealed class DunningRequestValidator : AbstractValidator<DunningRequest>
{
    public DunningRequestValidator()
    {
        RuleFor(r => r.Customers)
            .NotEmpty().WithMessage("Select at least one customer.")
            // A sane ceiling: the whole debtor book is ~223 customers. A request for thousands is a bug
            // or an attack, not a dunning run.
            .Must(c => c.Count <= 500).WithMessage("Too many customers in one request.");
    }
}

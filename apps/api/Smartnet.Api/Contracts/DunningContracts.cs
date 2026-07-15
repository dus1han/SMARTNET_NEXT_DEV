using FluentValidation;

namespace Smartnet.Api.Contracts;

/// <param name="Customers">The customer codes to send an outstanding statement to — the selection off
/// the outstanding report.</param>
public sealed record DunningRequest(IReadOnlyList<string> Customers);

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

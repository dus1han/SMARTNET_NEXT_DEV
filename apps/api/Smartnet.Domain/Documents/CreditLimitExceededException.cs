namespace Smartnet.Domain.Documents;

/// <summary>
/// An invoice — cash or credit — would take a customer past their credit limit, and enforcement is on.
/// </summary>
/// <remarks>
/// Enforced <b>server-side, before the save</b> — unlike the legacy check, which was a client-side
/// advisory a direct POST bypassed, and which ran on service invoices only so the same customer could
/// blow their limit on an item invoice unnoticed. It gates <b>both cash and credit</b> invoices, and
/// measures against the <b>derived</b> ledger balance, not a stored one.
/// </remarks>
public sealed class CreditLimitExceededException(decimal limit, decimal currentBalance, decimal invoiceTotal)
    : Exception(
        $"This invoice would take the customer to {currentBalance + invoiceTotal:N2}, past their "
        + $"credit limit of {limit:N2} ({currentBalance:N2} already outstanding).")
{
    public decimal Limit { get; } = limit;
    public decimal CurrentBalance { get; } = currentBalance;
    public decimal InvoiceTotal { get; } = invoiceTotal;
}

namespace Smartnet.Domain.Ledger;

/// <summary>
/// Builds the posting lines for the standard accounts, so a money-event service composes a GL posting from
/// named parts rather than repeating codes/names/types. Cash vs Bank is chosen by the event's method.
/// </summary>
public static class GlChart
{
    public static GlPostingLine Cash(decimal debit, decimal credit) =>
        new(GlAccountCodes.Cash, "Cash", AccountType.Asset, true, debit, credit);

    public static GlPostingLine Bank(decimal debit, decimal credit) =>
        new(GlAccountCodes.Bank, "Bank", AccountType.Asset, true, debit, credit);

    /// <summary>The cash or bank account, by payment method — <c>Cash</c> → Cash, anything else → Bank.</summary>
    public static GlPostingLine CashOrBank(string? method, decimal debit, decimal credit) =>
        string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase) ? Cash(debit, credit) : Bank(debit, credit);

    public static GlPostingLine Receivable(decimal debit, decimal credit) =>
        new(GlAccountCodes.AccountsReceivable, "Accounts Receivable", AccountType.Asset, false, debit, credit);

    public static GlPostingLine Payable(decimal debit, decimal credit) =>
        new(GlAccountCodes.AccountsPayable, "Accounts Payable", AccountType.Liability, false, debit, credit);

    public static GlPostingLine Sales(decimal debit, decimal credit) =>
        new(GlAccountCodes.Sales, "Sales", AccountType.Income, false, debit, credit);

    public static GlPostingLine Purchases(decimal debit, decimal credit) =>
        new(GlAccountCodes.Purchases, "Purchases", AccountType.Expense, false, debit, credit);

    public static GlPostingLine OutputVat(decimal debit, decimal credit) =>
        new(GlAccountCodes.OutputVat, "Output VAT", AccountType.Liability, false, debit, credit);

    public static GlPostingLine InputVat(decimal debit, decimal credit) =>
        new(GlAccountCodes.InputVat, "Input VAT", AccountType.Asset, false, debit, credit);

    public static GlPostingLine ExpenseCategory(long categoryId, string categoryName, decimal debit, decimal credit) =>
        new(GlAccountCodes.ExpenseCategory(categoryId), categoryName, AccountType.Expense, false, debit, credit);
}

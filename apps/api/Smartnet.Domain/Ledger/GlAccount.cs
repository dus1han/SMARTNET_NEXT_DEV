using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Ledger;

/// <summary>The five account classes of a double-entry chart. The sign convention lives in the trial balance.</summary>
public enum AccountType
{
    /// <summary>Debit-normal: cash, bank, receivables, input VAT.</summary>
    Asset,

    /// <summary>Credit-normal: payables, output VAT.</summary>
    Liability,

    /// <summary>Credit-normal: sales.</summary>
    Income,

    /// <summary>Debit-normal: purchases, expenses.</summary>
    Expense,

    /// <summary>Credit-normal: opening balances / retained earnings.</summary>
    Equity,
}

/// <summary>
/// One account in the general-ledger chart of accounts (GL slice 1) — per company.
/// </summary>
/// <remarks>
/// The legacy app had no general ledger; this is new. Each company has its own chart, seeded with a standard
/// set (see <see cref="GlAccountCodes"/>) and extended with one expense account per expense category. A GL
/// posting classifies a money event into these accounts; an account's balance is the sum of its lines
/// (derived, never stored).
/// </remarks>
public class GlAccount : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The company this account belongs to — each entity keeps its own books.</summary>
    public long CompanyId { get; set; }

    /// <summary>A stable code, unique within the company (e.g. <c>1100</c> for receivables) — see <see cref="GlAccountCodes"/>.</summary>
    public string Code { get; set; } = null!;

    /// <summary>The account name shown on the trial balance and P&amp;L.</summary>
    public string Name { get; set; } = null!;

    public AccountType Type { get; set; }

    /// <summary>True for the Cash and Bank accounts — the ones the cash-position report sums.</summary>
    public bool IsCashOrBank { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>The well-known account codes the posting engine resolves events to (per company).</summary>
public static class GlAccountCodes
{
    public const string Cash = "1000";
    public const string Bank = "1010";
    public const string AccountsReceivable = "1100";
    public const string InputVat = "1200";
    public const string AccountsPayable = "2000";
    public const string OutputVat = "2100";
    public const string Sales = "4000";
    public const string Purchases = "5000";

    /// <summary>The expense account for a category — one per category (a categorised P&amp;L).</summary>
    public static string ExpenseCategory(long categoryId) => $"5-{categoryId}";
}

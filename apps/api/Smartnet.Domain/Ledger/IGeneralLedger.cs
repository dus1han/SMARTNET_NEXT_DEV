namespace Smartnet.Domain.Ledger;

/// <summary>One line of a posting — an account (resolved/created by code) and its debit or credit.</summary>
public sealed record GlPostingLine(string AccountCode, string AccountName, AccountType Type, bool IsCashOrBank, decimal Debit, decimal Credit);

/// <summary>A whole balanced posting for one money event.</summary>
public sealed record GlPosting(
    long CompanyId,
    DateOnly Date,
    string SourceType,
    long SourceId,
    string? Description,
    IReadOnlyList<GlPostingLine> Lines);

/// <summary>
/// Posts money events to the general ledger (GL slice 2). Double-entry: every posting balances
/// (Σ debit = Σ credit) and is idempotent — an event (SourceType + SourceId) posts exactly once.
/// </summary>
/// <remarks>
/// Called from inside each money-event service's transaction, so the event and its GL entry commit together.
/// The engine resolves each line's account by code within the company, creating it (from the line's name/type)
/// if it does not exist yet — which is how a new expense category gets its expense account.
/// </remarks>
public interface IGeneralLedger
{
    /// <summary>Posts a balanced entry for an event, unless one already exists for it. Returns true if it posted.</summary>
    Task<bool> PostAsync(GlPosting posting, CancellationToken cancellationToken = default);
}

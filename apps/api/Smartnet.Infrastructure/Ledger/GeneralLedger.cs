using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Ledger;

/// <inheritdoc cref="IGeneralLedger"/>
public sealed class GeneralLedger : IGeneralLedger
{
    private readonly SmartnetDbContext _db;

    public GeneralLedger(SmartnetDbContext db) => _db = db;

    public async Task<bool> PostAsync(GlPosting posting, CancellationToken cancellationToken = default)
    {
        // Idempotent — one event posts exactly once (the unique index on (source_type, source_id) backs this).
        var alreadyPosted = await _db.GlEntries
            .AnyAsync(e => e.SourceType == posting.SourceType && e.SourceId == posting.SourceId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyPosted)
        {
            return false;
        }

        // Drop empty lines (e.g. a zero VAT line), then require the entry to balance.
        var lines = posting.Lines.Where(l => l.Debit != 0m || l.Credit != 0m).ToList();
        if (lines.Count == 0)
        {
            return false;
        }

        var debits = lines.Sum(l => l.Debit);
        var credits = lines.Sum(l => l.Credit);
        if (debits != credits)
        {
            throw new InvalidOperationException(
                $"GL posting for {posting.SourceType} {posting.SourceId} does not balance: debits {debits:0.00} ≠ credits {credits:0.00}.");
        }

        var entry = new GlEntry
        {
            CompanyId = posting.CompanyId,
            Date = posting.Date,
            SourceType = posting.SourceType,
            SourceId = posting.SourceId,
            Description = posting.Description,
        };

        foreach (var line in lines)
        {
            var accountId = await ResolveAccountAsync(posting.CompanyId, line, cancellationToken).ConfigureAwait(false);
            entry.Lines.Add(new GlLine { AccountId = accountId, Debit = line.Debit, Credit = line.Credit });
        }

        _db.GlEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Resolves an account by (company, code), creating it from the line's name/type if it does not exist yet.</summary>
    private async Task<long> ResolveAccountAsync(long companyId, GlPostingLine line, CancellationToken cancellationToken)
    {
        var existing = await _db.GlAccounts
            .Where(a => a.CompanyId == companyId && a.Code == line.AccountCode)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is { } id)
        {
            return id;
        }

        var account = new GlAccount
        {
            CompanyId = companyId,
            Code = line.AccountCode,
            Name = line.AccountName,
            Type = line.Type,
            IsCashOrBank = line.IsCashOrBank,
        };
        _db.GlAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return account.Id;
    }
}

using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IQuotationDeleter"/>
public sealed class QuotationDeleter : IQuotationDeleter
{
    private readonly SmartnetDbContext _db;
    private readonly ILegacyQuotationAdopter _adopter;
    private readonly TimeProvider _time;

    public QuotationDeleter(SmartnetDbContext db, ILegacyQuotationAdopter adopter, TimeProvider time)
    {
        _db = db;
        _adopter = adopter;
        _time = time;
    }

    public async Task<QuotationDeleted> DeleteAsync(long quotationId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var quotation = await _db.Quotations
            .IgnoreQueryFilters()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Quotation {quotationId} does not exist.");

        _db.Entry(quotation).Property(q => q.RowVersion).OriginalValue = expectedRowVersion;

        // Adopt a legacy quotation first (materialise + version-1) so the void is a change on a real
        // new-side document; the concurrency check fires on that first save. A no-op for a new quotation.
        await _adopter.MaterialiseInCurrentTransactionAsync(quotation, cancellationToken).ConfigureAwait(false);

        // Soft delete by setting deleted_at directly (the interceptor's WasDeleted path) — an UPDATE, not a
        // Remove(). A quotation has no ledger or stock to reverse.
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var line in quotation.Lines.Where(l => l.DeletedAt is null))
        {
            line.DeletedAt = now;
        }
        quotation.DeletedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new QuotationDeleted(quotation.Id, quotation.Number);
    }
}

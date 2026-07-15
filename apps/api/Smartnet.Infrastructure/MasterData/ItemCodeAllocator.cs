using System.Globalization;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.MasterData;

/// <inheritdoc cref="IItemCodeAllocator"/>
public sealed class ItemCodeAllocator : IItemCodeAllocator
{
    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public ItemCodeAllocator(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        // Exactly what the legacy app does (ItemController.saveitem): insert a row into item_seq and
        // take the auto-increment it produced, prefixing it with "I-". SequenceCode owns the shared
        // connection-pinning that makes LAST_INSERT_ID() trustworthy.
        var today = _time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return SequenceCode.NextAsync(_db, "item_seq", "I-", today, cancellationToken);
    }
}

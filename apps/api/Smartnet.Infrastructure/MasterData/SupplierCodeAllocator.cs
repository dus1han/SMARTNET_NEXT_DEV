using System.Globalization;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.MasterData;

/// <inheritdoc cref="ISupplierCodeAllocator"/>
public sealed class SupplierCodeAllocator : ISupplierCodeAllocator
{
    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public SupplierCodeAllocator(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        // Exactly what the legacy app does (SupplierController.savesupplier): insert a row into
        // sup_seq and take the auto-increment it produced, prefixing it with "S-". The shared
        // SequenceCode owns the connection-pinning that makes LAST_INSERT_ID() trustworthy.
        var today = _time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return SequenceCode.NextAsync(_db, "sup_seq", "S-", today, cancellationToken);
    }
}

using System.Globalization;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.MasterData;

/// <inheritdoc cref="ICustomerCodeAllocator"/>
public sealed class CustomerCodeAllocator : ICustomerCodeAllocator
{
    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public CustomerCodeAllocator(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        // Exactly what the legacy app does (CustomerController.savecustomer): insert a row into
        // cus_seq and take the auto-increment it produced. The `dt` column is the only one, and it
        // holds the allocation date — kept because the old app writes it, and harmless here. The
        // connection-pinning that makes LAST_INSERT_ID() trustworthy lives in SequenceCode, shared
        // with the supplier allocator.
        var today = _time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return SequenceCode.NextAsync(_db, "cus_seq", "C-", today, cancellationToken);
    }
}

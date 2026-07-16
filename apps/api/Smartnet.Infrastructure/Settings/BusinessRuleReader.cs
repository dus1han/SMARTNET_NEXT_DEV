using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Settings;

/// <inheritdoc cref="IBusinessRuleReader"/>
public sealed class BusinessRuleReader : IBusinessRuleReader
{
    private readonly SmartnetDbContext _db;

    public BusinessRuleReader(SmartnetDbContext db) => _db = db;

    public async Task<string> ResolveAsync(long companyId, string key, CancellationToken cancellationToken = default)
    {
        var stored = await _db.AppSettings
            .Where(s => s.Key == key && (s.CompanyId == null || s.CompanyId == companyId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Company override first, then the global value, then the built-in default. The last of those
        // always exists for a known key, so an unknown key is a programming error, not a config gap.
        return stored.FirstOrDefault(s => s.CompanyId == companyId)?.Value
            ?? stored.FirstOrDefault(s => s.CompanyId == null)?.Value
            ?? (BusinessRules.Defaults.TryGetValue(key, out var fallback)
                ? fallback
                : throw new InvalidOperationException($"'{key}' is not a known business rule."));
    }
}

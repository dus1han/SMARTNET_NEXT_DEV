using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Smartnet.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time — never by the running application, which builds
/// its context through <see cref="DependencyInjection.AddSmartnetPersistence"/>.
/// </summary>
/// <remarks>
/// Because <see cref="SmartnetServerVersion"/> is pinned rather than auto-detected, generating a
/// migration needs no live database and therefore no real credentials. So the fallback below is
/// a connection string that is syntactically valid and deliberately cannot connect to anything:
/// <c>dotnet ef migrations add</c> works offline, and <c>dotnet ef database update</c> fails
/// loudly rather than silently reaching for the wrong server.
/// </remarks>
public sealed class SmartnetDbContextFactory : IDesignTimeDbContextFactory<SmartnetDbContext>
{
    public SmartnetDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Smartnet")
            ?? "Server=localhost;Database=smartnet_invsys_dev;User=design-time;Password=;";

        var options = new DbContextOptionsBuilder<SmartnetDbContext>()
            .UseMySql(connectionString, SmartnetServerVersion.Value)
            .Options;

        return new SmartnetDbContext(options);
    }
}

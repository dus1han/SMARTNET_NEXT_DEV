using Microsoft.EntityFrameworkCore;

namespace Smartnet.Infrastructure.Persistence;

/// <summary>
/// The database server we target, stated rather than discovered.
/// </summary>
/// <remarks>
/// The obvious alternative, <c>ServerVersion.AutoDetect(connectionString)</c>, opens a blocking
/// connection during service registration — so the API cannot start while the database is
/// briefly unreachable, and <c>dotnet ef migrations add</c> cannot run without a live server at
/// all. Worse, it means the provider's behaviour is decided by whatever host happens to answer,
/// so a dev machine on MySQL 8 and a production box on MariaDB 10.11 can generate subtly
/// different SQL from identical code.
/// <para>
/// It is pinned to the production server (see DEVELOPMENT.md §1: MariaDB 10.11, existing
/// server, unchanged). If that is ever upgraded, this is the line to change.
/// </para>
/// </remarks>
public static class SmartnetServerVersion
{
    public static ServerVersion Value { get; } = new MariaDbServerVersion(new Version(10, 11, 0));
}

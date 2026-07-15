using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Persistence;
using Testcontainers.MySql;

namespace Smartnet.Tests.Auditing;

/// <summary>
/// A throwaway MariaDB container, per DEVELOPMENT.md §9.
/// </summary>
/// <remarks>
/// The audit spine's guarantees are transactional and provider-specific — atomicity, JSON
/// columns, an append-only grant. An in-memory provider would happily pass tests that a real
/// database would fail, which is the one outcome worse than having no tests.
/// </remarks>
public sealed class AuditFixture : IAsyncLifetime
{
    // The production server, not "some MySQL": the audit spine relies on JSON columns and on
    // transactional DDL-free behaviour that differ between MariaDB and MySQL.
    private readonly MySqlContainer _container = new MySqlBuilder("mariadb:10.11")
        .WithDatabase("smartnet_invsys_dev")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Deliberately the BASE context, not TestDbContext. EF matches migrations to the context
        // type they were generated against ([DbContext(typeof(SmartnetDbContext))]), so migrating
        // a derived context finds no migrations and — silently — creates nothing at all.
        await using var db = new SmartnetDbContext(Options<SmartnetDbContext>());

        // The legacy tables first, in their real pre-migration shape. Our migrations ALTER them
        // additively rather than creating them — in production they already exist — so a fresh
        // container has to be given the starting point before it can be migrated from it.
        foreach (var ddl in LegacySchema.All)
        {
            await db.Database.ExecuteSqlRawAsync(ddl);
        }

        // Now the migrations: audit_log, document_versions, and the additive columns and primary
        // key on user_m. Running them here means the tests exercise the *real* migration path,
        // not a hand-built schema that happens to resemble its output.
        await db.Database.MigrateAsync();

        // The table for the stand-in entity below. Not a migration: it exists only in tests.
        await db.Database.ExecuteSqlAsync($"""
            CREATE TABLE widgets (
              id           BIGINT PRIMARY KEY AUTO_INCREMENT,
              name         VARCHAR(100) NOT NULL,
              secret       VARCHAR(100) NULL,
              created_by   BIGINT NULL,
              created_at   DATETIME NOT NULL,
              updated_by   BIGINT NULL,
              updated_at   DATETIME NULL,
              deleted_by   BIGINT NULL,
              deleted_at   DATETIME NULL,
              row_version  INT NOT NULL
            )
            """);
    }

    public TestDbContext CreateContext(IChangeContext change) =>
        new(Options<SmartnetDbContext>(change));

    private DbContextOptions<TContext> Options<TContext>(IChangeContext? change = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseMySql(ConnectionString, SmartnetServerVersion.Value,
                mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName));

        if (change is not null)
        {
            builder.AddInterceptors(new AuditSaveChangesInterceptor(change, TimeProvider.System));
        }

        return builder.Options;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(nameof(AuditCollection))]
public sealed class AuditCollection : ICollectionFixture<AuditFixture>;

/// <summary>
/// A stand-in for "any entity a later phase adds". The claim under test is that auditing is
/// generic — that a developer gets it without writing any audit code — so the test subject
/// deliberately has no audit code in it.
/// </summary>
public sealed class Widget : IAuditable
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Named to hit the redaction list via <c>PasswordHash</c>-style matching.</summary>
    public string? PasswordHash { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class TestDbContext : SmartnetDbContext
{
    public TestDbContext(DbContextOptions<SmartnetDbContext> options) : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Widget>(e =>
        {
            e.ToTable("widgets");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasColumnName("id");
            e.Property(w => w.Name).HasColumnName("name");
            e.Property(w => w.PasswordHash).HasColumnName("secret");
            e.Property(w => w.CreatedBy).HasColumnName("created_by");
            e.Property(w => w.CreatedAt).HasColumnName("created_at");
            e.Property(w => w.UpdatedBy).HasColumnName("updated_by");
            e.Property(w => w.UpdatedAt).HasColumnName("updated_at");
            e.Property(w => w.DeletedBy).HasColumnName("deleted_by");
            e.Property(w => w.DeletedAt).HasColumnName("deleted_at");

            // The concurrency token: the second of two concurrent editors must fail loudly.
            e.Property(w => w.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        });
    }
}

public sealed class FakeChangeContext : IChangeContext
{
    public long? UserId { get; init; }
    public long? CompanyId { get; init; }
    public string? Reason { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? CorrelationId { get; init; }
}

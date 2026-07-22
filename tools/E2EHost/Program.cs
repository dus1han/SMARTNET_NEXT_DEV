using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Identity;
using Smartnet.Infrastructure.Persistence;
using Testcontainers.MySql;

// -----------------------------------------------------------------------------------------------
// The E2E database host (PHASE-5-PLAN slice 6).
//
// Stands up a throwaway MariaDB — bootstrapped with the EXACT same LegacySchema + EF migrations the
// API test-suite uses, so the E2E runs against the production schema, not a hand-built lookalike —
// seeds the minimum a login-and-invoice flow needs (a user with every permission, a company with a
// tax rate and an invoice number series, a customer and an item), prints the connection string, and
// blocks until it is killed. Playwright's global-setup spawns this, points the real API at the
// printed connection string, and drives the browser against it. Nothing here touches a real database.
// -----------------------------------------------------------------------------------------------

// The credentials and seed identifiers the Playwright spec relies on. Kept here as the single source
// of truth; the spec hard-codes the same strings.
const string SeedUser = "e2e";
const string SeedPassword = "E2Epassw0rd!";
const string SeedCustomer = "E2E Customer";
const string SeedItem = "E2E Widget";
const string SeedSupplier = "E2E Supplier";

await using var container = new MySqlBuilder("mariadb:10.11")
    .WithDatabase("smartnet_invsys_dev")
    .Build();

Console.WriteLine("E2E: starting MariaDB container…");
await container.StartAsync();
var conn = container.GetConnectionString();

// --- Bootstrap: the legacy baseline, then the real migrations (no interceptor — this is DDL). -----
Console.WriteLine("E2E: applying legacy schema + migrations…");
await using (var db = new SmartnetDbContext(BaseOptions(conn)))
{
    foreach (var ddl in LegacySchema.All)
    {
        await db.Database.ExecuteSqlRawAsync(ddl);
    }

    await db.Database.MigrateAsync();
}

// --- Seed: exactly what login + create-an-invoice needs. -----------------------------------------
Console.WriteLine("E2E: seeding…");
var change = new SeedChangeContext();
await using (var db = new SmartnetDbContext(SeedOptions(conn, change)))
{
    var company = new Company { Name = "E2E Trading Co", VatCode = "1", IsVatRegistered = true };
    db.Companies.Add(company);
    await db.SaveChangesAsync();

    db.TaxRates.Add(new TaxRate
    {
        CompanyId = company.Id,
        Name = "VAT 18%",
        Percentage = 18m,
        EffectiveFrom = new DateOnly(2024, 1, 1),
        IsDefault = true,
    });

    // Numbering series: an invoice series (Phase 5) and the Phase 6 PO and job-card series. Supplier
    // invoices carry the supplier's own reference and are not numbered, so they need no series.
    //
    // A missing series is a 500, not a quiet fallback — DocumentNumberAllocator refuses to invent one
    // rather than reissue numbers that are already printed on documents. So every document type a spec
    // raises needs one here, and the quotation series was absent until a spec tried to raise one.
    db.DocumentSeries.Add(new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = "E2E-", NextNumber = 1, Padding = 0 });
    db.DocumentSeries.Add(new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.Quotation, Prefix = "E2EQ-", NextNumber = 1, Padding = 0 });
    db.DocumentSeries.Add(new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.PurchaseOrder, Prefix = "E2EPO-", NextNumber = 1, Padding = 0 });
    db.DocumentSeries.Add(new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.JobCard, Prefix = "E2EJOB-", NextNumber = 1, Padding = 0 });

    db.Customers.Add(new Customer { Code = "E2E-CUST", Name = SeedCustomer, CreditLimit = 0m });
    db.Items.Add(new Item { Code = "E2E-ITEM", Name = SeedItem, SellingPrice = 100m, Cost = 60m });
    db.Suppliers.Add(new Supplier { Code = "E2E-SUP", Name = SeedSupplier });

    // A user who can do everything, so the flow is never blocked by a missing permission. A single
    // global role carrying the whole permission catalogue; a global (null-company) assignment grants
    // access to every company (CompanyAccessService).
    var user = new User
    {
        Username = SeedUser,
        Name = "E2E Runner",
        PasswordHash = new Argon2PasswordHasher().Hash(SeedPassword),
        MustChangePassword = false,
        Ustat = "Active",
        Addedby = string.Empty,
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    // AdministratorGrant, not All. Permissions.All is documented as *not a valid grant*: it holds both
    // dashboards, and holding both is a contradiction rather than a superset — the operations dashboard
    // is defined by what it withholds. The API enforces that on every permission save, so a user seeded
    // from All is in a state the app itself refuses to write back, and the first spec to save this
    // user's permissions got a 400 saying so. The same mistake the system roles had, in the one place
    // that fix did not reach.
    var role = new Role { Name = "E2E Admin", CompanyId = null, IsSystem = false };
    foreach (var permission in Permissions.AdministratorGrant)
    {
        role.Permissions.Add(new RolePermission { Permission = permission });
    }
    db.Roles.Add(role);
    await db.SaveChangesAsync();

    db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CompanyId = null });
    await db.SaveChangesAsync();
}

// --- Ready. Print the connection string and block until the parent kills us. ----------------------
Console.WriteLine($"E2E_CONN={conn}");
Console.WriteLine("E2E_READY");
Console.Out.Flush();

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => done.TrySetResult();
// If the parent (global-setup) dies, our stdin closes — exit so the container is not orphaned.
// (Testcontainers' reaper also removes it if we are hard-killed.)
_ = Task.Run(() =>
{
    try { while (Console.In.ReadLine() is not null) { } } catch { /* stdin closed */ }
    done.TrySetResult();
});

await done.Task;
Console.WriteLine("E2E: shutting down, disposing container…");

// --- Options helpers ------------------------------------------------------------------------------

static DbContextOptions<SmartnetDbContext> BaseOptions(string conn) =>
    new DbContextOptionsBuilder<SmartnetDbContext>()
        .UseMySql(conn, SmartnetServerVersion.Value,
            mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName))
        .Options;

static DbContextOptions<SmartnetDbContext> SeedOptions(string conn, IChangeContext change) =>
    new DbContextOptionsBuilder<SmartnetDbContext>()
        .UseMySql(conn, SmartnetServerVersion.Value,
            mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName))
        .AddInterceptors(new AuditSaveChangesInterceptor(change, TimeProvider.System))
        .Options;

/// <summary>The change context the seed's audit rows are attributed to — a system actor.</summary>
file sealed class SeedChangeContext : IChangeContext
{
    public long? UserId => 1;
    public long? CompanyId => null;
    public string? Reason => "E2E seed";
    public string? IpAddress => null;
    public string? UserAgent => null;
    public string? CorrelationId => null;
}

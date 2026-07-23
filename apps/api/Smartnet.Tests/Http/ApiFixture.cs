using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;
using Testcontainers.MySql;

namespace Smartnet.Tests.Http;

/// <summary>
/// The API as an HTTP surface — the real pipeline, over a real socket, against a real database.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Every other test in this project calls a service or a DbContext
/// directly, which means nothing exercised the things that only exist between the socket and the
/// controller: authentication, the permission policies, the must-change-password gate, the correlation
/// id, the change-reason filter, CORS, rate limiting and the global exception handler. All of that sits
/// in <c>Program.cs</c> and was proven only by a handful of browser tests — so a middleware could be
/// deleted and 660-odd tests would still pass.</para>
///
/// <para><b>A real database, not a stub.</b> Same reasoning as <see cref="AuditFixture"/>: the pipeline
/// under test ends in EF Core, and an in-memory provider would happily pass requests a real database
/// refuses. The container is started once for the whole collection.</para>
///
/// <para><b>Development environment, deliberately.</b> That is what the E2E harness runs and what a
/// developer runs, so it is the configuration most likely to drift unnoticed. Where a test cares about
/// Production behaviour instead — the auth cookie's Secure flag, say — it says so.</para>
/// </remarks>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mariadb:10.11")
        .WithDatabase("smartnet_invsys_dev")
        .Build();

    /// <summary>The seeded user: every permission, so a 403 in a test is never an accident of setup.</summary>
    public const string Username = "http-tests";
    public const string Password = "HttpT3sts!pass";

    public long CompanyId { get; private set; }
    public long UserId { get; private set; }

    /// <summary>The container's connection string, for a test that seeds the database directly.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Set as environment variables, not through ConfigureAppConfiguration.
        //
        // Program.cs uses top-level statements and reads the connection string inline, while it is
        // still constructing the builder. WebApplicationFactory applies ConfigureAppConfiguration
        // through a DeferredHostBuilder, which is too late — the throw happens first. Environment
        // variables are read by WebApplication.CreateBuilder itself, so they are in place before the
        // first line of Program runs. (`__` is the section separator: ConnectionStrings__Smartnet
        // binds to ConnectionStrings:Smartnet.)
        Environment.SetEnvironmentVariable("ConnectionStrings__Smartnet", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "http-tests-only-signing-key-long-enough-to-satisfy-validation");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "smartnet");
        Environment.SetEnvironmentVariable("Jwt__Audience", "smartnet");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "30");
        Environment.SetEnvironmentVariable("Cors__WebOrigin", CorsOrigin);

        // Program refuses to start without somewhere to keep the Data Protection key ring, so that a
        // deploy cannot quietly leave it inside a container it is about to replace. Tests want the
        // opposite lifetime — a fresh ring per run, thrown away with the rest of the temp directory.
        Environment.SetEnvironmentVariable(
            "DataProtection__KeyPath",
            Path.Combine(Path.GetTempPath(), $"smartnet-tests-keys-{Guid.NewGuid():N}"));

        await using var db = new SmartnetDbContext(
            new DbContextOptionsBuilder<SmartnetDbContext>()
                .UseMySql(_container.GetConnectionString(), SmartnetServerVersion.Value,
                    mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName))
                .Options);

        // The legacy tables in their pre-migration shape, then the migrations over them — the same
        // starting point production had, so the schema under test is the one the app ships against.
        foreach (var ddl in LegacySchema.All)
        {
            await db.Database.ExecuteSqlRawAsync(ddl);
        }

        await db.Database.MigrateAsync();
        await SeedAsync(db);

        // Before any test runs, so the rate-limit test cannot starve the others of a session.
        SignedIn = await NewSignedInClientAsync();
    }

    /// <summary>A company, and a user holding every permission through one global role.</summary>
    private async Task SeedAsync(SmartnetDbContext db)
    {
        var company = new Company { Name = "HTTP Test Co", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        CompanyId = company.Id;

        var user = new User
        {
            Username = Username,
            Name = "HTTP Tests",
            PasswordHash = new Argon2PasswordHasher().Hash(Password),
            MustChangePassword = false,
            Ustat = "Active",
            Addedby = string.Empty,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        UserId = user.Id;

        var role = new Role { Name = "HTTP Tests", CompanyId = null, IsSystem = false };
        foreach (var permission in Permissions.All)
        {
            role.Permissions.Add(new RolePermission { Permission = permission });
        }

        db.Roles.Add(role);
        await db.SaveChangesAsync();

        // A null company on the assignment means every company (CompanyAccessService).
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CompanyId = null });
        await db.SaveChangesAsync();
    }

    /// <summary>The origin the CORS policy is configured with, and the one the tests assert against.</summary>
    public const string CorsOrigin = "http://localhost:3000";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(Environments.Development);

    /// <summary>An unauthenticated client. Cookies are kept so a login on it sticks.</summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// A client already signed in as the seeded user — created once, in <see cref="InitializeAsync"/>.
    /// </summary>
    /// <remarks>
    /// <b>Shared on purpose.</b> The first version of this fixture logged in per test, and the suite
    /// started failing with 429s about ten tests in: the login endpoint is rate-limited, and a test
    /// suite hitting it in a tight loop looks exactly like the credential-stuffing it exists to stop.
    /// That is the limiter working, so the fix is to stop provoking it — sign in once, before any test
    /// runs, and let the one test that actually cares about the limiter provoke it deliberately.
    /// <para>Safe to share: xUnit runs the tests in a collection one at a time.</para>
    /// </remarks>
    public HttpClient SignedIn { get; private set; } = null!;

    /// <summary>Signs a fresh client in. Only for tests that need their own session.</summary>
    public async Task<HttpClient> NewSignedInClientAsync()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });

        if (!response.IsSuccessStatusCode)
        {
            // Failing here rather than in the test keeps a broken fixture from reading as a broken
            // endpoint — the difference between "login is broken" and "this test's subject is broken".
            throw new InvalidOperationException(
                $"Fixture could not sign in: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return client;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

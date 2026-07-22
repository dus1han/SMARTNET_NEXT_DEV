using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Api.Backups;
using Smartnet.Api.Dunning;
using Smartnet.Api.Middleware;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Backups;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure;
using Smartnet.Infrastructure.Backups;
using Smartnet.Infrastructure.Storage;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Identity;
using Smartnet.Domain.Exporting;
using Smartnet.Infrastructure.Exporting;
using Smartnet.Infrastructure.MasterData;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Infrastructure.Numbering;
using Smartnet.Infrastructure.Settings;
using Smartnet.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging: structured, from day one. The legacy app logged to Console.WriteLine
// (i.e. nowhere, under IIS) and returned stack traces to the browser instead.
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    // InvariantCulture: log output must not vary with the server's locale. A number
    // rendered "1.234,56" on one host and "1,234.56" on another makes logs
    // unparseable. The same reasoning applies to money, everywhere.
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// ---------------------------------------------------------------------------
// Database: the DEV copy, running on the host (not in Docker).
// The connection string comes from configuration/env. Never from source.
// ---------------------------------------------------------------------------
var conn = builder.Configuration.GetConnectionString("Smartnet")
           ?? throw new InvalidOperationException(
               "ConnectionStrings__Smartnet is not set. Copy .env.example to .env and fill it in.");

// Guard rail: a DEVELOPMENT process must never open the production database.
//
// This used to refuse unconditionally, which was right while production still belonged to the legacy
// app and nothing here had any business touching it. At cutover that inverts: the production
// deployment's whole job is to serve `smartnet_invsys`, and an unconditional guard would stop the
// real system from starting.
//
// What the guard was actually protecting against was never "the production database" — it was "a
// developer's machine, pointed at live by a copy-pasted connection string". That is exactly what
// Development means here, so that is what it now keys on. A Production deployment may open the
// production database; nothing else may.
if (builder.Environment.IsDevelopment()
    && Regex.IsMatch(conn, @"Database\s*=\s*smartnet_invsys\s*(;|$)", RegexOptions.IgnoreCase))
{
    throw new InvalidOperationException(
        "Refusing to start: a Development process is pointed at the PRODUCTION database " +
        "(smartnet_invsys). Development runs against smartnet_invsys_dev. If this really is the " +
        "production deployment, it must run with ASPNETCORE_ENVIRONMENT=Production.");
}

builder.Services.AddSmartnetPersistence(conn);

// Where uploaded documents are written (Phase 7, slice 4). In Docker this must point at a mounted
// volume: the container filesystem is replaced on every deploy, so an unmounted path would silently
// discard every document the business uploaded since the last release.
// A relative path is resolved against the CONTENT ROOT, not the process working directory. Those are
// the same under "dotnet run" from the project folder and different under almost anything else — an
// IDE, a service, a run from another directory — and when they differ the store silently points at an
// empty folder. Uploads appear to work and every download 410s, because the row is in the database
// and the file is somewhere else entirely. That is exactly the fault this comment is replacing.
builder.Services.Configure<DocumentStorageOptions>(options =>
{
    builder.Configuration.GetSection(DocumentStorageOptions.Section).Bind(options);

    if (!Path.IsPathRooted(options.RootPath))
    {
        options.RootPath = Path.Combine(builder.Environment.ContentRootPath, options.RootPath);
    }
});

// ---------------------------------------------------------------------------
// Audit: the "who, why, from where" of every request, read off the HTTP context
// and consumed by the SaveChanges interceptor at the persistence layer. No
// endpoint passes this down by hand, so no endpoint can forget to. See AUDIT.md.
// ---------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IChangeContext, HttpChangeContext>();

// Injected rather than called statically, so tests can control the clock. (DateTime.Now is
// banned outright — see BannedSymbols.txt; the legacy app wrote server-local time to the DB.)
builder.Services.AddSingleton(TimeProvider.System);

// ---------------------------------------------------------------------------
// Errors: a generic message and a correlation id to the client, the full detail
// to the log. Closes A9 — every legacy controller returned ex.ToString() to the
// browser, stack traces, SQL and schema included.
// ---------------------------------------------------------------------------
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------------------
// Auth. The token rides in an httpOnly, SameSite=Strict cookie — not in
// localStorage, where any XSS bug would hand it to an attacker, and not in an
// Authorization header the frontend has to remember to attach.
// Closes A4 (plaintext passwords), A5 (cosmetic authorization), A7 (no CSRF).
// ---------------------------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    // Fail at startup, not at the first login: a missing signing key is a deployment fault, and
    // the moment to find out is before the app is serving traffic.
    .ValidateOnStart();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ICompanyAccessService, CompanyAccessService>();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();

// Emailing a document to a customer's contacts — the part job sheets, statements and quotations share.
builder.Services.AddScoped<Smartnet.Api.Mailing.DocumentMailer>();
builder.Services.AddSingleton<IMailSender, MailSender>();
builder.Services.AddSingleton<IExcelExporter, ExcelExporter>();

// Bulk dunning runs off the request thread: an in-process queue (singleton) and a hosted worker that
// drains it. The endpoint enqueues and returns; nothing blocks. Sending itself is gated by the
// per-company mail kill switch (off by default) — see DunningController.
builder.Services.AddSingleton<IDunningChannel, DunningChannel>();
builder.Services.AddHostedService<DunningBackgroundService>();

// ---------------------------------------------------------------------------
// Database backups (Phase 9): hourly to FTPS, newest fifteen kept, restore on demand.
//
// The FTP destination is configured from the screen and lives in `backup_settings` — an administrator
// changing where backups go should not need a deploy. What stays here in configuration is the one thing
// that must not be editable through a web form: Backup:RestoreConnectionString, a credential that can
// DROP and CREATE the schema. The application's own database user deliberately holds no DDL at all
// (infra/sql/narrow-app-user-grants.sh), which is also what makes audit_log genuinely append-only, so a
// restore cannot run as it. Unset means restore reports itself unavailable and everything else works.
// ---------------------------------------------------------------------------
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.Section));

// The listing is cached for a minute. Without it, every page load opened a fresh FTP session, and the
// burst got this server's address banned by the remote host — see BackupService.ListingFreshFor.
builder.Services.AddMemoryCache();

// The dump reads what the application reads, so it is taken with the application's own credentials —
// SELECT, LOCK TABLES and SHOW VIEW are enough for mysqldump, and nothing more is granted.
builder.Services.AddSingleton<IDatabaseConnectionString>(new DatabaseConnectionString(conn));
builder.Services.AddScoped<IBackupDestinationProvider, BackupDestinationProvider>();
builder.Services.AddScoped<IBackupStorage, FtpBackupStorage>();
builder.Services.AddScoped<IDatabaseBackup, MySqlDatabaseBackup>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHostedService<BackupBackgroundService>();
builder.Services.AddScoped<INumberSeriesInitialiser, NumberSeriesInitialiser>();
builder.Services.AddScoped<IDocumentNumberAllocator, DocumentNumberAllocator>();
builder.Services.AddScoped<ICustomerCodeAllocator, CustomerCodeAllocator>();
builder.Services.AddScoped<ISupplierCodeAllocator, SupplierCodeAllocator>();
builder.Services.AddScoped<IItemCodeAllocator, ItemCodeAllocator>();

// The one tax engine every document runs its lines through (Phase 5). Stateless, pure decimal — a
// singleton with nothing to scope.
builder.Services.AddSingleton<ITaxEngine, TaxEngine>();

// The receivables ledger read side — a customer's balance, derived (never stored). Scoped, because it
// reads through the request's DbContext.
builder.Services.AddScoped<IReceivablesLedger, ReceivablesLedger>();

// The payables ledger read side — a supplier's balance and a supplier invoice's outstanding, derived
// (never stored). Phase 6, slice 2.
builder.Services.AddScoped<IPayablesLedger, PayablesLedger>();

// The general ledger: posts each money event as a balanced double-entry, idempotently.
builder.Services.AddScoped<IGeneralLedger, GeneralLedger>();

// Resolves a business rule (rounding mode, credit-limit enforcement) for a company — override, global,
// then default.
builder.Services.AddScoped<IBusinessRuleReader, BusinessRuleReader>();

// Encrypts the secrets held in the database at rest — the SMTP password (A2) and the FTP password the
// Backups screen stores.
//
// THE KEY RING MUST OUTLIVE THE CONTAINER. Left at its default it lands in the container's home
// directory, which a redeploy throws away, and every ciphertext already in the database becomes
// undecryptable — silently, because the ciphertext is still there and still looks fine. This is not
// hypothetical: it cost an afternoon. Backups uploaded happily at 06:53, two redeploys followed, and
// every attempt after that failed with an error about not reaching the FTP server — the stored password
// was decrypting to nothing and the login was being refused. The network was never the problem.
//
// So the path is required rather than defaulted. A missing key ring is a fault that shows up hours later
// as somebody else's fault; refusing to start says it once, at the moment it is cheap to fix.
var keyRing = builder.Configuration["DataProtection:KeyPath"];

if (string.IsNullOrWhiteSpace(keyRing))
{
    throw new InvalidOperationException(
        "DataProtection__KeyPath is not set. It must point at a directory that survives a redeploy "
        + "(infra/docker-compose.yml mounts /var/www/sys-dpkeys for this); without it every stored "
        + "password becomes unreadable the next time this container is replaced.");
}

builder.Services
    .AddDataProtection(options => options.ApplicationDiscriminator = "smartnet")
    .PersistKeysToFileSystem(Directory.CreateDirectory(keyRing));

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException(
              "Jwt configuration is missing. Set JWT_SIGNING_KEY (see .env.example).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // No grace period on expiry. The default is five minutes, which quietly extends every
            // token's life — and this one carries the user's permissions.
            ClockSkew = TimeSpan.Zero,
        };

        // The token is in a cookie, so the default "read the Authorization header" needs
        // redirecting. The cookie is httpOnly precisely so that the frontend cannot do this.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies[AuthCookie.Name];
                return Task.CompletedTask;
            },

            // Why a 401 happened, which the access log alone cannot say.
            //
            // Chasing "it logs me out after a few minutes" meant pairing login times against 401 times by
            // hand, because every rejection looked identical from outside: an expired token, a cookie the
            // browser declined to send, and a signature that no longer verifies all read as a bare 401.
            // Those have three different causes and three different fixes.
            //
            // Only failures with a token attached are logged. Unauthenticated scanners probing for
            // /api/vendor/... produce a steady trickle of 401s that carry nothing and mean nothing.
            OnAuthenticationFailed = context =>
            {
                var log = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Smartnet.Auth");

                AuthLog.TokenRejected(log, context.HttpContext.Request.Path.Value, context.Exception);

                return Task.CompletedTask;
            },

            // The other half: a 401 with no token to reject. OnAuthenticationFailed never fires for
            // these, so without this the two causes stay indistinguishable in the log.
            OnChallenge = context =>
            {
                var request = context.HttpContext.Request;

                if (!request.Cookies.ContainsKey(AuthCookie.Name) && request.Cookies.Count > 0)
                {
                    var log = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Smartnet.Auth");

                    AuthLog.NoAuthCookie(log, request.Path.Value, request.Cookies.Count);
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    // DENY BY DEFAULT. Every endpoint requires an authenticated user unless it explicitly says
    // [AllowAnonymous]. In the legacy app, SessionExpireAttribute checked only that *someone* was
    // logged in, and the data endpoints checked nothing at all — so any authenticated user could
    // call ManageUserController.updatepermission and make themselves an administrator (A5).
    //
    // Slice 3 layers the 36 permission flags on top of this as per-endpoint policies.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // One policy per permission, generated from the catalogue. Dev_Admin satisfies all of them.
    options.AddPermissionPolicies();
});

builder.Services.AddSmartnetRateLimiting();

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Without this, every C# reference type is described as optional in the schema — so the
    // generated TypeScript sees `permissions?: string[] | null` for a property that is never null,
    // and every screen has to null-check a field that cannot be null.
    //
    // Worse than the noise: it makes the genuinely nullable fields indistinguishable from the rest,
    // which is exactly the information the generated client exists to carry.
    options.SupportNonNullableReferenceTypes();
    options.UseAllOfToExtendReferenceSchemas();

    // ...and then actually mark them required, which SupportNonNullableReferenceTypes does not do.
    options.SchemaFilter<RequireNonNullableSchemaFilter>();
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    // The browser cannot read a response header unless it is exposed. Without this the client
    // never sees the correlation id, and "quote me the reference on the error screen" fails.
    .WithExposedHeaders(HttpChangeContext.CorrelationIdHeader)
    .AllowCredentials()));

var app = builder.Build();

// First in the pipeline: nothing below it should run unguarded, and everything below it should
// be able to log a correlation id.
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// After UseAuthorization, so the claims exist to inspect; before the endpoints, so that a user
// with an expired password cannot reach any of them.
app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapControllers();

// Health: proves the API is up AND that it can actually reach the database.
// Anonymous by necessity — authorization now denies by default, and the container's healthcheck
// has no credentials. It reveals only up/down.
app.MapGet("/health", async (SmartnetLegacyDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "healthy", database = "connected" })
        : Results.Problem("database unreachable"))
    .AllowAnonymous();

// The Phase 0 /_smoke endpoint is gone: it dumped row counts for every table to anyone who
// asked, and Phase 1 has real endpoints to prove the mapping instead.

app.Run();


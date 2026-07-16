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
using Smartnet.Api.Dunning;
using Smartnet.Api.Middleware;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure;
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

// Guard rail: refuse to start against production, whatever the config says.
if (Regex.IsMatch(conn, @"Database\s*=\s*smartnet_invsys\s*(;|$)", RegexOptions.IgnoreCase))
{
    throw new InvalidOperationException(
        "Refusing to start: the connection string points at the PRODUCTION database " +
        "(smartnet_invsys). Development runs against smartnet_invsys_dev.");
}

builder.Services.AddSmartnetPersistence(conn);

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
builder.Services.AddSingleton<IMailSender, MailSender>();
builder.Services.AddSingleton<IExcelExporter, ExcelExporter>();

// Bulk dunning runs off the request thread: an in-process queue (singleton) and a hosted worker that
// drains it. The endpoint enqueues and returns; nothing blocks. Sending itself is gated by the
// per-company mail kill switch (off by default) — see DunningController.
builder.Services.AddSingleton<IDunningChannel, DunningChannel>();
builder.Services.AddHostedService<DunningBackgroundService>();
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

// Resolves a business rule (rounding mode, credit-limit enforcement) for a company — override, global,
// then default.
builder.Services.AddScoped<IBusinessRuleReader, BusinessRuleReader>();

// Encrypts the SMTP password at rest (A2). The keys live outside the image; in production they
// must be persisted to a shared, backed-up location, or a redeploy silently invalidates every
// ciphertext already in the database.
builder.Services.AddDataProtection(options => options.ApplicationDiscriminator = "smartnet");

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

    // E2E-only affordance: record a payment against a new invoice, so the Playwright flow can exercise
    // the ledger-derived balance without the Phase 7 payments UI (PHASE-5-PLAN slice 6). Deliberately
    // Development-only — this endpoint does not exist in Staging or Production — and anonymous, so the
    // test harness can seed a payment without a session. It never touches a legacy invoice.
    app.MapPost("/api/dev/seed-payment", async (
        DevSeedPaymentRequest body,
        Smartnet.Infrastructure.Persistence.SmartnetDbContext db,
        CancellationToken cancellationToken) =>
    {
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.Number == body.InvoiceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (invoice is null)
        {
            return Results.NotFound();
        }

        db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = invoice.CustomerId,
            Type = LedgerEntryType.Payment,
            Amount = -body.Amount, // a payment reduces the receivable
            InvoiceId = invoice.Id,
            OccurredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok();
    }).AllowAnonymous();
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

/// <summary>The body of the Development-only E2E payment-seed endpoint (PHASE-5-PLAN slice 6).</summary>
internal sealed record DevSeedPaymentRequest(string InvoiceNumber, decimal Amount);

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Serilog;
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

builder.Services.AddDbContext<SmartnetLegacyDbContext>(o =>
    o.UseMySql(conn, ServerVersion.AutoDetect(conn)));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();

// Health: proves the API is up AND that it can actually reach the database.
app.MapGet("/health", async (SmartnetLegacyDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "healthy", database = "connected" })
        : Results.Problem("database unreachable"));

// Phase-0 smoke endpoint: proves the scaffolded entities really do map to the live
// schema. Removed once Phase 1 lands real endpoints.
app.MapGet("/_smoke", async (SmartnetLegacyDbContext db) => Results.Ok(new
{
    companies = await db.CompaniesMs.CountAsync(),
    customers = await db.CusMs.CountAsync(),
    items = await db.ItemMs.CountAsync(),
    invoices = await db.InvoiceHs.CountAsync(),
    invoiceLines = await db.InvoiceLs.CountAsync(),
    payments = await db.Payments.CountAsync(),
}));

app.Run();

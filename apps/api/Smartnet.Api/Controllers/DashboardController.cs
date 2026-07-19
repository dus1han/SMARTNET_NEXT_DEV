using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// The dashboard — one screen, two shapes, replacing the legacy AdminDashboard and UserDashboard.
/// </summary>
/// <remarks>
/// The shape is chosen by permission, not by a separate controller: a caller who holds
/// <c>dashboard</c> gets the company view (every invoice in the company they are signed into); everyone
/// else gets the "my" view, scoped to what they prepared. So the endpoint is reachable by any
/// authenticated user — it is a dashboard, not a report — and the role-awareness comes entirely from
/// the claims already in the token. No new authorization surface (the plan's slice-0 decision).
///
/// <para>The customer dashboard is gone: the business confirmed it unused, corroborated by there being
/// no customer-portal logins (Finding 7).</para>
/// </remarks>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly SmartnetDbContext _db;
    private readonly ICompanyContext _company;
    private readonly TimeProvider _time;

    public DashboardController(
        SmartnetLegacyDbContext legacy,
        SmartnetDbContext db,
        ICompanyContext company,
        TimeProvider time)
    {
        _legacy = legacy;
        _db = db;
        _company = company;
        _time = time;
    }

    /// <param name="company">Which company to scope to — a specific id, or "all"/absent for every
    /// company the caller may see, aggregated.</param>
    [HttpGet]
    [Authorize] // any authenticated user gets a dashboard; which shape is decided by their claims
    public async Task<ActionResult<DashboardResponse>> Get(
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var companyView =
            User.HasClaim(SmartnetClaims.Permission, Permissions.Dashboard)
            || User.HasClaim(SmartnetClaims.Permission, Permissions.SystemDevAdmin);

        // Company view: no name filter. "My" view: this user's legacy `name` (== invoice_h.preparedby)
        // — an empty string when we cannot resolve it, which scopes to nothing rather than to everyone.
        var preparedBy = companyView
            ? null
            : (await CurrentUserName(cancellationToken).ConfigureAwait(false) ?? string.Empty);

        // The company filter can only ever narrow to a company the token already permits: a specific id
        // is honoured only if it is in the accessible set; anything else falls back to "all of them".
        //
        // A List, not an array: on .NET 10 `array.Contains(x)` binds to the span-based
        // MemoryExtensions.Contains, which EF's funcletizer cannot evaluate (it throws on the
        // ReadOnlySpan<long> return constraint). List<T>.Contains is an instance method EF translates
        // to a SQL IN. Same reason scopeIds below is a List.
        var accessible = _company.Accessible.ToList();

        long? selectedId = long.TryParse(company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && accessible.Contains(id)
            ? id
            : null;

        var scopeIds = (selectedId is { } only ? [only] : accessible)
            .Select(i => i.ToString(CultureInfo.InvariantCulture))
            .ToList();

        var invoices = scopeIds.Count > 0
            ? await _legacy.InvoiceHs
                .Where(h => scopeIds.Contains(h.Company!))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];

        var companies = await _db.Companies
            .Where(c => accessible.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new DashboardCompanyOption(c.Id, c.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(Dashboard.Build(invoices, monthStart, monthEnd, preparedBy, selectedId, companies));
    }

    /// <summary>
    /// The analytical half of the dashboard — trend, ageing, cash movement, concentration and margin.
    /// </summary>
    /// <remarks>
    /// <b>A separate request from the at-a-glance tiles, deliberately.</b> This scans a year of invoices
    /// and their lines; the month tiles do not. Loading them together would hold the whole screen behind
    /// the slowest query, so the glance paints first and the analysis fills in behind it.
    ///
    /// <para>Company-scoped like everything else, and never wider than the token allows. There is no
    /// "my" variant: these are business readings, so a caller without the dashboard permission gets the
    /// same shape scoped to the companies they can already see — nothing here is per-user.</para>
    /// </remarks>
    [HttpGet("analytics")]
    [Authorize]
    public async Task<ActionResult<DashboardAnalytics>> Analytics(
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var accessible = _company.Accessible.ToList();

        long? selectedId = long.TryParse(company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && accessible.Contains(id)
            ? id
            : null;

        var scopeIds = (selectedId is { } only ? [only] : accessible)
            .Select(i => i.ToString(CultureInfo.InvariantCulture))
            .ToList();

        if (scopeIds.Count == 0)
        {
            return Ok(DashboardAnalyticsBuilder.Build([], [], [], [], [], [], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, decimal>(), today));
        }

        var invoices = await _legacy.InvoiceHs
            .Where(h => scopeIds.Contains(h.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoiceNos = invoices
            .Where(h => !string.IsNullOrEmpty(h.Invoiceno))
            .Select(h => h.Invoiceno!)
            .ToHashSet(StringComparer.Ordinal);

        // Lines and payments carry no company, so they are scoped by membership of the scoped invoices —
        // the same approach the reports take, and the reason a payment against a deleted invoice drops
        // out rather than being counted against a company it cannot be attributed to.
        var lines = (await _legacy.InvoiceLs.ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(l => l.Inno != null && invoiceNos.Contains(l.Inno))
            .ToList();

        var payments = (await _legacy.Payments.ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => p.Invoiceno != null && invoiceNos.Contains(p.Invoiceno))
            .ToList();

        var expenses = await _legacy.ExpenseTrs
            .Where(e => scopeIds.Contains(e.Company) && e.DeletedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A legacy supplier settlement carries no amount of its own — the row records which invoice was
        // paid and when, and the amount is the invoice's. So the cash that left is the invoice's value,
        // dated by the settlement.
        var supplierInvoices = await _legacy.SupplierInvoices
            .Where(i => scopeIds.Contains(i.Company!))
            .Select(i => new { i.Id, i.Amount, i.Supcode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var supplierSpend = supplierInvoices
            .Select(i => (SupplierCode: i.Supcode ?? string.Empty, Amount: LegacyValue.Money(i.Amount)))
            .ToList();

        var supplierNames = (await _legacy.SupMs
                .Select(x => new { x.Supcode, x.Supname })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(x => !string.IsNullOrEmpty(x.Supcode))
            .GroupBy(x => x.Supcode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Supname ?? string.Empty, StringComparer.Ordinal);

        var amountByInvoice = supplierInvoices
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => LegacyValue.Money(g.First().Amount));

        var settlements = await _legacy.SupplierInvPays
            .Select(s => new { s.Supinvid, s.Paiddate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var supplierPayments = settlements
            .Select(s => (
                Id: long.TryParse(s.Supinvid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid) ? sid : (long?)null,
                Date: LegacyValue.Date(s.Paiddate)))
            .Where(s => s.Id is not null && amountByInvoice.ContainsKey(s.Id!.Value))
            .Select(s => (s.Date, Amount: amountByInvoice[s.Id!.Value]))
            .ToList();

        // Credit limits live on the customer master and nothing enforces them; the dashboard is the
        // first thing in either system to read the column.
        var creditLimits = (await _legacy.CusMs
                .Select(c => new { c.Cuscode, c.Climit })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(c => !string.IsNullOrEmpty(c.Cuscode) && c.Climit is > 0)
            .GroupBy(c => c.Cuscode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Climit!.Value, StringComparer.Ordinal);

        var customerNames = (await _legacy.CusMs
                .Select(c => new { c.Cuscode, c.Cusname })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(c => !string.IsNullOrEmpty(c.Cuscode))
            .GroupBy(c => c.Cuscode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Cusname ?? string.Empty, StringComparer.Ordinal);

        return Ok(DashboardAnalyticsBuilder.Build(
            invoices, lines, payments, expenses, supplierPayments, supplierSpend,
            customerNames, supplierNames, creditLimits, today));
    }

    /// <summary>This user's display name — the legacy <c>preparedby</c> value — from their id claim.</summary>
    private async Task<string?> CurrentUserName(CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return null;
        }

        return await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

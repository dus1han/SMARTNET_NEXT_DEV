using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Customer receipts — the money-in module (Phase 7, slice 1).
/// </summary>
/// <remarks>
/// A receipt is money received from a customer, <b>allocated across one or more open invoices</b> — richer
/// than the legacy one-payment-one-invoice screen. Each allocation is a receivables-ledger <c>Payment</c>
/// entry (the truth, from which the outstanding is derived), dual-writing the legacy <c>payments</c> row and
/// <c>invoice_h.balance</c> for the surviving legacy outstanding report. Idempotent, and voidable through a
/// compensating entry — never by rewriting a balance.
/// </remarks>
[ApiController]
[Route("api/customer-receipts")]
public sealed class CustomerReceiptsController : ControllerBase
{
    private readonly ICustomerReceiptCreator _creator;
    private readonly ICustomerReceiptVoider _voider;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public CustomerReceiptsController(
        ICustomerReceiptCreator creator,
        ICustomerReceiptVoider voider,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _creator = creator;
        _voider = voider;
        _company = company;
        _db = db;
        _legacy = legacy;
    }

    /// <summary>A customer's open invoices — the picker a receipt is allocated over. New and legacy alike, from the ledger.</summary>
    [HttpGet("outstanding")]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<IReadOnlyList<OutstandingInvoiceLine>>> Outstanding(
        [FromQuery] long customerId, CancellationToken cancellationToken)
    {
        var accessibleText = _company.Accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        // Derived outstanding per invoice, in one grouped query over the ledger — unifies new and legacy invoices.
        var ledgerRows = await _db.ReceivablesLedger
            .Where(e => e.CustomerId == customerId && e.InvoiceId != null)
            .GroupBy(e => e.InvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Outstanding = g.Sum(e => e.Amount) })
            .Where(x => x.Outstanding > 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ledgerRows.Count == 0)
        {
            return Ok(Array.Empty<OutstandingInvoiceLine>());
        }

        var outstanding = ledgerRows.ToDictionary(r => r.InvoiceId, r => r.Outstanding);
        var ids = outstanding.Keys.ToList();

        var invoices = await _legacy.InvoiceHs
            .Where(h => ids.Contains(h.Id))
            .Select(h => new { h.Id, h.Invoiceno, h.Indate, h.Totamount, h.Company, h.DataOrigin })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = invoices
            .Where(h => h.Company != null && accessibleText.Contains(h.Company))
            .Select(h => new OutstandingInvoiceLine(
                h.Id,
                h.Invoiceno ?? string.Empty,
                LegacyValue.Date(h.Indate) ?? DateOnly.MinValue,
                LegacyValue.Money(h.Totamount),
                outstanding[h.Id],
                h.DataOrigin == "new" ? "new" : "legacy"))
            .OrderBy(l => l.Date)
            .ThenBy(l => l.Number)
            .ToList();

        return Ok(lines);
    }

    /// <summary>Every receipt the caller may see, newest first.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<PagedResult<CustomerReceiptSummary>>> List(
        [FromQuery] PageRequest paging,
        CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var accessibleText = PageRequest.AsText(accessible).ToList();

        // Unlike invoices and quotations, the two halves of this list are DIFFERENT TABLES —
        // customer_receipts for what this app records, payments for the pre-cutover history. A union
        // of two tables cannot be paged by skipping rows in one of them: page 2 would drop or repeat
        // rows depending on how the two interleave.
        //
        // So the ORDER is decided over both, and only then is a page taken. What crosses the wire per
        // request is (id, date) — two numbers a row — and only the 25 rows on the page are then read
        // in full. The browser receives 25 rows instead of 2,226, which is the point; if payments ever
        // reaches the millions, this key merge is the part to revisit.
        //
        // The signed id is not a trick invented here: a legacy payment is already listed negative
        // (it is display-only and must not collide with a receipt id), so the sign says which table a
        // row came from.
        long? searchCustomerId = null;
        List<string> searchCodes = [];
        List<long> searchIds = [];

        if (paging.LikePattern is { } pattern)
        {
            var matched = await _db.Customers
                .Where(c => EF.Functions.Like(c.Name, pattern))
                .Select(c => new { c.Id, c.Code })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            searchCodes = matched.Where(m => m.Code != null).Select(m => m.Code!).ToList();
            searchIds = matched.Select(m => m.Id).ToList();
        }

        var term = paging.LikePattern;

        // --- legacy keys: payments, scoped to a company through the invoice they settled -----------
        // payments carries no company of its own. The join also drops payments whose invoice is gone,
        // which is the same three orphaned rows the old code skipped and Data Exceptions reports.
        var legacyKeys = await (
            from p in _legacy.Payments
            join h in _legacy.InvoiceHs on p.Invoiceno equals h.Invoiceno
            where p.Invoiceno != null
                  && p.DataOrigin != "new"
                  && h.Company != null
                  && accessibleText.Contains(h.Company)
                  && (term == null
                      || EF.Functions.Like(p.Payref!, term)
                      || (h.Customer != null && searchCodes.Contains(h.Customer)))
            select new { p.Id, Date = p.Paymentrecdate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // --- new keys: receipts this app recorded --------------------------------------------------
        var newKeys = await _db.CustomerReceipts
            .Where(r => r.CompanyId != null && accessible.Contains(r.CompanyId.Value)
                        && (term == null
                            || EF.Functions.Like(r.Reference!, term)
                            || searchIds.Contains(r.CustomerId)))
            .Select(r => new { r.Id, r.Date })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var keys = legacyKeys
            .Select(k => (Id: (long)-k.Id, Date: LegacyValue.Date(k.Date) ?? DateOnly.MinValue))
            .Concat(newKeys.Select(k => (Id: (long)k.Id, Date: k.Date)))
            // Id is the tiebreaker so the order is total: without it, receipts sharing a date can
            // swap places between requests and paging shows some twice while missing others.
            .OrderByDescending(k => k.Date)
            .ThenByDescending(k => k.Id)
            .ToList();

        var pageIds = keys.Skip(paging.Skip).Take(paging.SafePageSize).Select(k => k.Id).ToList();
        var rows = await HydrateAsync(pageIds, cancellationToken).ConfigureAwait(false);

        _ = searchCustomerId;
        return Ok(new PagedResult<CustomerReceiptSummary>(rows, keys.Count, paging.SafePage, paging.SafePageSize));
    }

    /// <summary>
    /// Reads one page of ids back into rows, preserving the order the union decided.
    /// </summary>
    /// <remarks>
    /// Two queries, each scoped to the handful of ids on the page rather than the table: negative ids
    /// are legacy payments, positive ones are receipts this app recorded. The order is reapplied at the
    /// end because two separate reads cannot preserve an interleaved order on their own.
    /// </remarks>
    private async Task<List<CustomerReceiptSummary>> HydrateAsync(
        List<long> pageIds,
        CancellationToken cancellationToken)
    {
        if (pageIds.Count == 0)
        {
            return [];
        }

        var byId = new Dictionary<long, CustomerReceiptSummary>();

        var newIds = pageIds.Where(id => id > 0).ToList();
        if (newIds.Count > 0)
        {
            var receipts = await _db.CustomerReceipts
                .Where(r => newIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Date, r.CustomerId, r.Amount, r.Method, r.Reference, Invoices = r.Allocations.Count })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var customerIds = receipts.Select(r => r.CustomerId).Distinct().ToList();
            var names = await _db.Customers
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
                .ConfigureAwait(false);

            foreach (var r in receipts)
            {
                byId[r.Id] = new CustomerReceiptSummary(
                    r.Id, r.Date, names.GetValueOrDefault(r.CustomerId), r.Amount, r.Method, r.Reference, r.Invoices, "new");
            }
        }

        var legacyIds = pageIds.Where(id => id < 0).Select(id => -id).ToList();
        if (legacyIds.Count > 0)
        {
            var pays = await _legacy.Payments
                .Where(p => legacyIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Invoiceno, p.Amount, p.Paymentrecdate, p.Paym, p.Payref })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var invoiceNos = pays.Select(p => p.Invoiceno!).Where(n => n != null).Distinct().ToList();
            var invoices = (await _legacy.InvoiceHs
                    .Where(h => h.Invoiceno != null && invoiceNos.Contains(h.Invoiceno))
                    .Select(h => new { h.Invoiceno, h.Customer })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .GroupBy(h => h.Invoiceno!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var codes = invoices.Values.Select(i => i.Customer).Where(c => c != null).Select(c => c!).Distinct().ToList();
            var namesByCode = (await _db.Customers
                    .Where(c => c.Code != null && codes.Contains(c.Code))
                    .Select(c => new { c.Code, c.Name })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(c => c.Code!, c => c.Name, StringComparer.Ordinal);

            foreach (var p in pays)
            {
                var customer = p.Invoiceno is not null && invoices.TryGetValue(p.Invoiceno, out var inv) ? inv.Customer : null;

                byId[-p.Id] = new CustomerReceiptSummary(
                    -p.Id,
                    LegacyValue.Date(p.Paymentrecdate) ?? DateOnly.MinValue,
                    customer is not null ? namesByCode.GetValueOrDefault(customer) : null,
                    LegacyValue.Money(p.Amount),
                    p.Paym,
                    p.Payref,
                    1,
                    "legacy");
            }
        }

        // The union already decided the order; this restores it after two independent reads.
        return pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>The pre-cutover legacy customer payments (payments, data_origin NULL), joined for customer + amount.</summary>
    private async Task<List<CustomerReceiptSummary>> LegacyCustomerPayments(CancellationToken cancellationToken)
    {
        var accessibleText = _company.Accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        // != "new" is null-aware in EF (matches NULL and 'legacy'), so it is exactly the pre-cutover rows.
        var legacyPays = await _legacy.Payments
            .Where(p => p.DataOrigin != "new" && p.Invoiceno != null)
            .Select(p => new { p.Id, p.Invoiceno, p.Amount, p.Paymentrecdate, p.Paym, p.Payref })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (legacyPays.Count == 0)
        {
            return [];
        }

        var invoiceNos = legacyPays.Select(p => p.Invoiceno!).Distinct().ToList();
        var invoices = (await _legacy.InvoiceHs
            .Where(h => h.Invoiceno != null && invoiceNos.Contains(h.Invoiceno))
            .Select(h => new { h.Invoiceno, h.Customer, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(h => h.Invoiceno!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var custCodes = invoices.Values.Select(i => i.Customer).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Customers
            .Where(c => c.Code != null && custCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(c => c.Code!, c => c.Name);

        var rows = new List<CustomerReceiptSummary>();
        foreach (var p in legacyPays)
        {
            if (!invoices.TryGetValue(p.Invoiceno!, out var inv)) continue;
            if (inv.Company is null || !accessibleText.Contains(inv.Company)) continue;

            rows.Add(new CustomerReceiptSummary(
                -p.Id, // negative id: legacy rows are display-only, and it avoids colliding with a new receipt's id
                LegacyValue.Date(p.Paymentrecdate) ?? DateOnly.MinValue,
                inv.Customer is not null ? namesByCode.GetValueOrDefault(inv.Customer) : null,
                LegacyValue.Money(p.Amount),
                p.Paym,
                p.Payref,
                1,
                "legacy"));
        }

        return rows;
    }

    /// <summary>One receipt in full — its per-invoice allocations.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<CustomerReceiptDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // A legacy payment is listed with a negative id (payments.id); it settled a single invoice.
        if (id < 0)
        {
            return await LegacyReceiptDetail(-id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var receipt = await _db.CustomerReceipts
            .Include(r => r.Allocations)
            .FirstOrDefaultAsync(
                r => r.Id == id && r.CompanyId != null && accessible.Contains(r.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return NotFound();
        }

        var customer = await _db.Customers
            .Where(c => c.Id == receipt.CustomerId)
            .Select(c => new { c.Name, c.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var companyName = receipt.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var invoiceIds = receipt.Allocations.Select(a => a.InvoiceId).Distinct().ToList();
        var invInfo = (await _legacy.InvoiceHs
            .Where(h => invoiceIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Invoiceno, h.Indate, h.Totamount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(h => h.Id);

        var allocations = receipt.Allocations
            .Select(a =>
            {
                invInfo.TryGetValue(a.InvoiceId, out var info);
                return new ReceiptAllocationLine(
                    a.InvoiceId, info?.Invoiceno, LegacyValue.Date(info?.Indate), LegacyValue.Money(info?.Totamount), a.Amount);
            })
            .ToList();

        return Ok(new CustomerReceiptDetail(
            receipt.Id, receipt.Date, companyName, customer?.Name, customer?.Code,
            receipt.Amount, receipt.Method, receipt.Reference, receipt.RowVersion, allocations, "new"));
    }

    /// <summary>A pre-cutover legacy payment (one <c>payments</c> row) as a receipt detail — the single invoice it settled.</summary>
    private async Task<ActionResult<CustomerReceiptDetail>> LegacyReceiptDetail(
        long paymentId, List<long> accessible, CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var pay = await _legacy.Payments
            .Where(p => p.Id == (int)paymentId)
            .Select(p => new { p.Invoiceno, p.Amount, p.Paymentrecdate, p.Paym, p.Payref })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pay is null || pay.Invoiceno is null)
        {
            return NotFound();
        }

        var inv = await _legacy.InvoiceHs
            .Where(h => h.Invoiceno == pay.Invoiceno)
            .Select(h => new { h.Id, h.Customer, h.Company, h.Indate, h.Totamount })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (inv is null || inv.Company is null || !accessibleText.Contains(inv.Company))
        {
            return NotFound();
        }

        var customerName = inv.Customer is null
            ? null
            : await _db.Customers.Where(c => c.Code == inv.Customer).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var companyName = long.TryParse(inv.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var amount = LegacyValue.Money(pay.Amount);
        var allocations = new List<ReceiptAllocationLine>
        {
            new(inv.Id, pay.Invoiceno, LegacyValue.Date(inv.Indate), LegacyValue.Money(inv.Totamount), amount),
        };

        return Ok(new CustomerReceiptDetail(
            -paymentId, LegacyValue.Date(pay.Paymentrecdate) ?? DateOnly.MinValue, companyName,
            customerName, inv.Customer, amount, pay.Paym, pay.Payref, 0, allocations, "legacy"));
    }

    /// <summary>Record a receipt — allocated across open invoices; posts Payment entries and dual-writes the legacy shadow.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<CustomerReceiptCreatedResponse>> Create(
        CreateCustomerReceiptRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record a receipt in that company.");
        }

        try
        {
            var created = await _creator.CreateAsync(
                new NewCustomerReceipt(
                    request.CompanyId, request.CustomerId, request.Date, request.Method, request.Reference,
                    request.IdempotencyKey,
                    request.Allocations.Select(a => new NewReceiptAllocation(a.InvoiceId, a.Amount)).ToList()),
                cancellationToken).ConfigureAwait(false);

            return Ok(new CustomerReceiptCreatedResponse(created.Id, created.Amount, created.AlreadyExisted));
        }
        catch (ReceiptAllocationExceedsOutstandingException over)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }
        catch (ReceiptInvoiceCustomerMismatchException mismatch)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: mismatch.Message);
        }
    }

    /// <summary>Void a receipt — soft, reason-gated; reverses each allocation through a compensating entry.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Payments)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        // A negative id is a payment the old system took: the lists show legacy `payments` rows under
        // -id, because they have no customer_receipts row of their own to be identified by.
        if (id < 0)
        {
            return await VoidLegacyAsync(-id, cancellationToken).ConfigureAwait(false);
        }

        var accessible = _company.Accessible.ToList();
        var companyId = await _db.CustomerReceipts
            .IgnoreQueryFilters()
            .Where(r => r.Id == id && r.DeletedAt == null)
            .Select(r => r.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyId is null || !accessible.Contains(companyId.Value))
        {
            return NotFound();
        }

        try
        {
            await _voider.VoidAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This receipt was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>
    /// Voids a payment the old system took, guarding it by the company that owns its invoice.
    /// </summary>
    /// <remarks>
    /// There is no row_version to check: a legacy payment is a row in the old table, not a document this
    /// app versions. What guards it instead is that it can only be voided once — a second attempt finds
    /// the reversal already there and the balance already restored.
    /// </remarks>
    private async Task<IActionResult> VoidLegacyAsync(long legacyPaymentId, CancellationToken cancellationToken)
    {
        var accessibleText = _company.Accessible
            .Select(c => c.ToString(CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);

        var company = await (
            from p in _legacy.Payments
            join h in _legacy.InvoiceHs on p.Invoiceno equals h.Invoiceno
            where p.Id == legacyPaymentId
            select h.Company).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (company is null || !accessibleText.Contains(company))
        {
            return NotFound();
        }

        try
        {
            await _voider.VoidLegacyAsync(legacyPaymentId, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // The payment names no invoice, names one that is gone, or is for nothing — all data
            // exceptions, and all worth saying plainly rather than as a 500.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }
}

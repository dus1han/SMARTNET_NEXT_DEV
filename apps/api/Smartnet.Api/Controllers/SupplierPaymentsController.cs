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
/// Supplier payments — the money-out module (Phase 7).
/// </summary>
/// <remarks>
/// A payment is money paid to a supplier, <b>allocated across one or more open supplier invoices</b> — the
/// payables mirror of customer receipts. Each allocation is a payables-ledger <c>Payment</c> entry (the truth,
/// from which the outstanding is derived), dual-writing the legacy <c>supplier_inv_pay</c> row and
/// <c>paymentstat</c>. Idempotent, voidable through a compensating entry. It settles new and adopted-legacy
/// supplier invoices alike.
/// </remarks>
[ApiController]
[Route("api/supplier-payments")]
public sealed class SupplierPaymentsController : ControllerBase
{
    private readonly ISupplierPaymentCreator _creator;
    private readonly ISupplierPaymentVoider _voider;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public SupplierPaymentsController(
        ISupplierPaymentCreator creator,
        ISupplierPaymentVoider voider,
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

    /// <summary>A supplier's open invoices — the picker a payment is allocated over. New and legacy alike, from the ledger.</summary>
    [HttpGet("outstanding")]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<IReadOnlyList<OutstandingSupplierInvoiceLine>>> Outstanding(
        [FromQuery] long supplierId, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        // Derived outstanding per invoice, in one grouped query over the payables ledger.
        var ledgerRows = await _db.PayablesLedger
            .Where(e => e.SupplierId == supplierId && e.SupplierInvoiceId != null)
            .GroupBy(e => e.SupplierInvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Outstanding = g.Sum(e => e.Amount) })
            .Where(x => x.Outstanding > 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ledgerRows.Count == 0)
        {
            return Ok(Array.Empty<OutstandingSupplierInvoiceLine>());
        }

        var outstanding = ledgerRows.ToDictionary(r => r.InvoiceId, r => r.Outstanding);
        var ids = outstanding.Keys.ToList();

        var lines = new List<OutstandingSupplierInvoiceLine>();

        // New invoices carry typed columns; read them from this app's entity.
        var newInvoices = await _db.SupplierInvoices
            .IgnoreQueryFilters()
            .Where(s => ids.Contains(s.Id) && s.DataOrigin == "new" && s.DeletedAt == null)
            .Select(s => new { s.Id, s.SupplierReference, s.Date, s.Amount, s.CompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var s in newInvoices.Where(s => s.CompanyId != null && accessible.Contains(s.CompanyId!.Value)))
        {
            lines.Add(new OutstandingSupplierInvoiceLine(
                s.Id, s.SupplierReference ?? $"#{s.Id}", s.Date, s.Amount, outstanding[s.Id], "new"));
        }

        // Legacy invoices keep their values in varchar columns — read them from the legacy model.
        var legacyInvoices = await _legacy.SupplierInvoices
            .Where(h => ids.Contains(h.Id) && h.DataOrigin != "new")
            .Select(h => new { h.Id, h.Invno, h.Invdate, h.Amount, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var h in legacyInvoices.Where(h => h.Company != null && accessibleText.Contains(h.Company!)))
        {
            lines.Add(new OutstandingSupplierInvoiceLine(
                h.Id, h.Invno ?? $"#{h.Id}", LegacyValue.Date(h.Invdate) ?? DateOnly.MinValue,
                LegacyValue.Money(h.Amount), outstanding[h.Id], "legacy"));
        }

        return Ok(lines.OrderBy(l => l.Date).ThenBy(l => l.Reference).ToList());
    }

    /// <summary>Every supplier payment the caller may see, newest first.</summary>
    [HttpGet]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<IReadOnlyList<SupplierPaymentSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var payments = await _db.SupplierPayments
            .Where(p => p.CompanyId != null && accessible.Contains(p.CompanyId.Value))
            .Select(p => new
            {
                p.Id,
                p.Date,
                p.SupplierId,
                p.Amount,
                p.Method,
                p.Reference,
                Invoices = p.Allocations.Count,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var supplierIds = payments.Select(p => p.SupplierId).Distinct().ToList();
        var names = await _db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = payments
            .Select(p => new SupplierPaymentSummary(
                p.Id, p.Date, names.GetValueOrDefault(p.SupplierId), p.Amount, p.Method, p.Reference, p.Invoices, "new"))
            .ToList();

        // --- Legacy supplier payments -------------------------------------------------------------
        // Pre-cutover payments live in supplier_inv_pay (data_origin NULL); the new path writes 'new', so
        // this shows the legacy history without double-counting. No stored amount there — the invoice's is
        // the best available (the legacy report shows the same), so a legacy row's amount is its invoice's.
        rows.AddRange(await LegacySupplierPayments(cancellationToken).ConfigureAwait(false));

        return Ok(rows
            .OrderByDescending(p => p.Date)
            .ThenByDescending(p => p.Id)
            .ToList());
    }

    /// <summary>One supplier payment in full — its per-invoice allocations.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<SupplierPaymentDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // A legacy payment is listed with a negative id (supplier_inv_pay.id); it settled a single invoice.
        if (id < 0)
        {
            return await LegacyPaymentDetail(-id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var payment = await _db.SupplierPayments
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(
                p => p.Id == id && p.CompanyId != null && accessible.Contains(p.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return NotFound();
        }

        var supplier = await _db.Suppliers
            .Where(s => s.Id == payment.SupplierId)
            .Select(s => new { s.Name, s.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var companyName = payment.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var invoiceIds = payment.Allocations.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        var references = (await _legacy.SupplierInvoices
            .Where(h => invoiceIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Invno })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(h => h.Id, h => h.Invno);
        var newRefs = (await _db.SupplierInvoices
            .IgnoreQueryFilters()
            .Where(s => invoiceIds.Contains(s.Id) && s.SupplierReference != null)
            .Select(s => new { s.Id, s.SupplierReference })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(s => s.Id, s => s.SupplierReference);

        var allocations = payment.Allocations
            .Select(a => new SupplierPaymentAllocationLine(
                a.SupplierInvoiceId,
                newRefs.GetValueOrDefault(a.SupplierInvoiceId) ?? references.GetValueOrDefault(a.SupplierInvoiceId),
                a.Amount))
            .ToList();

        return Ok(new SupplierPaymentDetail(
            payment.Id, payment.Date, companyName, supplier?.Name, supplier?.Code,
            payment.Amount, payment.Method, payment.Reference, payment.RowVersion, allocations, "new"));
    }

    /// <summary>A pre-cutover legacy supplier payment (one <c>supplier_inv_pay</c> row) as a payment detail — the single invoice it settled.</summary>
    private async Task<ActionResult<SupplierPaymentDetail>> LegacyPaymentDetail(
        long payId, List<long> accessible, CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var pay = await _legacy.SupplierInvPays
            .Where(p => p.Id == (int)payId)
            .Select(p => new { p.Supinvid, p.Paiddate, p.Referenceno, p.PayMethod })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pay is null || !long.TryParse(pay.Supinvid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var invId))
        {
            return NotFound();
        }

        var inv = await _legacy.SupplierInvoices
            .Where(h => h.Id == invId)
            .Select(h => new { h.Id, h.Invno, h.Supcode, h.Amount, h.Company })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (inv is null || inv.Company is null || !accessibleText.Contains(inv.Company))
        {
            return NotFound();
        }

        var supplier = inv.Supcode is null
            ? null
            : await _db.Suppliers.Where(s => s.Code == inv.Supcode).Select(s => new { s.Name, s.Code }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var companyName = long.TryParse(inv.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var amount = LegacyValue.Money(inv.Amount);
        var allocations = new List<SupplierPaymentAllocationLine> { new(inv.Id, inv.Invno, amount) };

        return Ok(new SupplierPaymentDetail(
            -payId, LegacyValue.Date(pay.Paiddate) ?? DateOnly.MinValue, companyName,
            supplier?.Name ?? inv.Supcode, supplier?.Code ?? inv.Supcode, amount, pay.PayMethod, pay.Referenceno, 0, allocations, "legacy"));
    }

    /// <summary>The pre-cutover legacy supplier payments (supplier_inv_pay, data_origin NULL), joined for supplier + amount.</summary>
    private async Task<List<SupplierPaymentSummary>> LegacySupplierPayments(CancellationToken cancellationToken)
    {
        var accessibleText = _company.Accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        // != "new" is null-aware in EF (matches NULL and 'legacy'), so it is exactly the pre-cutover rows.
        var legacyPays = await _legacy.SupplierInvPays
            .Where(p => p.DataOrigin != "new")
            .Select(p => new { p.Id, p.Supinvid, p.Paiddate, p.Referenceno, p.PayMethod })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (legacyPays.Count == 0)
        {
            return [];
        }

        var pays = legacyPays
            .Select(p => new
            {
                p.Id,
                InvId = long.TryParse(p.Supinvid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (long?)null,
                p.Paiddate,
                p.Referenceno,
                p.PayMethod,
            })
            .Where(p => p.InvId != null)
            .ToList();

        var invIds = pays.Select(p => p.InvId!.Value).Distinct().ToList();
        var invoices = await _legacy.SupplierInvoices
            .Where(h => invIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Supcode, h.Amount, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var invById = invoices.ToDictionary(i => i.Id);

        var supcodes = invoices.Select(i => i.Supcode).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Suppliers
            .Where(s => s.Code != null && supcodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(s => s.Code!, s => s.Name);

        var rows = new List<SupplierPaymentSummary>();
        foreach (var p in pays)
        {
            if (!invById.TryGetValue(p.InvId!.Value, out var inv)) continue;
            if (inv.Company is null || !accessibleText.Contains(inv.Company)) continue;

            rows.Add(new SupplierPaymentSummary(
                -p.Id, // negative id: legacy rows are display-only, and it avoids colliding with a new payment's id
                LegacyValue.Date(p.Paiddate) ?? DateOnly.MinValue,
                inv.Supcode is not null ? namesByCode.GetValueOrDefault(inv.Supcode) : null,
                LegacyValue.Money(inv.Amount),
                p.PayMethod,
                p.Referenceno,
                1,
                "legacy"));
        }

        return rows;
    }

    /// <summary>Record a supplier payment — allocated across open invoices; posts Payment entries and dual-writes the legacy shadow.</summary>
    [HttpPost]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<SupplierPaymentCreatedResponse>> Create(
        CreateSupplierPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record a payment in that company.");
        }

        try
        {
            var created = await _creator.CreateAsync(
                new NewSupplierPayment(
                    request.CompanyId, request.SupplierId, request.Date, request.Method, request.Reference,
                    request.IdempotencyKey,
                    request.Allocations.Select(a => new NewSupplierPaymentAllocation(a.SupplierInvoiceId, a.Amount)).ToList()),
                cancellationToken).ConfigureAwait(false);

            return Ok(new SupplierPaymentCreatedResponse(created.Id, created.Amount, created.AlreadyExisted));
        }
        catch (SupplierPaymentAllocationExceedsOutstandingException over)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }
        catch (SupplierPaymentInvoiceMismatchException mismatch)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: mismatch.Message);
        }
    }

    /// <summary>Void a supplier payment — soft, reason-gated; reverses each allocation through a compensating entry.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.SupplierInvoice)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.SupplierPayments
            .IgnoreQueryFilters()
            .Where(p => p.Id == id && p.DeletedAt == null)
            .Select(p => p.CompanyId)
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
                title: "This payment was changed by someone else. Reload and try again.");
        }
    }
}

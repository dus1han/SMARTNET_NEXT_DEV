using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Supplier invoices — the accounts-payable module (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// A header-only record whose payable and payments are entries in the payables ledger, so the outstanding
/// is derived (never a stored, mutated column) and partial payments work — neither of which the legacy
/// binary <c>paymentstat</c> could do. "Paid" is the derived fact <c>Σ = 0</c>, dual-written to the legacy
/// flag for the surviving legacy reports.
/// </remarks>
[ApiController]
[Route("api/supplier-invoices")]
public sealed class SupplierInvoicesController : ControllerBase
{
    private readonly ISupplierInvoiceCreator _creator;
    private readonly ISupplierInvoicePayments _payments;
    private readonly IPayablesLedger _ledger;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public SupplierInvoicesController(
        ISupplierInvoiceCreator creator,
        ISupplierInvoicePayments payments,
        IPayablesLedger ledger,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _creator = creator;
        _payments = payments;
        _ledger = ledger;
        _company = company;
        _db = db;
        _legacy = legacy;
    }

    /// <summary>Every supplier invoice the caller may see, newest first — this app's own and the legacy ones.</summary>
    [HttpGet]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<IReadOnlyList<SupplierInvoiceSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // --- New supplier invoices ------------------------------------------------------------------
        var invoices = await _db.SupplierInvoices
            .Where(s => s.CompanyId != null && accessible.Contains(s.CompanyId.Value))
            .Select(s => new { s.Id, s.SupplierReference, s.Date, s.SupplierId, s.Amount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = invoices.Select(s => s.Id).ToList();
        // Derived outstanding per invoice, in one grouped query over the ledger.
        var outstandingById = (await _db.PayablesLedger
            .Where(e => e.SupplierInvoiceId != null && ids.Contains(e.SupplierInvoiceId.Value))
            .GroupBy(e => e.SupplierInvoiceId!.Value)
            .Select(g => new { Id = g.Key, Outstanding = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.Outstanding);

        var supplierIds = invoices.Select(s => s.SupplierId).Distinct().ToList();
        var names = await _db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = invoices.Select(s =>
        {
            var outstanding = outstandingById.GetValueOrDefault(s.Id, s.Amount);
            return new SupplierInvoiceSummary(
                s.Id, s.SupplierReference, s.Date, names.GetValueOrDefault(s.SupplierId),
                s.Amount, outstanding, outstanding == 0m ? "Paid" : "Pending", "new");
        }).ToList();

        // --- Legacy supplier invoices ---------------------------------------------------------------
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var legacy = await _legacy.SupplierInvoices
            .Where(h => h.DataOrigin != "new")
            .Select(h => new { h.Id, h.Invno, h.Invdate, h.Supcode, h.Amount, h.Paymentstat, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        legacy = legacy.Where(h => h.Company != null && accessibleText.Contains(h.Company)).ToList();

        var legacyCodes = legacy.Select(h => h.Supcode).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Suppliers
            .Where(s => s.Code != null && legacyCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(s => s.Code!, s => s.Name);

        rows.AddRange(legacy.Select(h =>
        {
            var amount = LegacyValue.Money(h.Amount);
            var paid = string.Equals(h.Paymentstat, "Paid", StringComparison.OrdinalIgnoreCase);
            return new SupplierInvoiceSummary(
                h.Id, h.Invno, LegacyValue.Date(h.Invdate) ?? DateOnly.MinValue,
                h.Supcode is not null ? namesByCode.GetValueOrDefault(h.Supcode) : null,
                amount, paid ? 0m : amount, paid ? "Paid" : "Pending", "legacy");
        }));

        return Ok(rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>One supplier invoice in full — its derived outstanding and payment history.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<SupplierInvoiceDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var invoice = await _db.SupplierInvoices
            .FirstOrDefaultAsync(
                s => s.Id == id && s.CompanyId != null && accessible.Contains(s.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return await LegacySupplierInvoiceDetail(id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == invoice.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        var companyName = invoice.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var outstanding = await _ledger.OutstandingForInvoiceAsync(id, cancellationToken).ConfigureAwait(false);

        // Payment history from the ledger — the amounts are here (the legacy supplier_inv_pay has none).
        var payments = await _db.PayablesLedger
            .Where(e => e.SupplierInvoiceId == id && e.Type == PayablesLedgerEntryType.Payment)
            .OrderBy(e => e.Id)
            .Select(e => new SupplierInvoicePaymentLine(
                DateOnly.FromDateTime(e.OccurredAt), -e.Amount, null, e.Note))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(new SupplierInvoiceDetail(
            invoice.Id, invoice.SupplierReference, invoice.Date, companyName,
            supplier?.Name, supplier?.Code, invoice.NetTotal, invoice.TaxRatePercentage, invoice.TaxAmount,
            invoice.Amount, outstanding, outstanding == 0m ? "Paid" : "Pending",
            invoice.RowVersion, "new", payments));
    }

    private async Task<ActionResult<SupplierInvoiceDetail>> LegacySupplierInvoiceDetail(
        long id, List<long> accessible, CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var h = await _legacy.SupplierInvoices
            .FirstOrDefaultAsync(x => x.Id == id && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h is null || h.Company is null || !accessibleText.Contains(h.Company))
        {
            return NotFound();
        }

        var supplier = h.Supcode is null
            ? null
            : await _db.Suppliers.FirstOrDefaultAsync(s => s.Code == h.Supcode, cancellationToken).ConfigureAwait(false);
        var companyName = long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var amount = LegacyValue.Money(h.Amount);
        var net = LegacyValue.Money(h.Novattotal);
        var paid = string.Equals(h.Paymentstat, "Paid", StringComparison.OrdinalIgnoreCase);

        // Legacy payments live in supplier_inv_pay — date/method/reference, but no per-payment amount.
        var idText = id.ToString(CultureInfo.InvariantCulture);
        var payments = (await _legacy.SupplierInvPays
            .Where(p => p.Supinvid == idText)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(p => new SupplierInvoicePaymentLine(
                LegacyValue.Date(p.Paiddate) ?? DateOnly.MinValue, 0m, p.PayMethod, p.Referenceno))
            .ToList();

        return Ok(new SupplierInvoiceDetail(
            h.Id, h.Invno, LegacyValue.Date(h.Invdate) ?? DateOnly.MinValue, companyName,
            supplier?.Name ?? h.Supcode, supplier?.Code ?? h.Supcode, net, LegacyValue.Money(h.Vper),
            amount - net, amount, paid ? 0m : amount, paid ? "Paid" : "Pending", 0, "legacy", payments));
    }

    /// <summary>Record a supplier invoice — a header-only AP record; posts the payable.</summary>
    [HttpPost]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<SupplierInvoiceCreatedResponse>> Create(
        CreateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record a supplier invoice in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewSupplierInvoice(
                request.CompanyId, request.SupplierId, request.SupplierReference,
                request.Date, request.NetTotal, request.TaxRatePercentage, request.Amount),
            cancellationToken).ConfigureAwait(false);

        return Ok(new SupplierInvoiceCreatedResponse(created.Id, created.SupplierReference, created.Amount));
    }

    /// <summary>Record a (partial) payment against a supplier invoice — a ledger entry; "paid" is derived.</summary>
    [HttpPost("{id:long}/payments")]
    [RequirePermission(Permissions.SupplierInvoice)]
    public async Task<ActionResult<SupplierPaymentRecordedResponse>> RecordPayment(
        long id, RecordSupplierPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!await CallerMaySee(id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        try
        {
            var result = await _payments.RecordPaymentAsync(
                id, new RecordSupplierPayment(request.Amount, request.Date, request.Method, request.Reference),
                cancellationToken).ConfigureAwait(false);

            return Ok(new SupplierPaymentRecordedResponse(result.SupplierInvoiceId, result.AmountPaid, result.Outstanding));
        }
        catch (SupplierPaymentExceedsOutstandingException over)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }
    }

    /// <summary>Void a supplier invoice — soft, reason-gated; reverses the payable through a compensating entry.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.SupplierInvoice)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        if (!await CallerMaySee(id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        try
        {
            await _payments.DeleteAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This supplier invoice was changed by someone else. Reload and try again.");
        }
    }

    private async Task<bool> CallerMaySee(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.SupplierInvoices
            .IgnoreQueryFilters()
            .Where(s => s.Id == id && s.DeletedAt == null)
            .Select(s => s.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return companyId is not null && accessible.Contains(companyId.Value);
    }
}

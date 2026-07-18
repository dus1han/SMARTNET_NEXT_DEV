using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Api.Mailing;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Reporting;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Purchase orders — the documents engine's supply-side document (Phase 6, slice 1).
/// </summary>
/// <remarks>
/// The quotation engine, addressed to a supplier: a PO <b>charges nothing and issues nothing</b> — it is
/// an order, not a payable (the supplier invoice is) and not a receipt (the deferred GRN is). Its item
/// lines carry an item id and cost so the future goods receipt can receive against them; the one company
/// rate is resolved at the PO's date and snapshotted, fixing the legacy <c>CURDATE()</c> drift.
/// </remarks>
[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderCreator _creator;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly ITaxEngine _tax;

    private readonly IPurchaseOrderRenderer _orderPdf;
    private readonly IPurchaseOrderEditor _editor;
    private readonly IPurchaseOrderDeleter _deleter;
    private readonly IAuditWriter _audit;
    private readonly DocumentMailer _mailer;

    public PurchaseOrdersController(
        IPurchaseOrderCreator creator,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ITaxEngine tax,
        IPurchaseOrderRenderer orderPdf,
        IPurchaseOrderEditor editor,
        IPurchaseOrderDeleter deleter,
        IAuditWriter audit,
        DocumentMailer mailer)
    {
        _editor = editor;
        _deleter = deleter;
        _creator = creator;
        _company = company;
        _db = db;
        _legacy = legacy;
        _tax = tax;
        _orderPdf = orderPdf;
        _audit = audit;
        _mailer = mailer;
    }

    /// <summary>
    /// Edits a purchase order — versioned, reason-gated, concurrency-guarded.
    /// </summary>
    /// <remarks>
    /// An order posts no ledger entry and no stock movement, so an edit re-values the document and nothing
    /// else. Moving the date re-rates it at the rate in force then.
    /// </remarks>
    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.PurchaseOrder)]
    [RequireChangeReason]
    public async Task<ActionResult<PurchaseOrderEditedResponse>> Edit(
        long id,
        EditPurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (await VisibleOrderAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound();
        }

        try
        {
            var edited = await _editor.EditAsync(
                id,
                new EditPurchaseOrder(
                    request.ExpectedRowVersion,
                    request.DocumentDiscountPercent,
                    [.. request.Lines.Select(l => new EditPurchaseOrderLine(
                        l.Id, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                    request.DocumentCost,
                    request.Date),
                cancellationToken).ConfigureAwait(false);

            return Ok(new PurchaseOrderEditedResponse(edited.Id, edited.Number, edited.Total, edited.VersionNo));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This purchase order was changed by someone else while you were editing it. Reload and try again.");
        }
    }

    /// <summary>Voids a purchase order — soft, recoverable, reason-gated. Nothing to reverse.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.PurchaseOrder)]
    [RequireChangeReason]
    public async Task<ActionResult<PurchaseOrderDeleted>> Delete(
        long id,
        [FromQuery] int expectedRowVersion,
        CancellationToken cancellationToken)
    {
        if (await VisibleOrderAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound();
        }

        try
        {
            return Ok(await _deleter.DeleteAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This purchase order was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>This purchase order as a printable PDF, in its company's own profile.</summary>
    /// <remarks>Serves a legacy order as readily as an adopted one — the renderer reads the legacy columns.</remarks>
    [HttpGet("{id:long}/pdf")]
    [RequirePermission(Permissions.SearchPurchaseOrder)]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        if (await VisibleOrderAsync(id, cancellationToken).ConfigureAwait(false) is not { } order)
        {
            return NotFound();
        }

        var pdf = await _orderPdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        await _audit.RecordAsync(
            AuditAction.Print,
            nameof(PurchaseOrder),
            id.ToString(CultureInfo.InvariantCulture),
            details: new { orderNo = order.Number, document = "purchase-order" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(pdf, "application/pdf", $"purchase-order-{order.Number}.pdf");
    }

    /// <summary>Who this order can be emailed to — the supplier's addresses on file.</summary>
    [HttpGet("{id:long}/recipients")]
    [RequirePermission(Permissions.SearchPurchaseOrder)]
    public async Task<ActionResult<PurchaseOrderRecipients>> Recipients(long id, CancellationToken cancellationToken)
    {
        if (await VisibleOrderAsync(id, cancellationToken).ConfigureAwait(false) is not { } order)
        {
            return NotFound();
        }

        var contacts = await _mailer.ContactsBySupplierCodeAsync(order.SupplierCode, cancellationToken).ConfigureAwait(false);
        var (subject, body) = OrderMessage(order.Number, await CompanyNameAsync(order.CompanyId, cancellationToken).ConfigureAwait(false));

        var blocked = contacts.Count == 0
            ? "This supplier has no email address on file. Add one on the supplier first."
            : await _mailer.BlockedReasonAsync(order.CompanyId, contacts.Count, cancellationToken).ConfigureAwait(false);

        return Ok(new PurchaseOrderRecipients(
            contacts,
            subject,
            body,
            $"purchase-order-{order.Number}.pdf",
            blocked));
    }

    /// <summary>Emails the purchase order, as a PDF attachment, to the chosen supplier addresses.</summary>
    [HttpPost("{id:long}/email")]
    [RequirePermission(Permissions.PurchaseOrder)]
    public async Task<ActionResult<EmailDocumentResponse>> Email(
        long id,
        EmailDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (await VisibleOrderAsync(id, cancellationToken).ConfigureAwait(false) is not { } order)
        {
            return NotFound();
        }

        // Re-resolved against this order's own supplier: a posted id cannot address anybody else.
        var offered = await _mailer.ContactsBySupplierCodeAsync(order.SupplierCode, cancellationToken).ConfigureAwait(false);
        var chosen = offered.Where(c => request.ContactIds.Contains(c.Id)).ToList();

        if (chosen.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "None of the chosen addresses belong to this order's supplier.",
            });
        }

        var pdf = await _orderPdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        var (subject, body) = OrderMessage(order.Number, await CompanyNameAsync(order.CompanyId, cancellationToken).ConfigureAwait(false));
        var recipients = chosen.Select(c => c.Email).ToList();

        var result = await _mailer.SendAsync(
            order.CompanyId,
            recipients,
            subject,
            body,
            [new MailAttachment($"purchase-order-{order.Number}.pdf", "application/pdf", pdf)],
            cancellationToken).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditAction.Email,
            nameof(PurchaseOrder),
            id.ToString(CultureInfo.InvariantCulture),
            details: new
            {
                orderNo = order.Number,
                document = "purchase-order",
                to = recipients,
                sent = result.Sent,
                error = result.Error,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return Ok(new EmailDocumentResponse(result.Sent, recipients, result.Error));
    }

    /// <summary>The order's identity, only if the caller's companies include the one that owns it.</summary>
    private async Task<VisibleOrder?> VisibleOrderAsync(long id, CancellationToken cancellationToken)
    {
        var meta = await _legacy.PoHs
            .Where(p => p.Id == id)
            .Select(p => new { p.PoNo, p.Company, p.Supplier })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (meta is null
            || !long.TryParse(meta.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId)
            || !_company.Accessible.Contains(companyId))
        {
            return null;
        }

        return new VisibleOrder(companyId, (meta.PoNo ?? string.Empty).Trim(), meta.Supplier);
    }

    private async Task<string> CompanyNameAsync(long companyId, CancellationToken cancellationToken) =>
        await _db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "SMARTNET";

    /// <summary>The covering message. Fixed, as the job sheet's and the quotation's are.</summary>
    private static (string Subject, string Body) OrderMessage(string number, string companyName) =>
        ($"Purchase Order {number} — {companyName}",
         $"""
          <p>Dear Supplier,</p>
          <p>Please find attached our purchase order <strong>{number}</strong>.</p>
          <p>Kindly quote this order number on your invoice and delivery note.</p>
          <p>Thank you,<br />{companyName}</p>
          """);

    /// <summary>An order the caller may see: its company, its number and its supplier code.</summary>
    private sealed record VisibleOrder(long CompanyId, string Number, string? SupplierCode);

    /// <summary>
    /// Every purchase order the caller may see, newest first — the ones this app has raised <b>and</b> the
    /// ones adopted from the legacy system (its stored <c>varchar</c> figures, parsed defensively).
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.SearchPurchaseOrder)]
    public async Task<ActionResult<IReadOnlyList<PurchaseOrderSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // --- New purchase orders (this app's own) ---------------------------------------------------
        var orders = await _db.PurchaseOrders
            .Where(p => p.CompanyId != null && accessible.Contains(p.CompanyId.Value))
            .Select(p => new { p.Id, p.Number, p.Date, p.SupplierId, p.Total })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var supplierIds = orders.Select(p => p.SupplierId).Distinct().ToList();
        var names = await _db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = orders.Select(p => new PurchaseOrderSummary(
            p.Id,
            p.Number,
            p.Date,
            names.GetValueOrDefault(p.SupplierId),
            p.Total,
            "new")).ToList();

        // --- Legacy purchase orders (adopted from the old system) -----------------------------------
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var legacy = await _legacy.PoHs
            .Where(h => h.DataOrigin != "new") // legacy rows only
            .Select(h => new { h.Id, h.PoNo, h.Podate, h.Supplier, h.Totamount, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        legacy = legacy.Where(h => h.Company != null && accessibleText.Contains(h.Company)).ToList();

        var legacyCodes = legacy.Select(h => h.Supplier).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Suppliers
            .Where(s => s.Code != null && legacyCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(s => s.Code!, s => s.Name);

        rows.AddRange(legacy.Select(h => new PurchaseOrderSummary(
            h.Id,
            h.PoNo ?? "—",
            LegacyValue.Date(h.Podate) ?? DateOnly.MinValue,
            h.Supplier is not null ? namesByCode.GetValueOrDefault(h.Supplier) : null,
            LegacyValue.Money(h.Totamount),
            "legacy")));

        return Ok(rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>
    /// One purchase order in full — the read view. Serves both a <c>new</c> PO and a <c>legacy</c> one
    /// adopted from the old system (its stored <c>varchar</c> figures).
    /// </summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SearchPurchaseOrder)]
    public async Task<ActionResult<PurchaseOrderDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var order = await _db.PurchaseOrders
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(
                p => p.Id == id && p.CompanyId != null && accessible.Contains(p.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        // Not one of this app's own — it may be a legacy PO adopted into the same table.
        if (order is null)
        {
            return await LegacyPurchaseOrderDetail(id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == order.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var companyName = order.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var kind = order.Lines.Any(l => l.ItemId is not null) ? "Item" : "Service";

        return Ok(new PurchaseOrderDetail(
            order.Id,
            order.Number,
            order.Date,
            companyName,
            kind,
            supplier?.Name,
            supplier?.Code,
            order.Subtotal,
            order.DiscountAmount,
            order.DiscountPercent,
            order.NetTotal,
            order.TaxRatePercentage,
            order.TaxAmount,
            order.Total,
            order.RowVersion,
            "new",
            [.. order.Lines.Select(l => new InvoiceLineDetail(
                l.Id, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>
    /// The read view for a legacy purchase order — the same shape, built from the old system's
    /// <c>varchar</c> columns, parsed defensively (<see cref="LegacyValue"/>). A legacy PO line is free
    /// text (no item linkage), so its kind is Service.
    /// </summary>
    private async Task<ActionResult<PurchaseOrderDetail>> LegacyPurchaseOrderDetail(
        long id,
        List<long> accessible,
        CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var h = await _legacy.PoHs
            .FirstOrDefaultAsync(x => x.Id == id && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h is null || h.Company is null || !accessibleText.Contains(h.Company))
        {
            return NotFound();
        }

        var lines = await _legacy.PoLs
            .Where(l => l.Pono == h.PoNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var supplier = h.Supplier is null
            ? null
            : await _db.Suppliers.FirstOrDefaultAsync(s => s.Code == h.Supplier, cancellationToken).ConfigureAwait(false);

        var companyName = long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyCompanyId)
            ? await _db.Companies.Where(c => c.Id == legacyCompanyId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        // The legacy PO header records the taxable total (nonvattotal) and the VAT-inclusive total; it has
        // no separate pre-discount subtotal, so subtotal = net here.
        var net = LegacyValue.Money(h.Nonvattotal);
        var total = LegacyValue.Money(h.Totamount);

        return Ok(new PurchaseOrderDetail(
            h.Id,
            h.PoNo ?? "—",
            LegacyValue.Date(h.Podate) ?? DateOnly.MinValue,
            companyName,
            "Service",
            supplier?.Name ?? h.Supplier,
            supplier?.Code ?? h.Supplier,
            net,
            0m,
            0m,
            net,
            LegacyValue.Money(h.Vatpercent),
            total - net,
            total,
            0,
            "legacy",
            [.. lines.Select(l => new InvoiceLineDetail(
                null,
                null,
                null,
                l.Desc,
                LegacyValue.Money(l.Qty),
                LegacyValue.Money(l.Rate),
                0m,
                LegacyValue.Money(l.Total),
                LegacyValue.Money(l.Total),
                null))]));
    }

    /// <summary>
    /// The single VAT rate a new PO would carry for a company on a date — the same preview the New Invoice
    /// screen gets, gated by the purchase-order permission.
    /// </summary>
    [HttpGet("tax-rate")]
    [RequirePermission(Permissions.PurchaseOrder)]
    public async Task<ActionResult<InvoiceTaxRate>> TaxRate(
        long companyId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(companyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise a purchase order in that company.");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return NotFound();
        }

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == companyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var calc = _tax.Calculate(new TaxCalculationRequest(
                date, company.IsVatRegistered, TaxRounding.PerLine, [], rates));

            return Ok(new InvoiceTaxRate(calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage));
        }
        catch (TaxRateNotResolvableException notInForce)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notInForce.Message);
        }
    }

    /// <summary>Raise a purchase order — the whole document, posted once. No ledger, no stock.</summary>
    [HttpPost]
    [RequirePermission(Permissions.PurchaseOrder)]
    public async Task<ActionResult<PurchaseOrderCreatedResponse>> Create(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise a purchase order in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewPurchaseOrder(
                request.CompanyId,
                request.SupplierId,
                request.Date,
                [.. request.Lines.Select(l => new NewPurchaseOrderLine(
                    l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                request.DocumentDiscountPercent),
            cancellationToken).ConfigureAwait(false);

        return Ok(new PurchaseOrderCreatedResponse(created.Id, created.Number, created.Total));
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using System.Globalization;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Invoices — the documents engine's first writer (Phase 5, slice 1).
/// </summary>
/// <remarks>
/// One controller for what the legacy app split across four (item vs service, create vs edit): a line
/// either references an item or is free-typed, and that is the whole difference. The browser holds the
/// draft and posts it whole — the server-session cart is gone (D4). The save is one transaction behind
/// <see cref="IInvoiceCreator"/>: number, header, lines, ledger, stock and snapshot, all or none.
/// </remarks>
[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceCreator _creator;
    private readonly IInvoiceEditor _editor;
    private readonly IInvoiceDeleter _deleter;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly ITaxEngine _tax;
    private readonly IReceivablesLedger _ledger;
    private readonly IBusinessRuleReader _rules;

    public InvoicesController(
        IInvoiceCreator creator,
        IInvoiceEditor editor,
        IInvoiceDeleter deleter,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ITaxEngine tax,
        IReceivablesLedger ledger,
        IBusinessRuleReader rules)
    {
        _creator = creator;
        _editor = editor;
        _deleter = deleter;
        _company = company;
        _db = db;
        _legacy = legacy;
        _tax = tax;
        _ledger = ledger;
        _rules = rules;
    }

    /// <summary>
    /// Every invoice the caller may see, newest first — the ones this app has raised <b>and</b> the ones
    /// adopted from the legacy system.
    /// </summary>
    /// <remarks>
    /// New and legacy invoices share the one <c>invoice_h</c> table, told apart by <c>data_origin</c>. A
    /// new invoice carries typed <c>decimal</c> figures and a ledger-derived outstanding; a legacy one
    /// carries the old system's <c>varchar</c> figures, parsed defensively (a bad value is 0, never an
    /// exception — <see cref="LegacyValue"/>) and shown read-only, since there is no new-side detail view
    /// for it. The two are merged and ordered by date.
    /// </remarks>
    [HttpGet]
    [RequirePermission(Permissions.SearchInvoice)]
    public async Task<ActionResult<IReadOnlyList<InvoiceSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // --- New invoices (this app's own) ----------------------------------------------------------
        var invoices = await _db.Invoices
            .Where(i => i.CompanyId != null && accessible.Contains(i.CompanyId.Value))
            .Select(i => new { i.Id, i.Number, i.Date, i.CustomerId, i.Type, i.Total })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        // Outstanding is derived, per invoice, from the ledger — never a stored column (B3).
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var outstanding = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId != null && invoiceIds.Contains(e.InvoiceId.Value))
            .GroupBy(e => e.InvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Balance = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Balance, cancellationToken)
            .ConfigureAwait(false);

        var rows = invoices.Select(i => new InvoiceSummary(
            i.Id,
            i.Number,
            i.Date,
            names.GetValueOrDefault(i.CustomerId),
            i.Type.ToString(),
            i.Total,
            outstanding.GetValueOrDefault(i.Id),
            "new")).ToList();

        // --- Legacy invoices (adopted from the old system) ------------------------------------------
        // The legacy company column is a varchar holding the id; match the accessible ids as strings.
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var legacy = await _legacy.InvoiceHs
            .Where(h => h.DataOrigin != "new") // legacy rows only — the new ones are already above
            .Select(h => new { h.Id, h.Invoiceno, h.Indate, h.Customer, h.Invtype, h.Totamount, h.Balance, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        legacy = legacy.Where(h => h.Company != null && accessibleText.Contains(h.Company)).ToList();

        // Resolve customer names by code from the adopted customer master, in one pass.
        var legacyCodes = legacy.Select(h => h.Customer).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Customers
            .Where(c => c.Code != null && legacyCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(c => c.Code!, c => c.Name);

        rows.AddRange(legacy.Select(h => new InvoiceSummary(
            h.Id,
            h.Invoiceno ?? "—",
            LegacyValue.Date(h.Indate) ?? DateOnly.MinValue,
            h.Customer is not null ? namesByCode.GetValueOrDefault(h.Customer) : null,
            h.Invtype ?? "—",
            LegacyValue.Money(h.Totamount),
            LegacyValue.Money(h.Balance),
            "legacy")));

        return Ok(rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>
    /// One invoice in full — the read view. Serves both a <c>new</c> invoice (typed figures, ledger
    /// balance) and a <c>legacy</c> one adopted from the old system (its stored <c>varchar</c> figures).
    /// </summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SearchInvoice)]
    public async Task<ActionResult<InvoiceDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var invoice = await _db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(
                i => i.Id == id && i.CompanyId != null && accessible.Contains(i.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        // Not one of this app's own — it may be a legacy invoice adopted into the same table.
        if (invoice is null)
        {
            return await LegacyInvoiceDetail(id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        var companyName = invoice.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var outstanding = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId == id)
            .SumAsync(e => e.Amount, cancellationToken)
            .ConfigureAwait(false);

        // Item vs service is the line-level distinction (slice 0-B): an invoice with any item line is an
        // item invoice, otherwise a service one.
        var kind = invoice.Lines.Any(l => l.ItemId is not null) ? "Item" : "Service";

        return Ok(new InvoiceDetail(
            invoice.Id,
            invoice.Number,
            invoice.Date,
            invoice.Type.ToString(),
            companyName,
            kind,
            customer?.Name,
            customer?.Code,
            invoice.PurchaseOrderNo,
            invoice.ContactPerson,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.DiscountPercent,
            invoice.NetTotal,
            invoice.TaxRatePercentage,
            invoice.TaxAmount,
            invoice.Total,
            invoice.Cost,
            outstanding,
            invoice.RowVersion,
            "new",
            [.. invoice.Lines.Select(l => new InvoiceLineDetail(
                l.Id, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>
    /// The read view for a legacy invoice — the same shape, built from the old system's <c>varchar</c>
    /// columns, parsed defensively (a bad value is 0, never an exception — <see cref="LegacyValue"/>). Its
    /// figures are reconstructed as they were stored: the discount is subtotal less the after-discount
    /// net, the tax is total less net. Lines come from <c>invoice_l</c> by the legacy number; a legacy
    /// line carries no per-line discount or item reference, so those are 0/absent.
    /// </summary>
    private async Task<ActionResult<InvoiceDetail>> LegacyInvoiceDetail(
        long id,
        List<long> accessible,
        CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var h = await _legacy.InvoiceHs
            .FirstOrDefaultAsync(x => x.Id == id && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h is null || h.Company is null || !accessibleText.Contains(h.Company))
        {
            return NotFound();
        }

        var lines = await _legacy.InvoiceLs
            .Where(l => l.Inno == h.Invoiceno)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Resolve the line item codes to ids, so a legacy invoice edited in the new app keeps its item
        // linkage (the edit sends these ids back, and the adoption/reconcile keep the stock link).
        var lineCodes = lines.Select(l => l.Itemcode).Where(c => c != null).Distinct().ToList();
        var itemIdsByCode = (await _db.Items
            .Where(i => i.Code != null && lineCodes.Contains(i.Code))
            .Select(i => new { i.Id, i.Code })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(i => i.Code!, i => i.Id, StringComparer.Ordinal);
        long? ItemIdFor(string? code) => code is not null && itemIdsByCode.TryGetValue(code, out var iid) ? iid : null;

        var customer = h.Customer is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);

        // The legacy company column is the id as a string; resolve its name from the adopted companies.
        var companyName = long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyCompanyId)
            ? await _db.Companies.Where(c => c.Id == legacyCompanyId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var subtotal = LegacyValue.Money(h.Beforedisctot);
        var net = LegacyValue.Money(h.Novattotal);
        var total = LegacyValue.Money(h.Totamount);

        // The legacy `it` column already records ITEM vs SERVICE.
        var kind = string.Equals(h.It, "ITEM", StringComparison.OrdinalIgnoreCase) ? "Item" : "Service";

        return Ok(new InvoiceDetail(
            h.Id,
            h.Invoiceno ?? "—",
            LegacyValue.Date(h.Indate) ?? DateOnly.MinValue,
            h.Invtype ?? "—",
            companyName,
            kind,
            customer?.Name ?? h.Customer,
            customer?.Code ?? h.Customer,
            h.Pono,
            h.Contactperson,
            subtotal,
            subtotal - net, // discount = pre-discount subtotal less the after-discount net
            LegacyValue.Money(h.Discountper), // the document discount rate, as stored
            net,
            LegacyValue.Money(h.Vper),
            total - net, // tax = grand total less the pre-VAT net
            total,
            LegacyValue.Money(h.Cost), // the stored document cost (item = summed, service = entered)
            LegacyValue.Money(h.Balance),
            h.RowVersion, // the legacy row's version, so an edit adopts it under a real concurrency guard
            "legacy",
            [.. lines.Select(l => new InvoiceLineDetail(
                l.Id,
                ItemIdFor(l.Itemcode),
                l.Itemcode,
                l.Desc,
                LegacyValue.Money(l.Qty),
                LegacyValue.Money(l.Rate),
                0m,
                LegacyValue.Money(l.Tot),
                LegacyValue.Money(l.Tot),
                null))]));
    }

    /// <summary>
    /// The VAT rate a new invoice would carry for a company on a date — one rate per document.
    /// </summary>
    /// <remarks>
    /// The New Invoice screen holds the draft and posts it whole, so it never round-trips a line while the
    /// user types; but to show the VAT rate and value as they build the document, it needs the rate once.
    /// Resolved by the one tax engine (with no lines — only the rate is wanted), so the preview matches the
    /// figure <see cref="Create"/> will charge. Gated by <see cref="Permissions.ItemInvoice"/>: the person
    /// raising the invoice, not a settings administrator.
    /// </remarks>
    [HttpGet("tax-rate")]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceTaxRate>> TaxRate(
        long companyId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(companyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise an invoice in that company.");
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
            // A VAT-registered company with no rate configured for that date is a misconfiguration the
            // caller can act on (set a rate, or check the date), and it is the same reason the save would
            // fail — surfaced here so the screen warns before they type a whole document.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notInForce.Message);
        }
    }

    /// <summary>
    /// A customer's credit standing — for the New Invoice screen's advisory when a customer is picked.
    /// </summary>
    /// <remarks>
    /// The <b>same</b> derived-ledger balance and enforcement setting the server-side gate uses (so the
    /// warning and the gate agree), surfaced so the screen can show the outstanding and the remaining
    /// headroom up front and confirm before a save that would breach it — rather than the breach only
    /// surfacing as a 409 after the whole document is typed.
    /// </remarks>
    [HttpGet("credit-status")]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<CreditStatus>> CustomerCreditStatus(
        long customerId,
        long companyId,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(companyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise an invoice in that company.");
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            .ConfigureAwait(false);
        if (customer is null)
        {
            return NotFound();
        }

        var outstanding = await _ledger.BalanceForCustomerAsync(customerId, cancellationToken).ConfigureAwait(false);
        var enforced = BusinessRules.AsBool(
            await _rules.ResolveAsync(companyId, BusinessRules.CreditLimitEnforced, cancellationToken).ConfigureAwait(false));

        return Ok(new CreditStatus(customer.CreditLimit, outstanding, enforced));
    }

    /// <remarks>
    /// Creating is not a reason-gated action (AUDIT.md §5 — "the record is the reason"), so no
    /// <c>X-Change-Reason</c> here; editing an issued invoice, in slice 5, is.
    /// </remarks>
    [HttpPost]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceCreatedResponse>> Create(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        // Deny by default extends to the company: a caller may only raise a document in a company their
        // token grants, never one they named in the body but cannot see.
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise an invoice in that company.");
        }

        var type = request.Type == "Cash" ? InvoiceType.Cash : InvoiceType.Credit;

        InvoiceCreated created;
        try
        {
            created = await _creator.CreateAsync(
                new NewInvoice(
                    request.CompanyId,
                    request.CustomerId,
                    type,
                    request.Date,
                    request.PurchaseOrderNo,
                    request.ContactPerson,
                    [.. request.Lines.Select(l => new NewInvoiceLine(
                        l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                    request.DocumentDiscountPercent,
                    request.AcknowledgeCreditLimit,
                    request.DocumentCost),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CreditLimitExceededException over)
        {
            // A soft gate, not a dead-end: the screen catches this, shows the numbers, and re-posts with
            // AcknowledgeCreditLimit once the user confirms. The code lets it tell this 409 from any other.
            var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = over.Message };
            problem.Extensions["code"] = "credit_limit_exceeded";
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
        }

        return Ok(new InvoiceCreatedResponse(created.Id, created.Number, created.Total, created.Outstanding));
    }

    /// <summary>
    /// Edit an issued invoice — versioned, reason-gated, concurrency-guarded (Phase 5, slice 5).
    /// </summary>
    /// <remarks>
    /// A reason is mandatory (<see cref="RequireChangeReasonAttribute"/>, AUDIT.md §5) — editing money is
    /// exactly the change the audit trail exists to explain. Only a <c>new</c> invoice this app owns is
    /// editable; a legacy row is read-only. A stale <c>ExpectedRowVersion</c> — someone else edited it since
    /// the screen loaded — is a 409, not a silent overwrite (the legacy last-write-wins bug).
    /// </remarks>
    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.ItemInvoice)]
    [RequireChangeReason]
    public async Task<ActionResult<InvoiceEditedResponse>> Edit(
        long id,
        EditInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        // Only this app's own invoices are editable, and only in a company the caller may act in.
        var companyId = await _db.Invoices
            .Where(i => i.Id == id)
            .Select(i => i.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyId is null || !_company.Accessible.Contains(companyId.Value))
        {
            return NotFound();
        }

        InvoiceEdited edited;
        try
        {
            edited = await _editor.EditAsync(
                id,
                new EditInvoice(
                    request.ExpectedRowVersion,
                    request.PurchaseOrderNo,
                    request.ContactPerson,
                    request.DocumentDiscountPercent,
                    [.. request.Lines.Select(l => new EditInvoiceLine(
                        l.Id, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                    request.DocumentCost),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The invoice moved under the editor since the screen loaded it. Tell them to reload rather than
            // clobber the other change.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This invoice was changed by someone else while you were editing it. Reload and try again.");
        }
        catch (InvoiceHasPaymentsException paid)
        {
            // A paid invoice cannot be edited — the payment must be deleted first (a Phase 7 action). A code
            // lets the screen tell this refusal from a concurrency conflict.
            var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = paid.Message };
            problem.Extensions["code"] = "invoice_has_payments";
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
        }

        return Ok(new InvoiceEditedResponse(edited.Id, edited.Number, edited.Total, edited.Outstanding, edited.VersionNo));
    }

    /// <summary>
    /// Void an issued invoice — soft, recoverable, attributable (Phase 5, slice 5).
    /// </summary>
    /// <remarks>
    /// Reason-gated (AUDIT.md §5) and <c>row_version</c>-guarded. Nothing is hard-deleted: the invoice and
    /// its lines are soft-deleted, its ledger and stock effects reversed through <b>new</b> entries. The
    /// broken legacy <c>delCN</c> is not ported — this is its correct replacement.
    /// </remarks>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.ItemInvoice)]
    [RequireChangeReason]
    public async Task<ActionResult<InvoiceDeleted>> Delete(
        long id,
        [FromQuery] int expectedRowVersion,
        CancellationToken cancellationToken)
    {
        var companyId = await _db.Invoices
            .Where(i => i.Id == id)
            .Select(i => i.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyId is null || !_company.Accessible.Contains(companyId.Value))
        {
            return NotFound();
        }

        try
        {
            var deleted = await _deleter.DeleteAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false);
            return Ok(deleted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This invoice was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>
    /// The deleted-invoice register — the new-side replacement for the legacy DeletedInvoicesController.
    /// </summary>
    /// <remarks>
    /// Two sources, merged and newest-deletion-first (as the legacy screen ordered by <c>deldate</c>):
    /// <list type="bullet">
    /// <item><b>New-app voids</b> — invoices this app soft-deleted (slice 5). Who voided each, when, and
    /// the reason from the audit trail; every one can be restored from its History tab.</item>
    /// <item><b>Legacy deletions</b> — the historical <c>del_invoice_h</c> register the legacy app moved
    /// deleted invoices into (its <c>DeletedInvoicesController</c> read exactly this table). Shown
    /// read-only, since the new app never wrote them and they carry no surrogate key. Without these the
    /// register looks empty on a database whose only deletions predate the new app.</item>
    /// </list>
    /// Gated by <see cref="Permissions.DeletedInvoices"/> — seeing voided documents is its own right, as
    /// it was in the legacy app.
    /// </remarks>
    [HttpGet("deleted")]
    [RequirePermission(Permissions.DeletedInvoices)]
    public async Task<ActionResult<IReadOnlyList<DeletedInvoiceSummary>>> Deleted(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // --- New-app voids (soft-deleted) -----------------------------------------------------------
        // IgnoreQueryFilters drops the "not deleted" clause too, so re-assert data_origin and pick the
        // soft-deleted ones.
        var deleted = await _db.Invoices
            .IgnoreQueryFilters()
            .Where(i => i.DataOrigin == "new" && i.DeletedAt != null
                && i.CompanyId != null && accessible.Contains(i.CompanyId.Value))
            .Select(i => new { i.Id, i.Number, i.Date, i.CustomerId, i.Total, i.DeletedAt, i.DeletedBy })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = deleted.Select(d => d.CustomerId).Distinct().ToList();
        var customerNames = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var userIds = deleted.Where(d => d.DeletedBy != null).Select(d => d.DeletedBy!.Value).Distinct().ToList();
        var userNames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name ?? u.Username, cancellationToken)
            .ConfigureAwait(false);

        // The reason lives on the audit row the soft delete wrote — the latest Delete on each invoice.
        var ids = deleted.Select(d => d.Id.ToString(CultureInfo.InvariantCulture)).ToList();
        var reasons = (await _db.AuditLog
            .Where(a => a.EntityType == "Invoice" && a.Action == AuditAction.Delete && ids.Contains(a.EntityId))
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => new { a.EntityId, a.Reason })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(a => a.EntityId)
            .ToDictionary(g => g.Key, g => g.First().Reason);

        var rows = deleted
            .Select(d => new DeletedInvoiceSummary(
                d.Id,
                d.Number,
                d.Date,
                customerNames.GetValueOrDefault(d.CustomerId),
                d.Total,
                d.DeletedAt!.Value,
                d.DeletedBy is { } uid ? userNames.GetValueOrDefault(uid) : null,
                reasons.GetValueOrDefault(d.Id.ToString(CultureInfo.InvariantCulture))))
            .ToList();

        // --- Legacy deletions (del_invoice_h) -------------------------------------------------------
        // The historical register the legacy app kept — every invoice its DeletedInvoicesController ever
        // listed lives here, and none of it is in invoice_h. The legacy `company` column holds the id as a
        // varchar (copied verbatim from invoice_h at delete), so it is matched against the accessible ids
        // as strings — exactly as the invoice List does for adopted legacy rows.
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var legacyDeleted = await _legacy.DelInvoiceHs
            .Select(d => new
            {
                d.Invoiceno, d.Indate, d.Customer, d.Totamount, d.Company, d.Deldate, d.Deluser, d.Delreason,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        legacyDeleted = legacyDeleted.Where(d => d.Company != null && accessibleText.Contains(d.Company)).ToList();

        // Resolve customer names by code from the adopted customer master, in one pass (the legacy row
        // stores the customer code, as the old INNER JOIN cus_m ON customer = cuscode did).
        var legacyCodes = legacyDeleted.Select(d => d.Customer).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Customers
            .Where(c => c.Code != null && legacyCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(c => c.Code!, c => c.Name);

        rows.AddRange(legacyDeleted.Select(d => new DeletedInvoiceSummary(
            0, // del_invoice_h is keyless — these are history, with no new-side detail view to link to.
            d.Invoiceno ?? "—",
            LegacyValue.Date(d.Indate) ?? DateOnly.MinValue,
            d.Customer is not null ? namesByCode.GetValueOrDefault(d.Customer) : null,
            LegacyValue.Money(d.Totamount),
            ParseLegacyTimestamp(d.Deldate),
            string.IsNullOrWhiteSpace(d.Deluser) ? null : d.Deluser,
            string.IsNullOrWhiteSpace(d.Delreason) ? null : d.Delreason)));

        return Ok(rows.OrderByDescending(r => r.DeletedAt).ToList());
    }

    /// <summary>
    /// One deleted invoice in full — the detail behind a register row, keyed by document number. Resolves a
    /// <c>legacy</c> deletion (<c>del_invoice_h</c>/<c>del_invoice_l</c>) or a <c>new</c>-app void (the
    /// soft-deleted invoice), and carries <b>who deleted it, when and why for both</b> — a legacy deletion's
    /// <c>deluser</c>/<c>deldate</c>/<c>delreason</c> as much as a new void's audit trail.
    /// </summary>
    [HttpGet("deleted/{number}")]
    [RequirePermission(Permissions.DeletedInvoices)]
    public async Task<ActionResult<DeletedInvoiceDetail>> DeletedDetail(string number, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        // --- Legacy deletion (del_invoice_h) — the register the old app kept. If a number was ever deleted
        // more than once, the most recent deletion is the one shown. ---
        var legacyHeaders = await _legacy.DelInvoiceHs
            .Where(d => d.Invoiceno == number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var h = legacyHeaders
            .Where(d => d.Company != null && accessibleText.Contains(d.Company))
            .OrderByDescending(d => ParseLegacyTimestamp(d.Deldate))
            .FirstOrDefault();

        if (h is not null)
        {
            var lines = await _legacy.DelInvoiceLs
                .Where(l => l.Inno == number)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var lineCodes = lines.Select(l => l.Itemcode).Where(c => c != null).Distinct().ToList();
            var itemIdsByCode = (await _db.Items
                .Where(i => i.Code != null && lineCodes.Contains(i.Code))
                .Select(i => new { i.Id, i.Code })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToDictionary(i => i.Code!, i => i.Id, StringComparer.Ordinal);
            long? ItemIdFor(string? code) => code is not null && itemIdsByCode.TryGetValue(code, out var iid) ? iid : null;

            var customer = h.Customer is null
                ? null
                : await _db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);

            var companyName = long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
                ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;

            var subtotal = LegacyValue.Money(h.Beforedisctot);
            var net = LegacyValue.Money(h.Novattotal);
            var total = LegacyValue.Money(h.Totamount);
            var kind = string.Equals(h.It, "ITEM", StringComparison.OrdinalIgnoreCase) ? "Item" : "Service";

            return Ok(new DeletedInvoiceDetail(
                h.Invoiceno ?? number,
                LegacyValue.Date(h.Indate) ?? DateOnly.MinValue,
                h.Invtype ?? "—",
                companyName,
                kind,
                customer?.Name,
                h.Customer,
                h.Pono,
                h.Contactperson,
                subtotal,
                subtotal - net,
                LegacyValue.Money(h.Discountper),
                net,
                LegacyValue.Money(h.Vper),
                total - net,
                total,
                "legacy",
                ParseLegacyTimestamp(h.Deldate),
                string.IsNullOrWhiteSpace(h.Deluser) ? null : h.Deluser,
                string.IsNullOrWhiteSpace(h.Delreason) ? null : h.Delreason,
                [.. lines.Select(l => new InvoiceLineDetail(
                    null,
                    ItemIdFor(l.Itemcode),
                    l.Itemcode,
                    l.Desc,
                    LegacyValue.Money(l.Qty),
                    LegacyValue.Money(l.Rate),
                    0m,
                    LegacyValue.Money(l.Tot),
                    LegacyValue.Money(l.Tot),
                    null))]));
        }

        // --- New-app void (a soft-deleted invoice this app raised) ---------------------------------
        var invoice = await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(
                i => i.Number == number && i.DataOrigin == "new" && i.DeletedAt != null
                    && i.CompanyId != null && accessible.Contains(i.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return NotFound();
        }

        var newCustomer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        var newCompanyName = invoice.CompanyId is { } newCid
            ? await _db.Companies.Where(c => c.Id == newCid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var deletedByName = invoice.DeletedBy is { } uid
            ? await _db.Users.Where(u => u.Id == uid).Select(u => u.Name ?? u.Username).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var invoiceKey = invoice.Id.ToString(CultureInfo.InvariantCulture);
        var newReason = await _db.AuditLog
            .Where(a => a.EntityType == "Invoice" && a.Action == AuditAction.Delete && a.EntityId == invoiceKey)
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => a.Reason)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var newKind = invoice.Lines.Any(l => l.ItemId is not null) ? "Item" : "Service";

        // Show the lines as they stood at void; if the header and lines were soft-deleted together, fall
        // back to all of them rather than an empty list.
        var voidLines = invoice.Lines.Where(l => l.DeletedAt == null).ToList();
        if (voidLines.Count == 0)
        {
            voidLines = invoice.Lines.ToList();
        }

        return Ok(new DeletedInvoiceDetail(
            invoice.Number,
            invoice.Date,
            invoice.Type.ToString(),
            newCompanyName,
            newKind,
            newCustomer?.Name,
            newCustomer?.Code,
            invoice.PurchaseOrderNo,
            invoice.ContactPerson,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.DiscountPercent,
            invoice.NetTotal,
            invoice.TaxRatePercentage,
            invoice.TaxAmount,
            invoice.Total,
            "new",
            invoice.DeletedAt!.Value,
            deletedByName,
            newReason,
            [.. voidLines.Select(l => new InvoiceLineDetail(
                l.Id, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>
    /// Parses a legacy <c>deldate</c> — written by the old app as <c>yyyy-MM-dd HH:mm:ss</c>
    /// (SearchInvoiceController) — defensively. A blank or unreadable value sorts last as
    /// <see cref="DateTime.MinValue"/> rather than throwing, the same posture as <see cref="LegacyValue"/>.
    /// </summary>
    private static DateTime ParseLegacyTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DateTime.MinValue;
        }

        var text = raw.Trim();
        return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss",
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)
            ? exact
            : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
                ? loose
                : DateTime.MinValue;
    }
}

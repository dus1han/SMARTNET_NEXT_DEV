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
/// Credit notes — the documents engine's third document type (Phase 5, slice 4).
/// </summary>
/// <remarks>
/// The mirror of an invoice: raised against a parent invoice, it posts the <b>opposite</b> ledger sign (a
/// <c>Credit</c> that reduces the customer's balance through the ledger, never a mutated <c>balance</c>
/// column — B3) and, where it returns goods, a stock <b>receipt</b> back into stock. The customer, company
/// and VAT rate are inherited from the parent invoice — new or legacy — so a full credit nets exactly
/// against it. The broken legacy delete (<c>delCN</c>) is not ported; delete is the soft, versioned,
/// reason-gated delete of slice 5.
/// </remarks>
[ApiController]
[Route("api/credit-notes")]
public sealed class CreditNotesController : ControllerBase
{
    private readonly ICreditNoteCreator _creator;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    private readonly ICreditNoteRenderer _notePdf;
    private readonly ICreditNoteDeleter _deleter;
    private readonly IAuditWriter _audit;
    private readonly DocumentMailer _mailer;

    public CreditNotesController(
        ICreditNoteCreator creator,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ICreditNoteRenderer notePdf,
        ICreditNoteDeleter deleter,
        IAuditWriter audit,
        DocumentMailer mailer)
    {
        _creator = creator;
        _company = company;
        _db = db;
        _legacy = legacy;
        _notePdf = notePdf;
        _deleter = deleter;
        _audit = audit;
        _mailer = mailer;
    }

    /// <summary>This credit note as a printable PDF, in its company's own profile.</summary>
    [HttpGet("{id:long}/pdf")]
    [RequirePermission(Permissions.SearchCreditNote)]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        if (await VisibleNoteAsync(id, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return NotFound();
        }

        var pdf = await _notePdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        await _audit.RecordAsync(
            AuditAction.Print,
            nameof(CreditNote),
            id.ToString(CultureInfo.InvariantCulture),
            details: new { creditNoteNo = note.Number, document = "credit-note" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(pdf, "application/pdf", $"credit-note-{note.Number}.pdf");
    }

    /// <summary>Who this credit note can be emailed to — the customer's saved contacts.</summary>
    [HttpGet("{id:long}/recipients")]
    [RequirePermission(Permissions.SearchCreditNote)]
    public async Task<ActionResult<CreditNoteRecipients>> Recipients(long id, CancellationToken cancellationToken)
    {
        if (await VisibleNoteAsync(id, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return NotFound();
        }

        var contacts = await _mailer.ContactsByCodeAsync(note.CustomerCode, cancellationToken).ConfigureAwait(false);
        var (subject, body) = NoteMessage(note.Number, note.InvoiceNo, await CompanyNameAsync(note.CompanyId, cancellationToken).ConfigureAwait(false));

        return Ok(new CreditNoteRecipients(
            contacts,
            subject,
            body,
            $"credit-note-{note.Number}.pdf",
            await _mailer.BlockedReasonAsync(note.CompanyId, contacts.Count, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Emails the credit note, as a PDF attachment, to the chosen saved contacts.</summary>
    [HttpPost("{id:long}/email")]
    [RequirePermission(Permissions.SearchCreditNote)]
    public async Task<ActionResult<EmailDocumentResponse>> Email(
        long id,
        EmailDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (await VisibleNoteAsync(id, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return NotFound();
        }

        var offered = await _mailer.ContactsByCodeAsync(note.CustomerCode, cancellationToken).ConfigureAwait(false);
        var chosen = offered.Where(c => request.ContactIds.Contains(c.Id)).ToList();

        if (chosen.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "None of the chosen contacts belong to this credit note's customer.",
            });
        }

        var pdf = await _notePdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        var (subject, body) = NoteMessage(note.Number, note.InvoiceNo, await CompanyNameAsync(note.CompanyId, cancellationToken).ConfigureAwait(false));
        var recipients = chosen.Select(c => c.Email).ToList();

        var result = await _mailer.SendAsync(
            note.CompanyId,
            recipients,
            subject,
            body,
            [new MailAttachment($"credit-note-{note.Number}.pdf", "application/pdf", pdf)],
            cancellationToken).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditAction.Email,
            nameof(CreditNote),
            id.ToString(CultureInfo.InvariantCulture),
            details: new
            {
                creditNoteNo = note.Number,
                document = "credit-note",
                to = recipients,
                sent = result.Sent,
                error = result.Error,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return Ok(new EmailDocumentResponse(result.Sent, recipients, result.Error));
    }

    /// <summary>
    /// Voids a credit note — soft, recoverable, reason-gated.
    /// </summary>
    /// <remarks>
    /// A credit note is never edited, only voided: it exists to reverse an invoice, and a correction to it
    /// is a new note rather than a rewrite of one already sent. The ledger credit and any stock it returned
    /// are undone through new entries — see <see cref="ICreditNoteDeleter"/>.
    /// </remarks>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.NewCreditNote)]
    [RequireChangeReason]
    public async Task<ActionResult<CreditNoteDeleted>> Delete(
        long id,
        [FromQuery] int expectedRowVersion,
        CancellationToken cancellationToken)
    {
        if (await VisibleNoteAsync(id, cancellationToken).ConfigureAwait(false) is null)
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
                title: "This credit note was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>The note's identity, only if the caller's companies include the one that owns it.</summary>
    private async Task<VisibleNote?> VisibleNoteAsync(long id, CancellationToken cancellationToken)
    {
        var meta = await _legacy.CnHs
            .Where(c => c.Id == id)
            .Select(c => new { c.Cnno, c.CompanyId, c.Invoiceno })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (meta?.CompanyId is not { } companyId || !_company.Accessible.Contains(companyId))
        {
            return null;
        }

        // cn_h names no customer — it belongs to the invoice it credits, and that names one.
        var customerCode = string.IsNullOrWhiteSpace(meta.Invoiceno)
            ? null
            : await _legacy.InvoiceHs
                .Where(i => i.Invoiceno == meta.Invoiceno)
                .Select(i => i.Customer)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        return new VisibleNote(
            companyId,
            (meta.Cnno ?? string.Empty).Trim(),
            (meta.Invoiceno ?? string.Empty).Trim(),
            customerCode);
    }

    private async Task<string> CompanyNameAsync(long companyId, CancellationToken cancellationToken) =>
        await _db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "SMARTNET";

    /// <summary>The covering message. Fixed, as every document's is.</summary>
    private static (string Subject, string Body) NoteMessage(string number, string invoiceNo, string companyName) =>
        ($"Credit Note {number} — {companyName}",
         $"""
          <p>Dear Customer,</p>
          <p>Please find attached credit note <strong>{number}</strong>, issued against invoice
             <strong>{invoiceNo}</strong>.</p>
          <p>The amount credited reduces the balance due on that invoice.</p>
          <p>Thank you,<br />{companyName}</p>
          """);

    /// <summary>A note the caller may see: its company, its number, its invoice and its customer code.</summary>
    private sealed record VisibleNote(long CompanyId, string Number, string InvoiceNo, string? CustomerCode);

    /// <summary>
    /// Every credit note the caller may see, newest first — the ones this app has raised <b>and</b> the ones
    /// adopted from the legacy system (its stored <c>varchar</c> figures, parsed defensively).
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.SearchCreditNote)]
    public async Task<ActionResult<IReadOnlyList<CreditNoteSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // --- New credit notes (this app's own) ------------------------------------------------------
        var notes = await _db.CreditNotes
            .Where(c => c.CompanyId != null && accessible.Contains(c.CompanyId.Value))
            .Select(c => new { c.Id, c.Number, c.Date, c.CustomerId, c.InvoiceId, c.Total })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = notes.Select(c => c.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        // The parent invoice number, for the list's "credits" column.
        var invoiceIds = notes.Select(c => c.InvoiceId).Distinct().ToList();
        var invoiceNumbers = await _db.Invoices
            .Where(i => invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Number, cancellationToken)
            .ConfigureAwait(false);

        var rows = notes.Select(c => new CreditNoteSummary(
            c.Id,
            c.Number,
            c.Date,
            names.GetValueOrDefault(c.CustomerId),
            invoiceNumbers.GetValueOrDefault(c.InvoiceId, "—"),
            c.Total,
            "new")).ToList();

        // --- Legacy credit notes (adopted from the old system) --------------------------------------
        // cn_h has no legacy `company` varchar; it is scoped by the company_id the multi-company migration
        // backfilled from the parent invoice.
        var legacy = await _legacy.CnHs
            .Where(h => h.DataOrigin != "new" && h.CompanyId != null && accessible.Contains(h.CompanyId.Value))
            .Select(h => new { h.Id, h.Cnno, h.Cndate, h.Invoiceno, h.Totamount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The customer is not on cn_h; resolve it through the parent invoice's legacy customer code.
        var legacyInvoiceNos = legacy.Select(h => h.Invoiceno).Where(n => n != null).Select(n => n!).Distinct().ToList();
        var invoiceCustomerCodes = (await _legacy.InvoiceHs
            .Where(i => i.Invoiceno != null && legacyInvoiceNos.Contains(i.Invoiceno))
            .Select(i => new { i.Invoiceno, i.Customer })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(i => i.Invoiceno!)
            .ToDictionary(g => g.Key, g => g.First().Customer);

        var legacyCodes = invoiceCustomerCodes.Values.Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Customers
            .Where(c => c.Code != null && legacyCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(c => c.Code!, c => c.Name);

        rows.AddRange(legacy.Select(h =>
        {
            var code = h.Invoiceno is not null ? invoiceCustomerCodes.GetValueOrDefault(h.Invoiceno) : null;
            return new CreditNoteSummary(
                h.Id,
                h.Cnno ?? "—",
                LegacyValue.Date(h.Cndate) ?? DateOnly.MinValue,
                code is not null ? namesByCode.GetValueOrDefault(code) : null,
                h.Invoiceno ?? "—",
                LegacyValue.Money(h.Totamount),
                "legacy");
        }));

        return Ok(rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>
    /// One credit note in full — the read view. Serves both a <c>new</c> note and a <c>legacy</c> one adopted
    /// from the old system (its stored <c>varchar</c> figures, parsed defensively).
    /// </summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SearchCreditNote)]
    public async Task<ActionResult<CreditNoteDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var note = await _db.CreditNotes
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(
                c => c.Id == id && c.CompanyId != null && accessible.Contains(c.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        // Not one of this app's own — it may be a legacy credit note adopted into the same table.
        if (note is null)
        {
            return await LegacyCreditNoteDetail(id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == note.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        var companyName = note.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var invoiceNumber = await _db.Invoices
            .Where(i => i.Id == note.InvoiceId)
            .Select(i => i.Number)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var kind = note.Lines.Any(l => l.ItemId is not null) ? "Item" : "Service";

        return Ok(new CreditNoteDetail(
            note.Id,
            note.Number,
            note.Date,
            companyName,
            kind,
            customer?.Name,
            customer?.Code,
            note.InvoiceId,
            invoiceNumber ?? "—",
            note.ReturnsStock,
            note.Subtotal,
            note.DiscountAmount,
            note.NetTotal,
            note.TaxRatePercentage,
            note.TaxAmount,
            note.Total,
            "new",
            note.RowVersion,
            [.. note.Lines.Select(l => new InvoiceLineDetail(
                null, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>
    /// The read view for a legacy credit note — the same shape, built from the old system's <c>varchar</c>
    /// columns, parsed defensively (<see cref="LegacyValue"/>). Its figures are reconstructed as stored: the
    /// tax is total less net. Lines come from <c>cn_l</c> by the legacy number.
    /// </summary>
    private async Task<ActionResult<CreditNoteDetail>> LegacyCreditNoteDetail(
        long id,
        List<long> accessible,
        CancellationToken cancellationToken)
    {
        var h = await _legacy.CnHs
            .FirstOrDefaultAsync(x => x.Id == id && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h is null || h.CompanyId is null || !accessible.Contains(h.CompanyId.Value))
        {
            return NotFound();
        }

        var lines = await _legacy.CnLs
            .Where(l => l.Cnno == h.Cnno)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var companyName = await _db.Companies
            .Where(c => c.Id == h.CompanyId.Value)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The customer is not on cn_h; resolve it through the parent invoice's legacy customer code.
        var customerCode = h.Invoiceno is null
            ? null
            : await _legacy.InvoiceHs.Where(i => i.Invoiceno == h.Invoiceno).Select(i => i.Customer).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var customer = customerCode is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == customerCode, cancellationToken).ConfigureAwait(false);

        var net = LegacyValue.Money(h.Novattotal);
        var total = LegacyValue.Money(h.Totamount);
        var kind = lines.Any(l => !string.IsNullOrWhiteSpace(l.Itemcode)) ? "Item" : "Service";

        // cn_h and credit_notes are the same row; the legacy entity simply does not map row_version. Read
        // it from the typed side so a legacy note can be voided under the same concurrency guard.
        var rowVersion = await _db.CreditNotes
            .IgnoreQueryFilters()
            .Where(c => c.Id == h.Id)
            .Select(c => c.RowVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(new CreditNoteDetail(
            h.Id,
            h.Cnno ?? "—",
            LegacyValue.Date(h.Cndate) ?? DateOnly.MinValue,
            companyName,
            kind,
            customer?.Name ?? customerCode,
            customer?.Code ?? customerCode,
            null, // a legacy note links to its invoice by number, not the new surrogate id
            h.Invoiceno ?? "—",
            string.Equals(h.Stockposting, "1", StringComparison.Ordinal),
            net,
            0m, // a legacy credit note carries no discount
            net,
            LegacyValue.Money(h.Vper),
            total - net, // tax = grand total less the pre-VAT net
            total,
            "legacy",
            rowVersion,
            [.. lines.Select(l => new InvoiceLineDetail(
                null,
                null,
                l.Itemcode,
                l.Desc,
                LegacyValue.Money(l.Qty),
                LegacyValue.Money(l.Rate),
                0m,
                LegacyValue.Money(l.Tot),
                LegacyValue.Money(l.Tot),
                null))]));
    }

    /// <summary>Raise a credit note against a parent invoice — the whole document, posted once.</summary>
    [HttpPost]
    [RequirePermission(Permissions.NewCreditNote)]
    public async Task<ActionResult<CreditNoteCreatedResponse>> Create(
        CreateCreditNoteRequest request,
        CancellationToken cancellationToken)
    {
        var parent = await ResolveParentInvoiceAsync(request.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (parent is null)
        {
            return NotFound();
        }

        // Deny by default extends to the company: a caller may only raise a note in a company their token
        // grants, never against an invoice they cannot see.
        if (!_company.Accessible.Contains(parent.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise a credit note in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewCreditNote(
                parent.CompanyId,
                parent.CustomerId,
                parent.InvoiceId,
                parent.InvoiceNumber,
                request.Date,
                request.ReturnsStock,
                parent.TaxRateId,
                parent.TaxRatePercentage,
                [.. request.Lines.Select(l => new NewCreditNoteLine(
                    l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))]),
            cancellationToken).ConfigureAwait(false);

        return Ok(new CreditNoteCreatedResponse(created.Id, created.Number, created.Total));
    }

    /// <summary>What a credit note needs from its parent invoice — resolved from a new or a legacy one.</summary>
    private sealed record ParentInvoice(
        long InvoiceId,
        long CompanyId,
        long CustomerId,
        string InvoiceNumber,
        long? TaxRateId,
        decimal TaxRatePercentage);

    /// <summary>
    /// Resolves the parent invoice — new or legacy — to the customer, company, number and inherited VAT rate
    /// a credit note is built from. Returns null if the invoice does not exist, or a legacy invoice whose
    /// customer code is not in the customer master (it cannot be credited against a customer the ledger
    /// cannot key).
    /// </summary>
    private async Task<ParentInvoice?> ResolveParentInvoiceAsync(long invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is not null)
        {
            if (invoice.CompanyId is not { } companyId)
            {
                return null;
            }

            return new ParentInvoice(
                invoice.Id, companyId, invoice.CustomerId, invoice.Number,
                invoice.TaxRateId, invoice.TaxRatePercentage);
        }

        // A legacy invoice adopted into invoice_h: its company is a bigint (backfilled), its customer a code,
        // and its VAT percentage the stored `vper` (no tax_rate_id — a legacy row has none).
        var h = await _legacy.InvoiceHs
            .FirstOrDefaultAsync(x => x.Id == invoiceId && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h?.Invoiceno is null
            || !long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyCompanyId))
        {
            return null;
        }

        var customer = h.Customer is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return null;
        }

        return new ParentInvoice(
            h.Id, legacyCompanyId, customer.Id, h.Invoiceno,
            TaxRateId: null, LegacyValue.Money(h.Vper));
    }
}

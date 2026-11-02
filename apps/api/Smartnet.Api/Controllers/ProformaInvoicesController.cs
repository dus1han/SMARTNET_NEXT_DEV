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

namespace Smartnet.Api.Controllers;

/// <summary>
/// Proforma invoices — the step between a quotation and an invoice. See <see cref="IProformaInvoiceService"/>
/// for the rules.
/// </summary>
/// <remarks>
/// <b>Permissions follow the documents either side of it</b>, so no role needs changing: reading and printing
/// take the quotation-search right, raising and voiding take the quotation right (a proforma is the quotation
/// carried forward), and converting takes the invoice right — converting <i>is</i> raising an invoice.
/// </remarks>
[ApiController]
[Route("api/proforma-invoices")]
public sealed class ProformaInvoicesController : ControllerBase
{
    private readonly IProformaInvoiceService _proformas;
    private readonly IProformaRenderer _pdf;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly DocumentMailer _mailer;

    public ProformaInvoicesController(
        IProformaInvoiceService proformas,
        IProformaRenderer pdf,
        ICompanyContext company,
        SmartnetDbContext db,
        IAuditWriter audit,
        DocumentMailer mailer)
    {
        _proformas = proformas;
        _pdf = pdf;
        _company = company;
        _db = db;
        _audit = audit;
        _mailer = mailer;
    }

    /// <summary>The proformas the caller may see, newest first — the live ones, or with <paramref name="voided"/> the voided ones.</summary>
    [HttpGet]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<ActionResult<PagedResult<ProformaSummary>>> List(
        [FromQuery] PageRequest paging,
        [FromQuery] bool voided,
        CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // Read past the soft-delete filter, then choose the half asked for — voided is a view, not a gap.
        var query = _db.ProformaInvoices
            .IgnoreQueryFilters()
            .Where(p => accessible.Contains(p.CompanyId))
            .Where(p => voided ? p.DeletedAt != null : p.DeletedAt == null);

        if (paging.LikePattern is { } pattern)
        {
            var matchedCustomers = await _db.Customers
                .Where(c => EF.Functions.Like(c.Name, pattern))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            query = query.Where(p =>
                EF.Functions.Like(p.Number, pattern)
                || (p.QuotationNumber != null && EF.Functions.Like(p.QuotationNumber, pattern))
                || matchedCustomers.Contains(p.CustomerId));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var page = await query
            .OrderByDescending(p => p.Date)
            .ThenByDescending(p => p.Id)
            .Skip(paging.Skip)
            .Take(paging.SafePageSize)
            .Select(p => new { p.Id, p.Number, p.Date, p.QuotationId, p.QuotationNumber, p.CustomerId, p.Total, p.ConvertedToInvoiceId, p.DeletedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = page.Select(p => p.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = page.Select(p => new ProformaSummary(
            p.Id,
            p.Number,
            p.Date,
            p.QuotationId,
            p.QuotationNumber,
            names.GetValueOrDefault(p.CustomerId),
            p.Total,
            p.ConvertedToInvoiceId,
            Status(p.DeletedAt, p.ConvertedToInvoiceId))).ToList();

        return Ok(new PagedResult<ProformaSummary>(rows, total, paging.SafePage, paging.SafePageSize));
    }

    /// <summary>One proforma in full — voided ones too, so the record stays readable.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<ActionResult<ProformaDetail>> Get(long id, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, cancellationToken).ConfigureAwait(false) is not { } p)
        {
            return NotFound();
        }

        var customer = await _db.Customers
            .Where(c => c.Id == p.CustomerId)
            .Select(c => new { c.Name, c.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var companyName = await _db.Companies
            .Where(c => c.Id == p.CompanyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoiceNumber = p.ConvertedToInvoiceId is { } invoiceId
            ? await _db.Invoices.Where(i => i.Id == invoiceId).Select(i => i.Number).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var lines = p.Lines.Where(l => l.DeletedAt == null).OrderBy(l => l.Id).ToList();

        return Ok(new ProformaDetail(
            p.Id,
            p.Number,
            p.Date,
            companyName,
            lines.Any(l => l.ItemId is not null) ? "Item" : "Service",
            customer?.Name,
            customer?.Code,
            p.ContactPerson,
            p.Validity,
            p.QuotationId,
            p.QuotationNumber,
            p.Subtotal,
            p.DiscountPercent,
            p.DiscountAmount,
            p.NetTotal,
            p.TaxRatePercentage,
            p.TaxAmount,
            p.Total,
            p.ConvertedToInvoiceId,
            invoiceNumber,
            Status(p.DeletedAt, p.ConvertedToInvoiceId),
            p.DeletedAt,
            p.RowVersion,
            [.. lines.Select(l => new InvoiceLineDetail(
                l.Id, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>Raise a proforma from a quotation. One live proforma per quotation.</summary>
    [HttpPost]
    [RequirePermission(Permissions.ItemQuotation)]
    public async Task<ActionResult<ProformaCreatedResponse>> Create(CreateProformaRequest request, CancellationToken cancellationToken)
    {
        // The quotation must be one the caller may see — legacy ones included, hence past the filter.
        var accessible = _company.Accessible.ToList();
        var quotationCompany = await _db.Quotations
            .IgnoreQueryFilters()
            .Where(q => q.Id == request.QuotationId && q.DeletedAt == null)
            .Select(q => q.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (quotationCompany is null || !accessible.Contains(quotationCompany.Value))
        {
            return NotFound();
        }

        try
        {
            var created = await _proformas
                .CreateFromQuotationAsync(request.QuotationId, request.Date, cancellationToken)
                .ConfigureAwait(false);

            return Ok(new ProformaCreatedResponse(created.Id, created.Number, created.Total));
        }
        catch (QuotationAlreadyConvertedException converted)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: converted.Message);
        }
        catch (QuotationHasProformaException hasProforma)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: hasProforma.Message);
        }
        catch (TaxRateNotResolvableException notInForce)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notInForce.Message);
        }
        catch (InvalidOperationException unreadable) when (unreadable.Message.Contains("cannot be converted", StringComparison.Ordinal)
                                                          || unreadable.Message.Contains("no lines", StringComparison.Ordinal))
        {
            // A legacy quotation whose customer has gone from the master, or with nothing on it.
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: unreadable.Message);
        }
    }

    /// <summary>Raise a proforma entered on its own — no quotation behind it.</summary>
    [HttpPost("standalone")]
    [RequirePermission(Permissions.ItemQuotation)]
    public async Task<ActionResult<ProformaCreatedResponse>> CreateStandalone(
        CreateStandaloneProformaRequest request,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise a proforma invoice in that company.");
        }

        try
        {
            var created = await _proformas.CreateAsync(
                new NewProforma(
                    request.CompanyId,
                    request.CustomerId,
                    request.Date,
                    request.ContactPerson,
                    request.Validity,
                    [.. request.Lines.Select(l => new NewInvoiceLine(
                        l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                    request.DocumentDiscountPercent),
                cancellationToken).ConfigureAwait(false);

            return Ok(new ProformaCreatedResponse(created.Id, created.Number, created.Total));
        }
        catch (TaxRateNotResolvableException notInForce)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notInForce.Message);
        }
    }

    /// <summary>
    /// Convert the proforma into an invoice — once, through the ordinary invoice pipeline. Takes the invoice
    /// right: converting <i>is</i> raising an invoice, and the credit limit gates it as it gates any other.
    /// </summary>
    [HttpPost("{id:long}/convert")]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceCreatedResponse>> Convert(long id, ConvertQuotationRequest request, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, cancellationToken).ConfigureAwait(false) is not { DeletedAt: null })
        {
            return NotFound();
        }

        var type = request.Type == "Cash" ? InvoiceType.Cash : InvoiceType.Credit;

        try
        {
            var created = await _proformas.ConvertToInvoiceAsync(
                id,
                new ConvertQuotation(type, request.Date, request.PurchaseOrderNo, request.ContactPerson, request.DocumentCost),
                cancellationToken).ConfigureAwait(false);

            return Ok(new InvoiceCreatedResponse(created.Id, created.Number, created.Total, created.Outstanding));
        }
        catch (ServiceCostRequiredException missingCost)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: missingCost.Message);
        }
        catch (ProformaAlreadyConvertedException already)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: already.Message);
        }
        catch (CreditLimitExceededException over)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }
    }

    /// <summary>Void a proforma — soft, reason-gated. Frees its quotation. A converted one is refused.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.ItemQuotation)]
    [RequireChangeReason]
    public async Task<ActionResult<ProformaVoided>> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, cancellationToken).ConfigureAwait(false) is not { DeletedAt: null })
        {
            return NotFound();
        }

        try
        {
            return Ok(await _proformas.VoidAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false));
        }
        catch (ProformaAlreadyConvertedException converted)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{converted.Message} Void the invoice instead.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This proforma invoice was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>The proforma as a printable PDF.</summary>
    [HttpGet("{id:long}/pdf")]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, cancellationToken).ConfigureAwait(false) is not { } p)
        {
            return NotFound();
        }

        var pdf = await _pdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);
        if (pdf is null)
        {
            return NotFound();
        }

        await _audit.RecordAsync(
            AuditAction.Print,
            nameof(ProformaInvoice),
            id.ToString(CultureInfo.InvariantCulture),
            details: new { proformaNo = p.Number, document = "proforma" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(pdf, "application/pdf", $"proforma-{p.Number}.pdf");
    }

    /// <summary>Who this proforma can be emailed to, and the message that would go with it.</summary>
    [HttpGet("{id:long}/recipients")]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<ActionResult<QuotationRecipients>> Recipients(long id, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, cancellationToken).ConfigureAwait(false) is not { } p)
        {
            return NotFound();
        }

        var code = await CustomerCodeAsync(p.CustomerId, cancellationToken).ConfigureAwait(false);
        var contacts = await _mailer.ContactsByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        var (subject, body) = Message(p.Number, await CompanyNameAsync(p.CompanyId, cancellationToken).ConfigureAwait(false));

        return Ok(new QuotationRecipients(
            contacts,
            subject,
            body,
            $"proforma-{p.Number}.pdf",
            await _mailer.BlockedReasonAsync(p.CompanyId, contacts.Count, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Emails the proforma, as a PDF attachment, to the chosen saved contacts.</summary>
    [HttpPost("{id:long}/email")]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<ActionResult<EmailDocumentResponse>> Email(long id, EmailDocumentRequest request, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, cancellationToken).ConfigureAwait(false) is not { } p)
        {
            return NotFound();
        }

        // Re-resolved against this customer's own contacts, so a posted id cannot send it to anybody's.
        var code = await CustomerCodeAsync(p.CustomerId, cancellationToken).ConfigureAwait(false);
        var offered = await _mailer.ContactsByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        var chosen = offered.Where(c => request.ContactIds.Contains(c.Id)).ToList();

        if (chosen.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "None of the chosen contacts belong to this proforma's customer.",
            });
        }

        var pdf = await _pdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);
        if (pdf is null)
        {
            return NotFound();
        }

        var (subject, body) = Message(p.Number, await CompanyNameAsync(p.CompanyId, cancellationToken).ConfigureAwait(false));
        var recipients = chosen.Select(c => c.Email).ToList();

        var result = await _mailer.SendAsync(
            p.CompanyId,
            recipients,
            subject,
            body,
            [new MailAttachment($"proforma-{p.Number}.pdf", "application/pdf", pdf)],
            cancellationToken).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditAction.Email,
            nameof(ProformaInvoice),
            id.ToString(CultureInfo.InvariantCulture),
            details: new { proformaNo = p.Number, document = "proforma", to = recipients, sent = result.Sent, error = result.Error },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return Ok(new EmailDocumentResponse(result.Sent, recipients, result.Error));
    }

    /// <summary>The proforma — voided or not — if the caller's companies include its company; otherwise null.</summary>
    private async Task<ProformaInvoice?> VisibleAsync(long id, CancellationToken cancellationToken)
    {
        var p = await _db.ProformaInvoices
            .IgnoreQueryFilters()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return p is not null && _company.Accessible.Contains(p.CompanyId) ? p : null;
    }

    private static string Status(DateTime? deletedAt, long? convertedInvoiceId) =>
        deletedAt is not null ? "Voided" : convertedInvoiceId is not null ? "Invoiced" : "Open";

    private Task<string?> CustomerCodeAsync(long customerId, CancellationToken cancellationToken) =>
        _db.Customers.Where(c => c.Id == customerId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);

    private async Task<string> CompanyNameAsync(long companyId, CancellationToken cancellationToken) =>
        await _db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "SMARTNET";

    /// <summary>The covering message — fixed, as the quotation's is.</summary>
    private static (string Subject, string Body) Message(string number, string companyName) =>
        ($"Proforma invoice {number} — {companyName}",
         $"""
          <p>Dear Customer,</p>
          <p>Please find attached our proforma invoice <strong>{number}</strong>. Kindly quote this number with your payment.</p>
          <p>A tax invoice will be issued on payment. We would be happy to answer any questions.</p>
          <p>Thank you,<br />{companyName}</p>
          """);
}

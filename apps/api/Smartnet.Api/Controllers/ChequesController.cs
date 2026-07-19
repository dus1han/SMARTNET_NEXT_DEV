using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Persistence;

using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// The cheque register (Phase 7, slice 2) — a standalone record of cheques written.
/// </summary>
/// <remarks>
/// No ledger, no balance: the legacy app never tied a cheque to a payment, and neither does this. Adopted
/// additively — this app's cheques (typed columns) and the legacy ones (varchar) share the table. Printing is
/// Printing is Phase 8 — a cheque is recorded, listed, printed and voided here.
/// </remarks>
[ApiController]
[Route("api/cheques")]
public sealed class ChequesController : ControllerBase
{
    private readonly IChequeCreator _creator;
    private readonly IChequeVoider _voider;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly IChequeRenderer _chequePdf;
    private readonly IAuditWriter _audit;
    private readonly IChequePrintRecorder _printRecorder;

    public ChequesController(
        IChequeCreator creator,
        IChequeVoider voider,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        IChequeRenderer chequePdf,
        IAuditWriter audit,
        IChequePrintRecorder printRecorder)
    {
        _creator = creator;
        _voider = voider;
        _company = company;
        _db = db;
        _legacy = legacy;
        _chequePdf = chequePdf;
        _audit = audit;
        _printRecorder = printRecorder;
    }

    /// <summary>
    /// This cheque as a print-ready overlay for its stationery, and a print recorded against it.
    /// </summary>
    /// <remarks>
    /// <b>A cheque may be printed as many times as it needs to be.</b> Paper jams, misfeeds and spoiled
    /// stationery are ordinary, and a system that prints once and then refuses just means somebody writes
    /// the second one by hand. What matters is not preventing the reprint but recording it, so a cheque
    /// printed four times is visibly a cheque printed four times.
    ///
    /// <para>The legacy app took the opposite approach without meaning to: it overwrote
    /// <c>printeddt</c> on every print, so the reprints happened anyway and only the last one survived.
    /// Here each print writes an <see cref="AuditAction.Print"/> entry, which gives the full log for free
    /// in the History panel and a count for the register; <see cref="Cheque.PrintedAt"/> and its legacy
    /// <c>printeddt</c> shadow keep holding the most recent print, so the old report still reads.</para>
    /// </remarks>
    [HttpGet("{id:long}/pdf")]
    [RequirePermission(Permissions.Cheques)]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        var cheque = await VisibleChequeAsync(id, cancellationToken).ConfigureAwait(false);

        if (cheque is null)
        {
            return NotFound();
        }

        var pdf = await _chequePdf.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        await _printRecorder.RecordPrintAsync(id, cancellationToken).ConfigureAwait(false);

        // The count *after* this print, so the audit detail reads as "this was the third one".
        var printNumber = await PrintCountAsync(id, cancellationToken).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditAction.Print,
            nameof(Cheque),
            id.ToString(CultureInfo.InvariantCulture),
            details: new
            {
                document = "cheque",
                payTo = cheque.PayTo,
                amount = cheque.Amount,
                chequeNo = cheque.ChequeNumber,
                printNumber = printNumber + 1,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var name = string.IsNullOrWhiteSpace(cheque.ChequeNumber) ? id.ToString(CultureInfo.InvariantCulture) : cheque.ChequeNumber.Trim();

        return File(pdf, "application/pdf", $"cheque-{name}.pdf");
    }

    /// <summary>How many times this cheque has been printed, counted off the audit trail.</summary>
    private async Task<int> PrintCountAsync(long id, CancellationToken cancellationToken) =>
        await _db.AuditLog
            .Where(e => e.EntityType == nameof(Cheque)
                     && e.EntityId == id.ToString(CultureInfo.InvariantCulture)
                     && e.Action == AuditAction.Print)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Print counts for a set of cheques, in one query.
    /// </summary>
    /// <remarks>
    /// Counted from <c>audit_log</c> rather than kept as a column on the cheque. The log is already
    /// written on every print and is already the thing History shows, so a stored counter would be a
    /// second copy of the same fact — and the one that could disagree.
    /// </remarks>
    private async Task<Dictionary<string, int>> PrintCountsAsync(
        List<string> chequeIds,
        CancellationToken cancellationToken) =>
        chequeIds.Count == 0
            ? []
            : await _db.AuditLog
                .Where(e => e.EntityType == nameof(Cheque)
                         && e.Action == AuditAction.Print
                         && chequeIds.Contains(e.EntityId))
                .GroupBy(e => e.EntityId)
                .Select(g => new { EntityId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EntityId, x => x.Count, cancellationToken)
                .ConfigureAwait(false);

    /// <summary>
    /// The cheque the caller may see, of either origin.
    /// </summary>
    /// <remarks>
    /// IgnoreQueryFilters for the reason the renderer gives: <see cref="Cheque"/> is filtered to
    /// <c>data_origin == "new"</c> and every cheque in the system is legacy, so the unfiltered set is
    /// empty and this returned null for all of them — a 404 on the print button before a document was
    /// ever composed. Soft deletes are excluded explicitly, since ignoring the filter drops that too.
    /// </remarks>
    private async Task<Cheque?> VisibleChequeAsync(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        return await _db.Cheques
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.Id == id
                     && c.DeletedAt == null
                     && c.CompanyId != null
                     && accessible.Contains(c.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Every cheque the caller may see, newest first — this app's own and the legacy ones.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Cheques)]
    public async Task<ActionResult<IReadOnlyList<ChequeSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var companyNames = await _db.Companies
            .Where(c => accessible.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var newCheques = await _db.Cheques
            .Where(c => c.CompanyId != null && accessible.Contains(c.CompanyId.Value))
            .Select(c => new { c.Id, c.ChequeDate, c.DueDate, c.PayTo, c.Bank, c.ChequeNumber, c.Amount, c.CompanyId, c.SourceType, c.PrintedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var legacyCheques = (await _legacy.Cheques
            .Where(c => c.DataOrigin != "new")
            .Select(c => new { c.Id, c.Chequedate, c.Duedate, c.Payto, c.Bank, c.Chkno, c.Amount, c.Company, c.Printeddt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Where(c => c.Company != null && accessibleText.Contains(c.Company))
            .ToList();

        // One count query for the whole page, over both origins — they share the id space and the
        // audit trail does not distinguish them.
        var counts = await PrintCountsAsync(
            newCheques.Select(c => c.Id)
                .Concat(legacyCheques.Select(c => (long)c.Id))
                .Select(chequeId => chequeId.ToString(CultureInfo.InvariantCulture))
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        int CountFor(long id) => counts.GetValueOrDefault(id.ToString(CultureInfo.InvariantCulture));

        var rows = newCheques
            .Select(c => new ChequeSummary(
                c.Id, c.ChequeDate, c.DueDate, c.PayTo, c.Bank, c.ChequeNumber, c.Amount,
                c.CompanyId is { } cid ? companyNames.GetValueOrDefault(cid) : null, SourceLabel(c.SourceType), "new",
                CountFor(c.Id), c.PrintedAt))
            .ToList();

        rows.AddRange(legacyCheques.Select(c => new ChequeSummary(
            c.Id, LegacyValue.Date(c.Chequedate), LegacyValue.Date(c.Duedate), c.Payto, c.Bank, c.Chkno,
            LegacyValue.Money(c.Amount),
            long.TryParse(c.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lc) ? companyNames.GetValueOrDefault(lc) : null,
            SourceLabel(null), "legacy",
            // A legacy cheque's prints predate the audit trail, so the count starts at zero. Its
            // printeddt still shows *when* it was last printed under the old system.
            CountFor(c.Id), ParseLegacyPrintedAt(c.Printeddt))));

        return Ok(rows
            .OrderByDescending(r => r.ChequeDate ?? DateOnly.MinValue)
            .ThenByDescending(r => r.Id)
            .ToList());
    }

    /// <summary>One cheque in full — this app's own or a legacy one.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Cheques)]
    public async Task<ActionResult<ChequeDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var cheque = await _db.Cheques
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId != null && accessible.Contains(c.CompanyId.Value), cancellationToken)
            .ConfigureAwait(false);

        if (cheque is not null)
        {
            var companyName = cheque.CompanyId is { } cid
                ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;
            var supplierName = cheque.SupplierId is { } sid
                ? await _db.Suppliers.Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;

            return Ok(new ChequeDetail(
                cheque.Id, cheque.ChequeDate, cheque.DueDate, cheque.PayTo, cheque.EntryType, supplierName, cheque.SupplierCode,
                cheque.Bank, cheque.ChequeNumber, cheque.Amount, companyName, SourceLabel(cheque.SourceType), cheque.RowVersion, "new",
                await PrintCountAsync(id, cancellationToken).ConfigureAwait(false), cheque.PrintedAt));
        }

        return await LegacyChequeDetail(id, accessible, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A legacy <c>printeddt</c>, parsed defensively — the same posture as every other legacy value.
    /// </summary>
    private static DateTime? ParseLegacyPrintedAt(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>A friendly label for where a cheque came from.</summary>
    private static string SourceLabel(string? sourceType) => sourceType switch
    {
        ChequeSource.SupplierPayment => "Supplier payment",
        ChequeSource.Expense => "Expense",
        _ => "Manual",
    };

    private async Task<ActionResult<ChequeDetail>> LegacyChequeDetail(long id, List<long> accessible, CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var c = await _legacy.Cheques
            .Where(x => x.Id == (int)id && x.DataOrigin != "new")
            .Select(x => new { x.Id, x.Chequedate, x.Duedate, x.Payto, x.Entry, x.Supcode, x.Bank, x.Chkno, x.Amount, x.Company, x.Printeddt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (c is null || c.Company is null || !accessibleText.Contains(c.Company))
        {
            return NotFound();
        }

        var companyName = long.TryParse(c.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
            ? await _db.Companies.Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var supplierName = string.IsNullOrEmpty(c.Supcode)
            ? null
            : await _db.Suppliers.Where(s => s.Code == c.Supcode).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ChequeDetail(
            c.Id, LegacyValue.Date(c.Chequedate), LegacyValue.Date(c.Duedate), c.Payto, c.Entry, supplierName,
            string.IsNullOrEmpty(c.Supcode) ? null : c.Supcode, c.Bank, c.Chkno, LegacyValue.Money(c.Amount),
            companyName, SourceLabel(null), 0, "legacy",
            await PrintCountAsync(id, cancellationToken).ConfigureAwait(false), ParseLegacyPrintedAt(c.Printeddt)));
    }

    /// <summary>Record a cheque — a standalone written record; dual-writes the legacy row for the ChequeReport.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Cheques)]
    public async Task<ActionResult<ChequeCreatedResponse>> Create(CreateChequeRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record a cheque in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewCheque(
                request.CompanyId, request.EntryType, request.PayTo, request.SupplierId,
                request.Bank, request.ChequeNumber, request.Amount, request.ChequeDate, request.DueDate),
            cancellationToken).ConfigureAwait(false);

        return Ok(new ChequeCreatedResponse(created.Id, created.Amount));
    }

    /// <summary>Void a cheque — soft, reason-gated (not the legacy hard delete).</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Cheques)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.Cheques
            .IgnoreQueryFilters()
            .Where(c => c.Id == id && c.DeletedAt == null)
            .Select(c => c.CompanyId)
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
                title: "This cheque was changed by someone else. Reload and try again.");
        }
    }
}

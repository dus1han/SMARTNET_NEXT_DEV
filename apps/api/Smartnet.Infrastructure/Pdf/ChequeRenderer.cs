using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// Resolves a cheque and renders the overlay for its pre-printed stationery.
/// </summary>
/// <remarks>
/// Reads the typed columns, unlike the document renderers: <c>cheques</c> was adopted in Phase 7 with the
/// typed columns as the source of truth and the legacy <c>varchar</c> ones dual-written beside them, so
/// there is no unadopted-row problem to work around here.
/// </remarks>
public sealed class ChequeRenderer : IChequeRenderer
{
    static ChequeRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;

    public ChequeRenderer(SmartnetDbContext db) => _db = db;

    public async Task<byte[]?> RenderAsync(long chequeId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(chequeId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    public async Task<ChequeDocument?> BuildAsync(long chequeId, CancellationToken cancellationToken = default)
    {
        var cheque = await _db.Cheques
            .Where(c => c.Id == chequeId)
            .Select(c => new { c.PayTo, c.Amount, c.ChequeDate })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return cheque is null
            ? null
            : new ChequeDocument(new ChequeModel(
                PayTo: cheque.PayTo.Trim().ToUpperInvariant(),
                Amount: cheque.Amount,
                DateDigits: DateDigits(cheque.ChequeDate)));
    }

    /// <summary>
    /// The six digits the date boxes take: day, month, then the last two of the year.
    /// </summary>
    /// <remarks>
    /// The sample cheque prints <c>0 5 0 7</c> and then, after a wider gap, <c>2 6</c> — 5 July 2026 with
    /// only the last two year digits, the leading "20" boxes left empty. That is what the stationery's
    /// boxes expect, so it is reproduced: the gap is in the box positions, not in the data.
    /// </remarks>
    private static string DateDigits(DateOnly? date) =>
        date is { } d ? d.ToString("ddMMyy", CultureInfo.InvariantCulture) : string.Empty;
}

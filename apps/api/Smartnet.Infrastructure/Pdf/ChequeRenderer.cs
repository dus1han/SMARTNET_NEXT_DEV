using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// Resolves a cheque and renders the overlay for its pre-printed stationery.
/// </summary>
/// <remarks>
/// <b>Reads the legacy columns when the typed ones are empty</b>, exactly as the document renderers do.
/// This file previously claimed the opposite — that <c>cheques</c> was adopted in Phase 7 and so had no
/// unadopted-row problem. It was not true of the rows that exist: both carry <c>data_origin='legacy'</c>
/// with <c>cheque_amount</c> at zero and <c>cheque_date</c> null, while the real figures sit in the
/// varchar <c>amount</c> and <c>chequedate</c> beside them. A cheque printed from those typed columns
/// came out with no date and an amount of nought — in words as well as figures.
///
/// <para><c>payto</c> is a shared column and maps straight onto the entity, so it would have printed
/// where the others did not — but the row never arrived at all: <see cref="Cheque"/> carries a query
/// filter of <c>data_origin == "new"</c>, and both cheques are legacy, so the lookup found nothing and
/// the print produced no document. Hence the explicit IgnoreQueryFilters below.</para>
/// </remarks>
public sealed class ChequeRenderer : IChequeRenderer
{
    static ChequeRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public ChequeRenderer(SmartnetDbContext db, SmartnetLegacyDbContext legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<byte[]?> RenderAsync(long chequeId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(chequeId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    public async Task<ChequeDocument?> BuildAsync(long chequeId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters, because the Cheque entity is filtered to data_origin = "new" and every
        // cheque in the system is legacy — the filter exists so the new app's own list does not
        // double-count rows it reads from the legacy side, but printing has to reach either origin.
        // Soft deletes are still excluded, by hand, since ignoring the filter drops that too.
        var cheque = await _db.Cheques
            .IgnoreQueryFilters()
            .Where(c => c.Id == chequeId && c.DeletedAt == null)
            .Select(c => new { c.PayTo, c.Amount, c.ChequeDate })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (cheque is null)
        {
            return null;
        }

        // The legacy row beside it, for the figures adoption has not yet moved across.
        var legacy = await _legacy.Cheques
            .Where(c => c.Id == (int)chequeId)
            .Select(c => new { c.Amount, c.Chequedate })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var amount = cheque.Amount > 0m ? cheque.Amount : LegacyValue.Money(legacy?.Amount);
        var date = cheque.ChequeDate ?? LegacyValue.Date(legacy?.Chequedate);

        // A cheque with no amount is not a cheque. Better a button that reports nothing to print than a
        // sheet of stationery spoiled with "ZERO ONLY" across the words line.
        if (amount <= 0m)
        {
            return null;
        }

        return new ChequeDocument(new ChequeModel(
            PayTo: (cheque.PayTo ?? string.Empty).Trim().ToUpperInvariant(),
            Amount: amount,
            DateDigits: DateDigits(date)));
    }

    /// <summary>
    /// The six digits the date boxes take: day, month, then the last two of the year.
    /// </summary>
    /// <remarks>
    /// The sample cheque prints <c>0 5 0 7</c> and then, after a wider gap, <c>2 6</c> — 5 July 2026 with
    /// only the last two year digits, the leading "20" boxes left empty. That is what the stationery's
    /// boxes expect, so it is reproduced: the gap is in the box positions, not in the data. Matches
    /// <c>ChequeController.printcheque</c>, which splits dd, MM and yy into six separate fields.
    /// </remarks>
    private static string DateDigits(DateOnly? date) =>
        date is { } d ? d.ToString("ddMMyy", CultureInfo.InvariantCulture) : string.Empty;
}

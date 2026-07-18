using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// Resolves a quotation, its customer and its company, and renders the document.
/// </summary>
/// <remarks>
/// <b>Read from the legacy columns, deliberately.</b> <c>Quotation</c> and <c>QuotationH</c> map the same
/// <c>quotation_h</c> row — the typed <c>decimal</c>/<c>date</c> columns were added beside the original
/// <c>varchar</c> ones, and every save dual-writes both. But a legacy quotation is only <i>adopted</i>
/// into the typed columns on first edit, so for the 2,119 quotations in the dev database those columns
/// are still empty while the varchars hold the real figures. Reading the varchars therefore serves both:
/// an adopted quotation has them dual-written, an untouched legacy one has only them.
///
/// <para>The same applies to the lines. <c>quotation_l</c> rows carry the new <c>quotation_id</c> only
/// once adopted; before that they are linked by <c>qno</c> alone, which is why they are fetched by
/// number rather than through the navigation property.</para>
///
/// <para>Money is parsed from those varchars into <c>decimal</c> and formatted at render time. The legacy
/// report received pre-formatted strings, which froze every rounding decision upstream and out of sight.</para>
/// </remarks>
public sealed class QuotationRenderer : IQuotationRenderer
{
    static QuotationRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public QuotationRenderer(SmartnetDbContext db, SmartnetLegacyDbContext legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<byte[]?> RenderAsync(long quotationId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(quotationId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    /// <summary>
    /// The composed document rather than its bytes — what the drafting preview tool streams to the
    /// QuestPDF Companion. Kept on the concrete class, not the interface: the application only ever
    /// wants a PDF, and the tool exists so the template is iterated against the real resolution logic
    /// rather than a second copy of it that drifts.
    /// </summary>
    public async Task<QuotationDocument?> BuildAsync(long quotationId, CancellationToken cancellationToken = default)
    {
        var header = await _legacy.QuotationHs
            .FirstOrDefaultAsync(q => q.Id == quotationId, cancellationToken)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var number = Trim(header.QNo);

        // Lines by the real key when the quotation has been adopted, by number only when it has not.
        //
        // The fallback is unavoidable — an unadopted quotation_l row carries no quotation_id — but it is
        // not safe on its own: the dev data contains two headers both numbered "0", and a bare
        // number match hands each of them the other's lines. Adopted rows are therefore matched
        // exactly, and the number fallback is used only where nothing better exists.
        var adoptedLines = await _db.QuotationLines
            .IgnoreQueryFilters()
            .Where(l => l.QuotationId == quotationId && l.DeletedAt == null)
            .OrderBy(l => l.Id)
            .Select(l => new LineValues(l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.Net, null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = adoptedLines.Count > 0
            ? adoptedLines
            : await _legacy.QuotationLs
                .Where(l => l.Qno == number)
                .Select(l => new LineValues(
                    l.Itemcode,
                    l.Desc,
                    LegacyValue.Money(l.Qty),
                    LegacyValue.Money(l.Rate),
                    LegacyValue.Money(l.Total),
                    l.Itemno))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        long.TryParse(header.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        var logo = await _db.CompanyLogos
            .Where(l => l.CompanyId == companyId)
            .Select(l => l.Data)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The legacy `customer` column holds the customer *code*, not a key.
        var customerCode = Trim(header.Customer);
        var customer = customerCode.Length == 0
            ? null
            : await _db.Customers
                .Where(c => c.Code == customerCode)
                .Select(c => new { c.Name, c.Address })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var items = lines
            // `itemno` is the legacy line order, as text. Ordered numerically where it parses so line 10
            // does not sort between 1 and 2, and left alone where it does not. Adopted lines arrive
            // already ordered by key and carry no itemno, so they sort stably.
            .OrderBy(l => int.TryParse(l.Itemno, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .Select(l => new QuotationItem(Trim(l.ItemCode), Trim(l.Description), l.Quantity, l.Rate, l.Total))
            .ToList();

        var subtotal = LegacyValue.Money(header.Beforedisctot);
        var netTotal = LegacyValue.Money(header.Novattotal);
        var total = LegacyValue.Money(header.Totamount);
        var discountPercent = LegacyValue.Money(header.Discountper);
        var vatRate = LegacyValue.Money(header.Vper);

        // No VAT rows unless the company is registered — the whole reason the legacy pack carried a
        // separate _ST file. The amount is derived (total less the taxable figure) rather than stored:
        // quotation_h has no vat column of its own.
        var vatRegistered = (company?.IsVatRegistered ?? false) && vatRate > 0m;
        var taxAmount = total - netTotal;

        var model = new QuotationModel(
            Logo: logo is { Length: > 0 } ? logo : null,
            CompanyName: Trim(company?.Name),
            CompanyContact: CompanyHeader.Build(company),
            // The company's own colour, from the one place that decides it — so this prints in the same
            // accent as that company's job sheet rather than whatever brand_colour happens to hold.
            AccentColour: CompanyTheme.AccentFor(companyId),
            QuotationNo: number,
            Date: FormatDate(header.Qdate),
            ClientName: Trim(customer?.Name),
            ClientAddress: Trim(customer?.Address),
            ContactPerson: Trim(header.Contactperson),
            PreparedBy: Trim(header.Preparedby),
            Validity: Trim(header.QValid),
            Items: items,
            Subtotal: subtotal,
            DiscountPercent: discountPercent,
            DiscountAmount: subtotal - netTotal,
            NetTotal: netTotal,
            TaxLabel: vatRegistered ? $"VAT ({Percentage(vatRate)}%)" : null,
            TaxAmount: vatRegistered ? taxAmount : null,
            Total: total,
            Bank: BuildBank(company));

        return new QuotationDocument(model);
    }

    /// <summary>The bank block, or null when the company has no account on file — nothing to print.</summary>
    private static BankDetails? BuildBank(Company? c) =>
        c is null || string.IsNullOrWhiteSpace(c.BankName)
            ? null
            : new BankDetails(c.BankName.Trim(), Trim(c.BankBranch), Trim(c.BankAccountName), Trim(c.BankAccountNumber));

    private static string FormatDate(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : Trim(raw);

    /// <summary>A rate without trailing zeros — "18", not "18.00".</summary>
    private static string Percentage(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    /// <summary>A line from either source, already reduced to the figures the document prints.</summary>
    private sealed record LineValues(
        string? ItemCode,
        string? Description,
        decimal Quantity,
        decimal Rate,
        decimal Total,
        string? Itemno);
}

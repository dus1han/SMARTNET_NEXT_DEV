using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// Resolves a credit note, its customer and its company, and renders the document.
/// </summary>
/// <remarks>
/// <b>Read from the legacy columns</b>, as the quotation and purchase-order renderers do: the typed
/// columns are only populated once a note is adopted, and the varchars are written either way.
///
/// <para><b>The customer comes through the invoice.</b> <c>cn_h</c> carries no customer of its own — a
/// credit note belongs to the invoice it credits, and that invoice names the customer. So the lookup is
/// two hops, and a note whose invoice has gone missing prints without a client rather than failing.</para>
/// </remarks>
public sealed class CreditNoteRenderer : ICreditNoteRenderer
{
    static CreditNoteRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public CreditNoteRenderer(SmartnetDbContext db, SmartnetLegacyDbContext legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<byte[]?> RenderAsync(long creditNoteId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(creditNoteId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    /// <summary>The composed document rather than its bytes — what the drafting preview tool streams.</summary>
    public async Task<CreditNoteDocument?> BuildAsync(long creditNoteId, CancellationToken cancellationToken = default)
    {
        var header = await _legacy.CnHs
            .FirstOrDefaultAsync(c => c.Id == creditNoteId, cancellationToken)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var number = Trim(header.Cnno);
        var invoiceNo = Trim(header.Invoiceno);

        var adoptedLines = await _db.CreditNoteLines
            .IgnoreQueryFilters()
            .Where(l => l.CreditNoteId == creditNoteId && l.DeletedAt == null)
            .OrderBy(l => l.Id)
            .Select(l => new LineValues(l.Description, l.Quantity, l.UnitPrice, l.Net, null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = adoptedLines.Count > 0
            ? adoptedLines
            : await _legacy.CnLs
                .Where(l => l.Cnno == number)
                .Select(l => new LineValues(
                    l.Desc,
                    LegacyValue.Money(l.Qty),
                    LegacyValue.Money(l.Rate),
                    LegacyValue.Money(l.Tot),
                    l.Itemno))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var companyId = header.CompanyId ?? 0;

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        var logo = await _db.CompanyLogos
            .Where(l => l.CompanyId == companyId)
            .Select(l => l.Data)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Two hops: the note names its invoice, and the invoice names both the customer and the person it
        // was addressed to. A credit note carries neither of its own.
        var parent = invoiceNo.Length == 0
            ? null
            : await _legacy.InvoiceHs
                .Where(i => i.Invoiceno == invoiceNo)
                .Select(i => new { i.Customer, i.Contactperson })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var customerCode = parent?.Customer;

        var customer = string.IsNullOrWhiteSpace(customerCode)
            ? null
            : await _db.Customers
                .Where(c => c.Code == customerCode)
                .Select(c => new { c.Name, c.Address, c.Phone })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var items = lines
            .OrderBy(l => l.Itemno ?? long.MaxValue)
            .Select(l => new CreditNoteItem(Trim(l.Description), l.Quantity, l.Rate, l.Total))
            .ToList();

        var netTotal = LegacyValue.Money(header.Novattotal);
        var total = LegacyValue.Money(header.Totamount);
        var vatRate = LegacyValue.Money(header.Vper);

        var vatRegistered = (company?.IsVatRegistered ?? false) && vatRate > 0m;

        var model = new CreditNoteModel(
            Logo: logo is { Length: > 0 } ? logo : null,
            CompanyName: Trim(company?.Name),
            CompanyContact: CompanyHeader.Build(company),
            AccentColour: CompanyTheme.AccentFor(companyId),
            CreditNoteNo: number,
            Date: FormatDate(header.Cndate),
            InvoiceNo: invoiceNo,
            ClientName: Trim(customer?.Name),
            ClientAddress: Trim(customer?.Address),
            ContactPerson: Trim(parent?.Contactperson),
            // Grouped by the same formatter the company header uses, so a number reads the same wherever
            // it appears on the document.
            ContactPhone: string.IsNullOrWhiteSpace(customer?.Phone)
                ? string.Empty
                : CompanyHeader.FormatPhone(customer.Phone.Trim()),
            PreparedBy: Trim(header.Preparedby),
            // The legacy flag is "1" when the credit put the goods back on the shelf.
            ReturnsStock: Trim(header.Stockposting) == "1",
            Items: items,
            Subtotal: netTotal,
            DiscountPercent: 0m,
            DiscountAmount: 0m,
            NetTotal: netTotal,
            TaxLabel: vatRegistered ? $"VAT ({Percentage(vatRate)}%)" : null,
            TaxAmount: vatRegistered ? total - netTotal : null,
            Total: total);

        return new CreditNoteDocument(model);
    }

    private static string FormatDate(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : Trim(raw);

    private static string Percentage(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    /// <summary>A line from either source, reduced to the figures the document prints.</summary>
    private sealed record LineValues(
        string? Description,
        decimal Quantity,
        decimal Rate,
        decimal Total,
        long? Itemno);
}

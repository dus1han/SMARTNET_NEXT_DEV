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
/// Resolves an invoice, its customer and its company, and renders the document.
/// </summary>
/// <remarks>
/// <b>Non-VAT invoices only</b> — the <c>Invoice_ST</c> replacement. A VAT-registered company returns
/// null, because this layout carries no VAT rows and no supplier/purchaser registration block: printing
/// it for Smart Net would hand the customer an invoice showing 209,450 with no VAT line and nothing to
/// reclaim against. Null surfaces as a 404 on a button that should not have been offered, which is a
/// visible gap; a silently VAT-less tax invoice is a wrong document, which is not.
///
/// <para><b>Read from the legacy columns, deliberately.</b> <c>Invoice</c> and <c>InvoiceH</c> map the
/// same <c>invoice_h</c> row — the typed <c>decimal</c>/<c>date</c> columns were added beside the
/// original <c>varchar</c> ones, and every save dual-writes both. But a legacy invoice is only
/// <i>adopted</i> into the typed columns on first edit, so for the 2,485 invoices in the dev database
/// those columns are still empty while the varchars hold the real figures. Reading the varchars
/// therefore serves both.</para>
///
/// <para>The same applies to the lines: <c>invoice_l</c> rows carry <c>invoice_id</c> only once adopted,
/// so they are fetched by number when it is absent. Unlike quotations there is no duplicate-number
/// hazard here — <c>invoice_h</c> has a unique index on <c>invoiceno</c> and zero duplicates — but the
/// adopted-key-first order is kept anyway, so the two renderers do not differ for a reason a reader has
/// to go and check.</para>
/// </remarks>
public sealed class InvoiceRenderer : IInvoiceRenderer
{
    static InvoiceRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public InvoiceRenderer(SmartnetDbContext db, SmartnetLegacyDbContext legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<byte[]?> RenderAsync(long invoiceId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(invoiceId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    /// <summary>
    /// The composed document rather than its bytes — what the drafting preview tool streams to the
    /// QuestPDF Companion, so the template is iterated against the real resolution logic rather than a
    /// second copy of it that drifts.
    /// </summary>
    public async Task<InvoiceDocument?> BuildAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        var header = await _legacy.InvoiceHs
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        long.TryParse(header.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        // The tax invoice is a different document, not a flag on this one. See the class remarks.
        if (company?.IsVatRegistered ?? false)
        {
            return null;
        }

        var number = Trim(header.Invoiceno);

        var adoptedLines = await _db.InvoiceLines
            .IgnoreQueryFilters()
            .Where(l => l.InvoiceId == invoiceId && l.DeletedAt == null)
            .OrderBy(l => l.Id)
            .Select(l => new LineValues(l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.Net, null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = adoptedLines.Count > 0
            ? adoptedLines
            : await _legacy.InvoiceLs
                .Where(l => l.Inno == number)
                .Select(l => new LineValues(
                    l.Itemcode,
                    l.Desc,
                    LegacyValue.Money(l.Qty),
                    LegacyValue.Money(l.Rate),
                    LegacyValue.Money(l.Tot),
                    l.Itemno))
                .ToListAsync(cancellationToken)
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
                .Select(c => new { c.Name, c.Address, c.Phone })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var items = lines
            // `itemno` is the legacy line order — a number here, unlike quotation_l where it is text.
            // Adopted lines arrive already ordered by key and carry no itemno, so they sort stably.
            .OrderBy(l => l.Itemno ?? long.MaxValue)
            .Select(l => new InvoiceItem(Trim(l.ItemCode), Trim(l.Description), l.Quantity, l.Rate, l.Total))
            .ToList();

        var subtotal = LegacyValue.Money(header.Beforedisctot);
        var netTotal = LegacyValue.Money(header.Novattotal);
        var total = LegacyValue.Money(header.Totamount);
        var balance = LegacyValue.Money(header.Balance);

        var model = new InvoiceModel(
            Logo: logo is { Length: > 0 } ? logo : null,
            CompanyName: Trim(company?.Name),
            CompanyContact: CompanyHeader.Build(company),
            // The company's own colour, from the one place that decides it — so this prints in the same
            // accent as that company's job sheet rather than whatever brand_colour happens to hold.
            AccentColour: CompanyTheme.AccentFor(companyId),
            InvoiceNo: number,
            Date: FormatDate(header.Indate),
            InvoiceType: Trim(header.Invtype),
            PoNumber: PoNumber(header.Pono),
            ClientName: Trim(customer?.Name),
            ClientAddress: Trim(customer?.Address),
            ContactPerson: WithPhone(header.Contactperson, customer?.Phone),
            PreparedBy: Trim(header.Preparedby),
            Items: items,
            Subtotal: subtotal,
            DiscountPercent: LegacyValue.Money(header.Discountper),
            DiscountAmount: subtotal - netTotal,
            NetTotal: netTotal,
            Total: total,
            // Derived, not stored: invoice_h keeps only the running balance, and `paid` was a report
            // parameter the legacy app computed the same way.
            Paid: total - balance,
            BalanceDue: balance,
            Bank: BuildBank(company));

        return new InvoiceDocument(model);
    }

    /// <summary>
    /// The customer's order number, or empty when they gave none.
    /// </summary>
    /// <remarks>
    /// <c>pono</c> is free text and no invoice leaves it blank — when there is no customer order the
    /// clerks type an X instead. 219 of the 2,485 invoices do (179 of them ST), spelled five different
    /// ways: <c>X</c>, <c>xx</c>, <c>XXX</c>, <c>XXXX</c> and <c>XXXXXxxxx</c>. Matching any run of Xs in
    /// either case covers all five, where matching the literal "XXX" would have caught barely half.
    ///
    /// <para>Printing "PO Number: XXX" on a document the customer reads is noise standing in for a blank,
    /// so the placeholder is treated as one and the row is omitted.</para>
    /// </remarks>
    private static string PoNumber(string? raw)
    {
        var value = Trim(raw);

        return value.All(c => c is 'X' or 'x') ? string.Empty : value;
    }

    /// <summary>Contact person as "Name (telephone)", the house convention across every document.</summary>
    private static string WithPhone(string? name, string? phone)
    {
        var person = Trim(name);
        var number = Trim(phone);

        if (number.Length == 0) return person;

        var formatted = CompanyHeader.FormatPhone(number);

        return person.Length == 0 ? formatted : $"{person} ({formatted})";
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

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    /// <summary>A line from either source, already reduced to the figures the document prints.</summary>
    private sealed record LineValues(
        string? ItemCode,
        string? Description,
        decimal Quantity,
        decimal Rate,
        decimal Total,
        long? Itemno);
}

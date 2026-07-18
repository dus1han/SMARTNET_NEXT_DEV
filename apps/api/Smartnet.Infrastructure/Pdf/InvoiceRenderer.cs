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
/// <b>Two documents, chosen by what the invoice actually charged.</b> An invoice with VAT on it, from a
/// registered company, gets the tax invoice (<see cref="TaxInvoiceDocument"/>, the
/// <c>Invoice_SN_TAX</c> replacement), which names both parties' registration numbers and the date of
/// supply because that is what the purchaser reclaims against. Everything else gets the plain one
/// (<see cref="InvoiceDocument"/>, replacing <c>Invoice_ST</c>).
///
/// <para>The legacy pack left this to whichever report a clerk picked, so the same company could issue
/// either. Keying it to the company's registration flag alone would only move that fault: Smart Net
/// registered part-way through its history, and its 664 earlier invoices would then reprint as tax
/// invoices charging no tax. The document decides.</para>
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
    /// second copy of it that drifts. Returns whichever of the two invoice documents this company's VAT
    /// registration calls for.
    /// </summary>
    public async Task<IDocument?> BuildAsync(long invoiceId, CancellationToken cancellationToken = default)
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
                .Select(c => new { c.Name, c.Address, c.Phone, c.VatNumber })
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
        var date = FormatDate(header.Indate);
        var accent = CompanyTheme.AccentFor(companyId);
        var logoBytes = logo is { Length: > 0 } ? logo : null;
        var bank = BuildBank(company);

        var vatRate = LegacyValue.Money(header.Vper);

        // The tax invoice follows the *invoice*, not the company. Smart Net registered for VAT on
        // 2024-09-02: its 664 earlier invoices charge no VAT and are not tax invoices, and reprinting one
        // today under a TAX INVOICE heading with a "VAT (0%)" line would restate a historical supply as
        // something it was not. Reading the rate off the document gets that right without needing a
        // registration date, and it is the same reason the legacy pack's clerk-picks-the-report approach
        // was wrong — the document decides, not whoever is printing it.
        if ((company?.IsVatRegistered ?? false) && vatRate > 0m)
        {
            return new TaxInvoiceDocument(new TaxInvoiceModel(
                Logo: logoBytes,
                CompanyName: Trim(company.Name),
                CompanyContact: CompanyHeader.Build(company),
                AccentColour: accent,
                InvoiceNo: number,
                Date: date,
                // The same day on every legacy invoice, because the old system had nowhere else to put
                // it. Kept as its own field: it decides the VAT period, and the invoice date does not.
                DateOfSupply: date,
                Supplier: new TaxParty(
                    Tin(company.VatNumber),
                    Trim(company.Name),
                    CompanyAddress(company),
                    CompanyHeader.FormatPhone(Trim(company.Phone))),
                Purchaser: new TaxParty(
                    Tin(customer?.VatNumber),
                    Trim(customer?.Name),
                    Trim(customer?.Address),
                    CompanyHeader.FormatPhone(Trim(customer?.Phone))),
                ContactPerson: Trim(header.Contactperson),
                PoNumber: PoNumber(header.Pono),
                PreparedBy: Trim(header.Preparedby),
                Items: items,
                Subtotal: subtotal,
                DiscountPercent: LegacyValue.Money(header.Discountper),
                DiscountAmount: subtotal - netTotal,
                NetTotal: netTotal,
                TaxLabel: $"VAT ({Percentage(vatRate)}%)",
                // Derived, not stored: invoice_h has no VAT column, so the tax is the grand total less
                // the pre-VAT net — the same subtraction the legacy report did.
                TaxAmount: total - netTotal,
                Total: total,
                Paid: total - balance,
                BalanceDue: balance,
                Bank: bank));
        }

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

    /// <summary>
    /// A registration number, or empty when the field holds a placeholder rather than one.
    /// </summary>
    /// <remarks>
    /// The same habit as <see cref="PoNumber"/>, in a field that matters more. 111 of the 223 customers
    /// have a <c>vatnum</c> with no digit in it at all — <c>-</c> for "not registered", or <c>XXXX</c> —
    /// and others carry a real number with a dash still in front of it, which is why the first tax invoice
    /// rendered showed a purchaser's TIN of "- 104046851-7000".
    ///
    /// <para>Anything without a digit is treated as absent and prints as "—", because a tax invoice
    /// claiming the purchaser's TIN is "-" is worse than one that plainly has not got it: the second is a
    /// gap somebody can fill, the first looks answered. Leading punctuation is stripped from the rest.</para>
    /// </remarks>
    private static string Tin(string? raw)
    {
        var value = Trim(raw);

        return value.Any(char.IsDigit) ? value.TrimStart('-', '.', '/', ' ').Trim() : string.Empty;
    }

    /// <summary>
    /// The company's address on one line — what the tax invoice's "Address" row prints.
    /// </summary>
    /// <remarks>
    /// The masthead sets the same parts over several lines; here they run together, because the party
    /// block is a grid of one-line values and a three-line address would push the two columns out of
    /// step with each other.
    /// </remarks>
    private static string CompanyAddress(Company c) =>
        string.Join(", ", new[] { c.AddressLine1, c.AddressLine2, c.City }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()));

    /// <summary>A rate without trailing zeros — "18", not "18.00".</summary>
    private static string Percentage(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

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

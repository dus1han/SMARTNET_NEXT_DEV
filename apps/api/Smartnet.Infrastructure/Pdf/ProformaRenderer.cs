using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>Resolves a proforma, its customer and its company, and renders the document.</summary>
/// <remarks>
/// Read from the proforma's own typed columns — it is a new-app document with no legacy shadow — and
/// voided ones included, so a voided proforma can still be reprinted for the record.
/// </remarks>
public sealed class ProformaRenderer : IProformaRenderer
{
    static ProformaRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;

    public ProformaRenderer(SmartnetDbContext db) => _db = db;

    public async Task<byte[]?> RenderAsync(long proformaId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(proformaId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    public async Task<ProformaDocument?> BuildAsync(long proformaId, CancellationToken cancellationToken = default)
    {
        var p = await _db.ProformaInvoices
            .IgnoreQueryFilters()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == proformaId, cancellationToken)
            .ConfigureAwait(false);

        if (p is null)
        {
            return null;
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == p.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        var logo = await _db.CompanyLogos
            .Where(l => l.CompanyId == p.CompanyId)
            .Select(l => l.Data)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var customer = await _db.Customers
            .Where(c => c.Id == p.CustomerId)
            .Select(c => new { c.Name, c.Address })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var preparedBy = p.PreparedBy is { } userId
            ? await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Name ?? u.Username)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var vatRegistered = (company?.IsVatRegistered ?? false) && p.TaxRatePercentage > 0m;

        return new ProformaDocument(new ProformaModel(
            Logo: logo is { Length: > 0 } ? logo : null,
            CompanyName: Trim(company?.Name),
            CompanyContact: CompanyHeader.Build(company),
            AccentColour: CompanyTheme.AccentFor(p.CompanyId),
            ProformaNo: p.Number,
            QuotationNo: p.QuotationNumber,
            Date: p.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            ClientName: Trim(customer?.Name),
            ClientAddress: Trim(customer?.Address),
            ContactPerson: Trim(p.ContactPerson),
            PreparedBy: Trim(preparedBy),
            Validity: Trim(p.Validity),
            Items: [.. p.Lines
                .Where(l => l.DeletedAt == null)
                .OrderBy(l => l.Id)
                .Select(l => new QuotationItem(Trim(l.ItemCode), Trim(l.Description), l.Quantity, l.UnitPrice, l.Net))],
            Subtotal: p.Subtotal,
            DiscountPercent: p.DiscountPercent,
            DiscountAmount: p.DiscountAmount,
            NetTotal: p.NetTotal,
            TaxLabel: vatRegistered ? $"VAT ({InvoiceRenderer.Percentage(p.TaxRatePercentage)}%)" : null,
            TaxAmount: vatRegistered ? p.TaxAmount : null,
            Total: p.Total,
            Bank: InvoiceRenderer.BuildBank(company)));
    }

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;
}

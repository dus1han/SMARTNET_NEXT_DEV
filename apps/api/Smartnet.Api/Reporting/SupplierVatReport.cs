using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Input VAT on supplier tax invoices — <c>supplier_invoice</c> where <c>vtype='1'</c>, VAT derived
/// <c>amount − novattotal</c>. The mirror of the customer VAT report, on the same spine.
/// </summary>
public static class SupplierVatReport
{
    public static SupplierVatResponse Build(
        IReadOnlyList<SupplierInvoice> invoices,
        IReadOnlyDictionary<string, (string Name, string? Vat)> suppliers,
        ReportPeriod period)
    {
        var rows = invoices
            .Where(i => string.Equals(i.Vtype, "1", StringComparison.Ordinal))
            .Where(i => period.ContainsIso(i.Invdate))
            .Select(i => Row(i, suppliers))
            .OrderBy(r => r.Date ?? DateOnly.MaxValue)
            .ThenBy(r => r.InvoiceNo, StringComparer.Ordinal)
            .ToList();

        return new SupplierVatResponse(
            TotalValue: rows.Sum(r => r.Value),
            TotalVat: rows.Sum(r => r.Vat),
            Count: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static SupplierVatRow Row(SupplierInvoice i, IReadOnlyDictionary<string, (string Name, string? Vat)> suppliers)
    {
        var value = LegacyValue.Money(i.Amount, out var valueOk);
        var net = LegacyValue.Money(i.Novattotal, out var netOk);

        var date = LegacyValue.Date(i.Invdate);
        var dateOk = string.IsNullOrWhiteSpace(i.Invdate) || date is not null;

        var party = i.Supcode is { Length: > 0 } code
            ? suppliers.GetValueOrDefault(code, (string.Empty, null))
            : (Name: string.Empty, Vat: (string?)null);

        return new SupplierVatRow(
            Date: date,
            InvoiceNo: i.Invno ?? string.Empty,
            SupplierName: party.Name,
            VatNumber: party.Vat,
            Value: value,
            Vat: value - net,
            HasDataIssue: !valueOk || !netOk || !dateOk);
    }
}

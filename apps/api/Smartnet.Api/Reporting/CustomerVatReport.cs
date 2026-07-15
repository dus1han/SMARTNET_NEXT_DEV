using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Output VAT on tax invoices — <c>invoice_h</c> where <c>vtype='1'</c>. VAT is derived
/// <c>totamount − novattotal</c>, as the legacy report does. The legacy export's <c>cvatcomp = to</c>
/// corrupt-company bug cannot occur here: the company comes from the caller's token and the dates from
/// the request, never round-tripped through a session slot.
/// </summary>
public static class CustomerVatReport
{
    public static CustomerVatResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyDictionary<string, (string Name, string? Vat)> customers,
        ReportPeriod period)
    {
        var rows = invoices
            .Where(h => string.Equals(h.Vtype, "1", StringComparison.Ordinal)) // tax invoices only
            .Where(h => period.ContainsIso(h.Indate))
            .Select(h => Row(h, customers))
            .OrderBy(r => r.Date ?? DateOnly.MaxValue) // legacy orders by indate for the filing sequence
            .ThenBy(r => r.InvoiceNo, StringComparer.Ordinal)
            .ToList();

        return new CustomerVatResponse(
            TotalValue: rows.Sum(r => r.Value),
            TotalVat: rows.Sum(r => r.Vat),
            Count: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static CustomerVatRow Row(InvoiceH h, IReadOnlyDictionary<string, (string Name, string? Vat)> customers)
    {
        var value = LegacyValue.Money(h.Totamount, out var valueOk);
        var net = LegacyValue.Money(h.Novattotal, out var netOk);

        var date = LegacyValue.Date(h.Indate);
        var dateOk = string.IsNullOrWhiteSpace(h.Indate) || date is not null;

        var party = h.Customer is { Length: > 0 } code
            ? customers.GetValueOrDefault(code, (string.Empty, null))
            : (Name: string.Empty, Vat: (string?)null);

        return new CustomerVatRow(
            Date: date,
            InvoiceNo: h.Invoiceno ?? string.Empty,
            CustomerName: party.Name,
            VatNumber: party.Vat,
            DocumentType: $"{h.It} Invoice".Trim(),
            Value: value,
            Vat: value - net,
            HasDataIssue: !valueOk || !netOk || !dateOk);
    }
}

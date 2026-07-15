using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Purchases per supplier for the period, with the pending balance. The legacy per-supplier correlated
/// subquery (<c>SUM(amount) WHERE paymentstat='Pending'</c>) becomes one grouped pass. "Pending" is a
/// whole-invoice flag — the legacy model has no partial payment — and is reported as such.
/// </summary>
public static class SupplierPurchaseReport
{
    public static SupplierPurchaseResponse Build(
        IReadOnlyList<SupplierInvoice> invoices,
        IReadOnlyDictionary<string, string> supplierNames,
        ReportPeriod period)
    {
        var rows = invoices
            .Where(i => period.ContainsIso(i.Invdate))
            .GroupBy(i => i.Supcode ?? string.Empty, StringComparer.Ordinal)
            .Select(g => Row(g.Key, g.ToList(), supplierNames))
            .OrderByDescending(r => r.PendingBalance)
            .ThenBy(r => r.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SupplierPurchaseResponse(
            TotalPurchase: rows.Sum(r => r.TotalPurchase),
            TotalPending: rows.Sum(r => r.PendingBalance),
            SupplierCount: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static SupplierPurchaseRow Row(
        string code,
        List<SupplierInvoice> invoices,
        IReadOnlyDictionary<string, string> names)
    {
        decimal total = 0m, pending = 0m;
        var issue = false;

        foreach (var i in invoices)
        {
            var amount = LegacyValue.Money(i.Amount, out var ok);
            issue |= !ok;
            total += amount;

            if (string.Equals(i.Paymentstat, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                pending += amount;
            }
        }

        return new SupplierPurchaseRow(
            SupplierCode: code,
            SupplierName: names.GetValueOrDefault(code, string.Empty),
            TotalPurchase: total,
            PendingBalance: pending,
            HasDataIssue: issue);
    }
}

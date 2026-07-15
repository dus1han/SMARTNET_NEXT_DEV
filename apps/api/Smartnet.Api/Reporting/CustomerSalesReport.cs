using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// The Sales report's mirror: the same <c>invoice_h</c> rows, grouped by customer and ranked by
/// profit. Another clone on the spine — a different grouping and column set, the same parser and window.
/// </summary>
public static class CustomerSalesReport
{
    public static CustomerSalesResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> customerNames,
        ReportPeriod period)
    {
        var rows = invoices
            .Where(h => period.ContainsIso(h.Indate))
            .GroupBy(h => h.Customer ?? string.Empty, StringComparer.Ordinal)
            .Select(g => Row(g.Key, g.ToList(), customerNames))
            .OrderByDescending(r => r.Profit)
            .ThenBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CustomerSalesResponse(
            TotalSales: rows.Sum(r => r.Total),
            TotalProfit: rows.Sum(r => r.Profit),
            CustomerCount: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static CustomerSalesRow Row(
        string code,
        List<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> names)
    {
        decimal total = 0m, cost = 0m, balance = 0m;
        var issue = false;

        foreach (var h in invoices)
        {
            total += LegacyValue.Money(h.Totamount, out var totalOk);
            cost += LegacyValue.Money(h.Cost, out var costOk);
            balance += LegacyValue.Money(h.Balance, out var balanceOk);

            issue |= !totalOk || !costOk || !balanceOk;
        }

        return new CustomerSalesRow(
            CustomerCode: code,
            CustomerName: names.GetValueOrDefault(code, string.Empty),
            InvoiceCount: invoices.Count,
            Total: total,
            Cost: cost,
            Profit: total - cost,
            Balance: balance,
            HasDataIssue: issue);
    }
}

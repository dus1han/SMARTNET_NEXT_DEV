using Smartnet.Api.Contracts;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// One customer's whole account — the pure half, so the drill-down is unit-testable like the dashboard.
/// </summary>
/// <remarks>
/// Deliberately built from the same legacy columns and the same defensive parse as
/// <see cref="DashboardAnalyticsBuilder"/>. The dashboard says a customer owes a figure and this page
/// has to show the invoices that make it up; if the two read the data differently they will eventually
/// disagree in front of somebody holding a phone, which is the one place a reporting difference cannot
/// be explained away.
/// </remarks>
public static class CustomerInsightBuilder
{
    private const int TrendMonths = 12;

    public static CustomerInsight Build(
        string code,
        string name,
        string? address,
        string? phone,
        string? vatNumber,
        decimal? creditLimit,
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<Payment> payments,
        DateOnly today)
    {
        var rows = invoices
            .Select(h => new
            {
                Number = (h.Invoiceno ?? string.Empty).Trim(),
                Date = LegacyValue.Date(h.Indate),
                Type = (h.Invtype ?? string.Empty).Trim(),
                Total = LegacyValue.Money(h.Totamount),
                Balance = LegacyValue.Money(h.Balance),
            })
            .ToList();

        var dated = rows.Where(r => r.Date is not null).ToList();

        var lastPaymentFor = payments
            .Where(p => !string.IsNullOrEmpty(p.Invoiceno))
            .Select(p => (Invoice: p.Invoiceno!, Date: LegacyValue.Date(p.Paymentrecdate)))
            .Where(p => p.Date is not null)
            .GroupBy(p => p.Invoice, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(p => p.Date!.Value), StringComparer.Ordinal);

        var collectionSpans = dated
            .Where(r => lastPaymentFor.ContainsKey(r.Number))
            .Select(r => lastPaymentFor[r.Number].DayNumber - r.Date!.Value.DayNumber)
            .Where(days => days >= 0)
            .ToList();

        var lastPurchase = dated.Count == 0 ? (DateOnly?)null : dated.Max(r => r.Date!.Value);

        return new CustomerInsight(
            Code: code,
            Name: name,
            Address: address,
            Phone: phone,
            VatNumber: Tin(vatNumber),
            CreditLimit: creditLimit is > 0 ? creditLimit : null,
            Lifetime: rows.Sum(r => r.Total),
            Outstanding: rows.Sum(r => r.Balance),
            // Aged from the invoice date, matching the dashboard's buckets exactly.
            Overdue: dated.Where(r => r.Balance > 0m && today.DayNumber - r.Date!.Value.DayNumber > 30).Sum(r => r.Balance),
            InvoiceCount: rows.Count,
            FirstPurchase: dated.Count == 0 ? null : dated.Min(r => r.Date!.Value),
            LastPurchase: lastPurchase,
            SilentDays: lastPurchase is { } last ? Math.Max(0, today.DayNumber - last.DayNumber) : null,
            DaysToCollect: collectionSpans.Count == 0 ? null : (int)Math.Round(collectionSpans.Average()),
            MonthlyTrend: Trend(dated.Select(r => (r.Date!.Value, r.Total)).ToList(), today),
            // Newest first: an account is read from what happened last backwards, not from its opening.
            Invoices: dated
                .OrderByDescending(r => r.Date!.Value)
                .ThenByDescending(r => r.Number, StringComparer.Ordinal)
                .Select(r => new CustomerInvoiceRow(
                    r.Number,
                    r.Date,
                    r.Type,
                    r.Total,
                    r.Balance,
                    today.DayNumber - r.Date!.Value.DayNumber))
                .ToList(),
            Payments: payments
                .Select(p => new CustomerPaymentRow(
                    (p.Invoiceno ?? string.Empty).Trim(),
                    LegacyValue.Date(p.Paymentrecdate),
                    LegacyValue.Money(p.Amount),
                    // Blank on 78% of receipts — the field exists and is not filled.
                    string.IsNullOrWhiteSpace(p.Paym) ? "—" : p.Paym!.Trim()))
                .OrderByDescending(p => p.Date ?? DateOnly.MinValue)
                .ToList());
    }

    /// <summary>Twelve months of what this customer bought, empty months kept so a gap reads as a gap.</summary>
    private static List<MonthPoint> Trend(IReadOnlyList<(DateOnly Date, decimal Total)> sales, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var points = new List<MonthPoint>(TrendMonths);

        for (var i = TrendMonths - 1; i >= 0; i--)
        {
            var month = monthStart.AddMonths(-i);
            var inMonth = sales.Where(s => s.Date.Year == month.Year && s.Date.Month == month.Month).ToList();

            // Profit is not shown per customer — invoice cost is a document figure and splitting it by
            // customer would be sound, but the trend here answers "are they still buying", not margin.
            points.Add(new MonthPoint(month, inMonth.Sum(s => s.Total), 0m));
        }

        return points;
    }

    /// <summary>A registration number, or empty when the field holds a placeholder. See InvoiceRenderer.</summary>
    private static string? Tin(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        return value.Any(char.IsDigit) ? value.TrimStart('-', '.', '/', ' ').Trim() : null;
    }
}

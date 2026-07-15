using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Builds the dashboard from raw legacy <c>invoice_h</c> rows — the pure half, so the month totals,
/// the profit, the outstanding sum and the daily chart are unit-testable without a database.
/// </summary>
/// <remarks>
/// It reuses the reporting spine: the same <see cref="LegacyValue"/> money parser and the same
/// <see cref="ReportPeriod"/> window as the reports. The "my" view scopes every figure to one person's
/// <c>preparedby</c> — including outstanding, which the legacy <c>UserDashboard</c> left company-wide.
/// That is a latent legacy bug (someone's "my payments" silently showing everyone's) deliberately not
/// reproduced: here, "mine" means mine.
/// </remarks>
public static class Dashboard
{
    /// <param name="preparedBy">Null → the company view (every invoice in the company). Non-null → the
    /// "my" view, scoped to this exact legacy <c>preparedby</c> name.</param>
    public static DashboardResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        DateOnly monthStart,
        DateOnly monthEnd,
        string? preparedBy,
        long? selectedCompanyId = null,
        IReadOnlyList<DashboardCompanyOption>? companies = null)
    {
        var companyView = preparedBy is null;

        var scoped = companyView
            ? invoices
            : invoices
                .Where(h => string.Equals(h.Preparedby, preparedBy, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var period = new ReportPeriod(monthStart, monthEnd);
        var monthRows = scoped.Where(h => period.ContainsIso(h.Indate)).ToList();

        decimal cash = 0m, credit = 0m, total = 0m, cost = 0m;
        var flagged = 0;

        foreach (var h in monthRows)
        {
            var amount = LegacyValue.Money(h.Totamount, out var amountOk);
            var lineCost = LegacyValue.Money(h.Cost, out var costOk);

            total += amount;
            cost += lineCost;

            if (IsType(h, "Cash"))
            {
                cash += amount;
            }
            else if (IsType(h, "Credit"))
            {
                credit += amount;
            }

            if (!amountOk || !costOk)
            {
                flagged++;
            }
        }

        // Outstanding is a running figure, not a monthly one: the whole scoped balance, all-time.
        // Bare SUM(balance) with no `balance > 0`, exactly as the legacy dashboard reads it.
        decimal outstanding = 0m;
        foreach (var h in scoped)
        {
            outstanding += LegacyValue.Money(h.Balance);
        }

        return new DashboardResponse(
            View: companyView ? "company" : "my",
            Approximate: !companyView,
            PeriodStart: monthStart,
            PeriodEnd: monthEnd,
            CashSales: cash,
            CreditSales: credit,
            TotalSales: total,
            Outstanding: outstanding,
            Profit: total - cost,
            FlaggedCount: flagged,
            Chart: BuildChart(monthRows, monthStart, monthEnd),
            SelectedCompanyId: selectedCompanyId,
            Companies: companies ?? []);
    }

    /// <summary>One point per calendar day of the month — days with no sales are present as zero, so
    /// the chart's x-axis is the month, not just the days that happened to trade.</summary>
    private static List<DailySalesPoint> BuildChart(
        List<InvoiceH> monthRows,
        DateOnly monthStart,
        DateOnly monthEnd)
    {
        var byDay = new Dictionary<DateOnly, (decimal Cash, decimal Credit)>();

        foreach (var h in monthRows)
        {
            if (LegacyValue.Date(h.Indate) is not { } day)
            {
                continue;
            }

            var amount = LegacyValue.Money(h.Totamount);
            var current = byDay.GetValueOrDefault(day);

            if (IsType(h, "Cash"))
            {
                current.Cash += amount;
            }
            else if (IsType(h, "Credit"))
            {
                current.Credit += amount;
            }

            byDay[day] = current;
        }

        var points = new List<DailySalesPoint>();

        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            var value = byDay.GetValueOrDefault(day);
            points.Add(new DailySalesPoint(day, value.Cash, value.Credit));
        }

        return points;
    }

    private static bool IsType(InvoiceH invoice, string type) =>
        string.Equals(invoice.Invtype, type, StringComparison.OrdinalIgnoreCase);
}

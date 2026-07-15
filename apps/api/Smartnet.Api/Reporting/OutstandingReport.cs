using Smartnet.Api.Contracts;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// What customers owe — <c>invoice_h</c> balances that are still &gt; 0, per customer, aged from the
/// invoice date. The one report that must be careful about what it is allowed to claim.
/// </summary>
/// <remarks>
/// It reads the legacy <c>balance</c> column as-is, so the figure matches the statements the business
/// already sends — <b>but that column is wrong by Rs 1.55M</b> across invoices with negative balances
/// (Finding 1), which the legacy <c>balance &gt; 0</c> filter silently drops. So any customer holding a
/// negative-balance invoice is flagged (<see cref="OutstandingRow.HasDefect"/>): the number is shown
/// <i>and</i> marked, never presented as clean and never "corrected" here (Phase 4 is read-only; the
/// remediation phase fixes the data). Aging uses defensive date parsing — the legacy <c>Split('-')</c>
/// throws on a malformed <c>indate</c>; this does not.
/// </remarks>
public static class OutstandingReport
{
    public static OutstandingResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> customerNames,
        DateOnly asOf)
    {
        var rows = invoices
            .GroupBy(h => h.Customer ?? string.Empty, StringComparer.Ordinal)
            .Select(g => Row(g.Key, g.ToList(), customerNames, asOf))
            // A customer belongs on an outstanding report if they owe something — or if a defect is
            // distorting their figure (a negative-balance invoice), which is worth surfacing even at 0.
            .Where(r => r.Outstanding > 0m || r.HasDefect)
            .OrderByDescending(r => r.Outstanding)
            .ThenBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OutstandingResponse(
            TotalOutstanding: rows.Sum(r => r.Outstanding),
            TotalCurrent: rows.Sum(r => r.Current),
            Total30: rows.Sum(r => r.Days30),
            Total60: rows.Sum(r => r.Days60),
            Total90: rows.Sum(r => r.Days90),
            CustomerCount: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            DefectCount: rows.Count(r => r.HasDefect),
            Rows: rows);
    }

    /// <summary>
    /// The per-invoice drill-down — every outstanding invoice (balance &gt; 0) for the given set, aged.
    /// This is what "export selected" produces: the legacy outstanding-invoice list, for the customers
    /// the user ticked.
    /// </summary>
    public static IReadOnlyList<OutstandingDetailRow> Detail(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> names,
        DateOnly asOf) =>
        invoices
            .Select(h => DetailRow(h, names, asOf))
            .Where(r => r.Balance > 0m)
            .OrderBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.Days)
            .ToList();

    private static OutstandingDetailRow DetailRow(
        InvoiceH h,
        IReadOnlyDictionary<string, string> names,
        DateOnly asOf)
    {
        var total = LegacyValue.Money(h.Totamount, out var totalOk);
        var balance = LegacyValue.Money(h.Balance, out var balanceOk);

        var date = LegacyValue.Date(h.Indate);
        var dateOk = string.IsNullOrWhiteSpace(h.Indate) || date is not null;
        var days = date is { } d ? Math.Max(0, asOf.DayNumber - d.DayNumber) : 0;

        var code = h.Customer ?? string.Empty;

        return new OutstandingDetailRow(
            CustomerCode: code,
            CustomerName: names.GetValueOrDefault(code, string.Empty),
            Category: h.It ?? string.Empty,
            InvoiceNo: h.Invoiceno ?? string.Empty,
            Date: date,
            PurchaseOrderNo: h.Pono,
            Total: total,
            Balance: balance,
            Days: days,
            HasDataIssue: !totalOk || !balanceOk || !dateOk);
    }

    private static OutstandingRow Row(
        string code,
        List<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> names,
        DateOnly asOf)
    {
        decimal outstanding = 0m, current = 0m, days30 = 0m, days60 = 0m, days90 = 0m;
        var oldest = 0;
        var count = 0;
        var issue = false;
        var defect = false;

        foreach (var h in invoices)
        {
            var balance = LegacyValue.Money(h.Balance, out var balanceOk);
            if (!balanceOk)
            {
                issue = true;
            }

            if (balance < 0m)
            {
                // Finding 1 — a negative balance the legacy report drops. It distorts the total.
                defect = true;
                continue;
            }

            if (balance <= 0m)
            {
                continue; // settled invoice, not outstanding
            }

            outstanding += balance;
            count++;

            var date = LegacyValue.Date(h.Indate);
            if (!string.IsNullOrWhiteSpace(h.Indate) && date is null)
            {
                issue = true;
            }

            var age = date is { } d ? Math.Max(0, asOf.DayNumber - d.DayNumber) : 0;
            oldest = Math.Max(oldest, age);

            if (age <= 30)
            {
                current += balance;
            }
            else if (age <= 60)
            {
                days30 += balance;
            }
            else if (age <= 90)
            {
                days60 += balance;
            }
            else
            {
                days90 += balance;
            }
        }

        return new OutstandingRow(
            CustomerCode: code,
            CustomerName: names.GetValueOrDefault(code, string.Empty),
            Outstanding: outstanding,
            Current: current,
            Days30: days30,
            Days60: days60,
            Days90: days90,
            OldestDays: oldest,
            InvoiceCount: count,
            HasDataIssue: issue,
            HasDefect: defect);
    }
}

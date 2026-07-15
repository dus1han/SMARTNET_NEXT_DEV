using System.Globalization;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Turns raw legacy <c>invoice_h</c> rows into the sales report — the pure half, with no database in
/// it, so the money parsing and the cash/credit/total arithmetic are unit-testable directly.
/// </summary>
/// <remarks>
/// The controller does the reading (parameterised, read-only, scoped to the caller's company); this
/// does the parsing and the totals. The split is deliberate: the legacy report's bugs are all in this
/// half — the throw on a blank amount, the profit arithmetic — and this is the half a test can pin.
/// </remarks>
public static class SalesReport
{
    public static SalesReportResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> customerNames,
        ReportPeriod period)
    {
        var rows = invoices
            .Where(h => period.ContainsIso(h.Indate))
            .Select(h => Row(h, customerNames))
            // The legacy detail orders by the numeric suffix of the invoice number
            // (CONVERT(SUBSTRING_INDEX(invoiceno,'-',-1),SIGNED)) so "SNI-2" sorts before "SNI-10".
            .OrderBy(r => SuffixNumber(r.InvoiceNo))
            .ThenBy(r => r.InvoiceNo, StringComparer.Ordinal)
            .ToList();

        return new SalesReportResponse(Summarise(rows), rows);
    }

    private static SalesReportRow Row(InvoiceH h, IReadOnlyDictionary<string, string> names)
    {
        var total = LegacyValue.Money(h.Totamount, out var totalOk);
        var balance = LegacyValue.Money(h.Balance, out var balanceOk);
        var cost = LegacyValue.Money(h.Cost, out var costOk);

        var date = LegacyValue.Date(h.Indate);
        // A blank date is fine (unset); a non-blank one we could not read is a defect.
        var dateOk = string.IsNullOrWhiteSpace(h.Indate) || date is not null;

        var code = h.Customer ?? string.Empty;

        return new SalesReportRow(
            Category: h.It ?? string.Empty,
            InvoiceNo: h.Invoiceno ?? string.Empty,
            Type: h.Invtype ?? string.Empty,
            Date: date,
            CustomerCode: code,
            // Left, not inner, join: the legacy detail export inner-joins cus_m and silently drops an
            // invoice whose customer row is missing — so its detail no longer reconciles with its own
            // summary. Keeping the invoice (blank name) preserves that reconciliation and the row.
            CustomerName: names.GetValueOrDefault(code, string.Empty),
            PurchaseOrderNo: h.Pono,
            Total: total,
            Balance: balance,
            Cost: cost,
            Profit: total - cost,
            PreparedBy: h.Preparedby,
            GeneratedAt: h.Cdatetime,
            HasDataIssue: !totalOk || !balanceOk || !costOk || !dateOk);
    }

    private static SalesReportSummary Summarise(List<SalesReportRow> rows)
    {
        static bool Is(SalesReportRow r, string type) =>
            string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase);

        return new SalesReportSummary(
            CashSales: rows.Where(r => Is(r, "Cash")).Sum(r => r.Total),
            CashProfit: rows.Where(r => Is(r, "Cash")).Sum(r => r.Profit),
            CreditSales: rows.Where(r => Is(r, "Credit")).Sum(r => r.Total),
            CreditProfit: rows.Where(r => Is(r, "Credit")).Sum(r => r.Profit),
            // "Total" is every invoice in the window, not Cash + Credit — faithful to the legacy
            // unfiltered SUM, which also captures any invoice whose type is neither.
            TotalSales: rows.Sum(r => r.Total),
            TotalProfit: rows.Sum(r => r.Profit),
            InvoiceCount: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue));
    }

    /// <summary>The trailing number of an invoice code — "SNI-42" → 42 — for the legacy sort order.</summary>
    private static long SuffixNumber(string invoiceNo)
    {
        var dash = invoiceNo.LastIndexOf('-');
        var tail = dash >= 0 ? invoiceNo[(dash + 1)..] : invoiceNo;

        return long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : long.MaxValue;
    }
}

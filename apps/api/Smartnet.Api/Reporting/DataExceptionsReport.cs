using System.Globalization;
using Smartnet.Api.Contracts;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Detects the known legacy-data defects for the Data Exceptions screen (LEGACY-DATA-POLICY §4) — the pure
/// half, with no database in it, so the detection rules are unit-testable directly. The controller does the
/// scoped reading; this classifies.
/// </summary>
/// <remarks>
/// Three defect families, each matching a DATA-AUDIT finding and detected the same way the audit found them:
/// <list type="bullet">
///   <item><b>Duplicate payment</b> (Finding 1) — the same invoice/amount/date recorded more than once. The
///   negative-balance ones are remediated; this keeps the check live so a new duplicate surfaces.</item>
///   <item><b>Paid, no payment</b> (Finding 2) — a credit invoice with a settled (zero) balance but no
///   payment row behind it: a receivable nobody is chasing, or a balance zeroed in error.</item>
///   <item><b>Lines ≠ header</b> (Finding 4) — the line items do not sum to the header's before-discount
///   total, so the document contradicts itself.</item>
/// </list>
/// Cash invoices are excluded from "Paid, no payment": they settle at issue and legitimately carry no
/// payment row, so flagging them would be noise.
/// </remarks>
public static class DataExceptionsReport
{
    /// <summary>The difference above which a header/lines mismatch is a defect, not rounding drift.</summary>
    private const decimal LineMismatchTolerance = 1m;

    public static DataExceptionsResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<InvoiceL> lines,
        IReadOnlyDictionary<string, string> customerNames)
    {
        var rows = new List<DataExceptionRow>();

        rows.AddRange(DuplicatePayments(payments, invoices, customerNames));
        rows.AddRange(PaidNoPayment(invoices, payments, customerNames));
        rows.AddRange(LinesNotHeader(invoices, lines, customerNames));

        var duplicate = rows.Count(r => r.Type == Types.DuplicatePayment);
        var paidNoPayment = rows.Count(r => r.Type == Types.PaidNoPayment);
        var linesNotHeader = rows.Count(r => r.Type == Types.LinesNotHeader);

        var ordered = rows
            .OrderByDescending(r => r.Amount)
            .ThenBy(r => r.Reference, StringComparer.Ordinal)
            .ToList();

        return new DataExceptionsResponse(duplicate, paidNoPayment, linesNotHeader, ordered.Count, ordered);
    }

    private static IEnumerable<DataExceptionRow> DuplicatePayments(
        IReadOnlyList<Payment> payments,
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyDictionary<string, string> customerNames)
    {
        var customerByInvoice = invoices
            .Where(h => !string.IsNullOrEmpty(h.Invoiceno))
            .GroupBy(h => h.Invoiceno!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Customer ?? string.Empty, StringComparer.Ordinal);

        // A duplicate is the same (invoiceno, amount, date) recorded more than once — the finding's own key.
        var groups = payments
            .Where(p => !string.IsNullOrEmpty(p.Invoiceno))
            .GroupBy(p => (p.Invoiceno!, p.Amount ?? string.Empty, p.Paymentrecdate ?? string.Empty))
            .Where(g => g.Count() > 1);

        foreach (var g in groups)
        {
            var (invoiceNo, amountRaw, date) = g.Key;
            var count = g.Count();
            var amount = LegacyValue.Money(amountRaw);
            var extra = amount * (count - 1); // the overstated value: every copy past the first

            yield return new DataExceptionRow(
                Types.DuplicatePayment,
                invoiceNo,
                CustomerName(customerByInvoice, customerNames, invoiceNo),
                $"{count} identical payments of {amount:N2}{(date.Length > 0 ? $" on {date}" : "")} — {count - 1} duplicated",
                extra);
        }
    }

    private static IEnumerable<DataExceptionRow> PaidNoPayment(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<Payment> payments,
        IReadOnlyDictionary<string, string> customerNames)
    {
        var paidInvoices = payments
            .Where(p => !string.IsNullOrEmpty(p.Invoiceno))
            .Select(p => p.Invoiceno!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var h in invoices)
        {
            if (h.Invoiceno is null) continue;
            // Credit only: a cash invoice settles at issue and legitimately has no payment row.
            if (!string.Equals(h.Invtype, "Credit", StringComparison.OrdinalIgnoreCase)) continue;

            var total = LegacyValue.Money(h.Totamount);
            var balance = LegacyValue.Money(h.Balance);
            if (total <= 0m || balance != 0m) continue;
            if (paidInvoices.Contains(h.Invoiceno)) continue;

            yield return new DataExceptionRow(
                Types.PaidNoPayment,
                h.Invoiceno,
                CustomerName(null, customerNames, h.Customer),
                $"Balance is settled but no payment is recorded — a {total:N2} receivable or a balance zeroed in error",
                total);
        }
    }

    private static IEnumerable<DataExceptionRow> LinesNotHeader(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<InvoiceL> lines,
        IReadOnlyDictionary<string, string> customerNames)
    {
        var lineTotalByInvoice = lines
            .Where(l => !string.IsNullOrEmpty(l.Inno))
            .GroupBy(l => l.Inno!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(l => LegacyValue.Money(l.Tot)), StringComparer.Ordinal);

        foreach (var h in invoices)
        {
            if (h.Invoiceno is null) continue;
            if (!lineTotalByInvoice.TryGetValue(h.Invoiceno, out var lineSum) || lineSum <= 0m) continue;

            var header = LegacyValue.Money(h.Beforedisctot);
            var gap = header - lineSum;
            if (Math.Abs(gap) <= LineMismatchTolerance) continue;

            yield return new DataExceptionRow(
                Types.LinesNotHeader,
                h.Invoiceno,
                CustomerName(null, customerNames, h.Customer),
                $"Header {header:N2} vs lines {lineSum:N2} — a {Math.Abs(gap):N2} gap",
                Math.Abs(gap));
        }
    }

    private static string CustomerName(
        Dictionary<string, string>? customerByInvoice,
        IReadOnlyDictionary<string, string> customerNames,
        string? invoiceOrCode)
    {
        if (string.IsNullOrEmpty(invoiceOrCode)) return string.Empty;

        // For duplicate payments the reference is an invoice number → resolve to its customer code first.
        var code = customerByInvoice is not null && customerByInvoice.TryGetValue(invoiceOrCode, out var c)
            ? c
            : invoiceOrCode;

        return customerNames.GetValueOrDefault(code, string.Empty);
    }

    private static class Types
    {
        public const string DuplicatePayment = "Duplicate payment";
        public const string PaidNoPayment = "Paid, no payment";
        public const string LinesNotHeader = "Lines ≠ header";
    }
}

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
/// Seven defect families. The first three match a DATA-AUDIT finding and are detected the same way the audit
/// found them; the rest were found when the receivables ledger was rebuilt from the documents, which stopped
/// a stored balance from covering for a payment history that did not add up:
/// <list type="bullet">
///   <item><b>Duplicate payment</b> (Finding 1) — the same invoice/amount/date recorded more than once. The
///   negative-balance ones are remediated; this keeps the check live so a new duplicate surfaces.</item>
///   <item><b>Paid, no payment</b> (Finding 2) — a credit invoice with a settled (zero) balance but no
///   payment row behind it: a receivable nobody is chasing, or a balance zeroed in error.</item>
///   <item><b>Lines ≠ header</b> (Finding 4) — the line items do not sum to the header's before-discount
///   total, so the document contradicts itself.</item>
///   <item><b>Overpaid</b> — the payments exceed the invoice. Catches what the duplicate rule cannot: two
///   payments of one amount weeks apart, which by its (invoice, amount, date) key are not duplicates.</item>
///   <item><b>Payment without an invoice</b> — a payment naming an invoice that does not exist, or naming
///   none. Money received and attributed to nobody.</item>
///   <item><b>Supplier paid, not settled</b> and <b>Supplier settled twice</b> — the payables equivalents,
///   which nothing checked before because the payables ledger holds no legacy rows at all.</item>
/// </list>
/// Cash invoices are excluded from "Paid, no payment": they settle at issue and legitimately carry no
/// payment row, so flagging them would be noise. Overpayment is judged against the same one-rupee tolerance
/// as the header/lines gap, so rounding drift is not reported as money.
/// </remarks>
public static class DataExceptionsReport
{
    /// <summary>The difference above which a header/lines mismatch is a defect, not rounding drift.</summary>
    private const decimal LineMismatchTolerance = 1m;

    /// <param name="orphanedPayments">Payments naming an invoice that exists nowhere — the controller
    /// resolves these against every invoice number, not the scoped ones, so a payment against another
    /// company's invoice is not mistaken for one against nothing.</param>
    public static DataExceptionsResponse Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<InvoiceL> lines,
        IReadOnlyDictionary<string, string> customerNames,
        IReadOnlyList<Payment>? orphanedPayments = null,
        IReadOnlyList<SupplierInvoice>? supplierInvoices = null,
        IReadOnlyList<SupplierInvPay>? supplierSettlements = null,
        IReadOnlyDictionary<string, string>? supplierNames = null)
    {
        var rows = new List<DataExceptionRow>();

        rows.AddRange(DuplicatePayments(payments, invoices, customerNames));
        rows.AddRange(PaidNoPayment(invoices, payments, customerNames));
        rows.AddRange(LinesNotHeader(invoices, lines, customerNames));
        rows.AddRange(Overpaid(invoices, payments, customerNames));
        rows.AddRange(OrphanedPayments(orphanedPayments ?? []));
        rows.AddRange(SupplierSettlementFaults(
            supplierInvoices ?? [],
            supplierSettlements ?? [],
            supplierNames ?? new Dictionary<string, string>(StringComparer.Ordinal)));

        var duplicate = rows.Count(r => r.Type == Types.DuplicatePayment);
        var paidNoPayment = rows.Count(r => r.Type == Types.PaidNoPayment);
        var linesNotHeader = rows.Count(r => r.Type == Types.LinesNotHeader);
        var overpaid = rows.Count(r => r.Type == Types.Overpaid);
        var orphaned = rows.Count(r => r.Type == Types.OrphanedPayment);
        var supplier = rows.Count(r => r.Type is Types.SupplierPaidNoSettlement or Types.SupplierDuplicateSettlement);

        var ordered = rows
            .OrderByDescending(r => r.Amount)
            .ThenBy(r => r.Reference, StringComparer.Ordinal)
            .ToList();

        return new DataExceptionsResponse(
            duplicate,
            paidNoPayment,
            linesNotHeader,
            overpaid,
            orphaned,
            supplier,
            ordered.Count,
            ordered);
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

    /// <summary>
    /// Invoices paid more than they were worth.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="DuplicatePayments"/>, which keys on (invoice, amount, date) and so only
    /// finds copies taken the same day. STI-38 was paid 71,000 twice — seventeen days apart — and SNI-915
    /// 23,600 twice, three weeks apart. Neither was a duplicate by that key, and neither surfaced, because
    /// the stored balance said zero and a settled invoice looks like a finished one.
    ///
    /// <para>Measuring the payments against the total instead finds them however they were spread. This
    /// is the check that matters now the ledger is derived from the documents: an overpaid invoice shows
    /// the customer in credit, which is true, rather than a zero that concealed it.</para>
    /// </remarks>
    private static IEnumerable<DataExceptionRow> Overpaid(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<Payment> payments,
        IReadOnlyDictionary<string, string> customerNames)
    {
        var paidByInvoice = payments
            .Where(p => !string.IsNullOrEmpty(p.Invoiceno))
            .GroupBy(p => p.Invoiceno!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Sum: g.Sum(p => LegacyValue.Money(p.Amount)), Count: g.Count()), StringComparer.Ordinal);

        foreach (var h in invoices)
        {
            if (h.Invoiceno is null) continue;
            if (!paidByInvoice.TryGetValue(h.Invoiceno, out var paid)) continue;

            var total = LegacyValue.Money(h.Totamount);
            var over = paid.Sum - total;

            if (total <= 0m || over <= LineMismatchTolerance) continue;

            yield return new DataExceptionRow(
                Types.Overpaid,
                h.Invoiceno,
                CustomerName(null, customerNames, h.Customer),
                $"{paid.Count} payments totalling {paid.Sum:N2} against a {total:N2} invoice — {over:N2} overpaid",
                over);
        }
    }

    /// <summary>
    /// Payments that name an invoice which does not exist.
    /// </summary>
    /// <remarks>
    /// The money was taken and recorded against nothing, so no invoice shows it and no customer is
    /// credited with it. The old system's reports joined these away silently, which is why they were never
    /// visible; the ledger rebuild leaves them out for the same reason — there is no invoice to attribute
    /// them to — and this is the only place they can be seen.
    /// </remarks>
    private static IEnumerable<DataExceptionRow> OrphanedPayments(IReadOnlyList<Payment> orphaned)
    {
        foreach (var p in orphaned)
        {
            var reference = p.Invoiceno?.Trim() ?? string.Empty;
            var amount = LegacyValue.Money(p.Amount);
            var named = reference.Length == 0 ? "no invoice at all" : $"invoice {reference}, which does not exist";
            var date = p.Paymentrecdate is { Length: > 0 } d ? $" on {d}" : string.Empty;

            yield return new DataExceptionRow(
                Types.OrphanedPayment,
                reference.Length == 0 ? $"Payment #{p.Id}" : reference,
                string.Empty,
                $"A {amount:N2} payment recorded{date} names {named}",
                amount);
        }
    }

    /// <summary>
    /// The supplier side of the same two questions: invoices marked paid that nothing settled, and invoices
    /// settled more than once.
    /// </summary>
    /// <remarks>
    /// A legacy supplier settlement carries no amount of its own — <c>supplier_inv_pay</c> records which
    /// invoice was paid and when, and the amount <i>is</i> the invoice's. So a payables defect can only be
    /// the presence or absence of that row against <c>paymentstat</c>, never a mismatched figure:
    /// <list type="bullet">
    ///   <item><b>Paid, not settled</b> — <c>paymentstat='Paid'</c> with no settlement row. The invoice is
    ///   out of the payables ledger's outstanding, which reads <c>paymentstat</c>, but nothing records who
    ///   was paid or when.</item>
    ///   <item><b>Settled twice</b> — two settlement rows for one invoice. Since each stands for the whole
    ///   invoice, the second is a second payment of the same money.</item>
    /// </list>
    /// </remarks>
    private static IEnumerable<DataExceptionRow> SupplierSettlementFaults(
        IReadOnlyList<SupplierInvoice> invoices,
        IReadOnlyList<SupplierInvPay> settlements,
        IReadOnlyDictionary<string, string> supplierNames)
    {
        // supinvid is a varchar holding the invoice's id — parse rather than join, since a blank or
        // malformed value must not throw and must not silently match id 0.
        var settlementCount = settlements
            .Select(s => long.TryParse(s.Supinvid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (long?)null)
            .Where(id => id is not null)
            .GroupBy(id => id!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var inv in invoices)
        {
            var count = settlementCount.GetValueOrDefault(inv.Id);
            var paid = string.Equals(inv.Paymentstat, "Paid", StringComparison.OrdinalIgnoreCase);
            var amount = LegacyValue.Money(inv.Amount);
            var reference = inv.Invno is { Length: > 0 } no ? no : $"Supplier invoice #{inv.Id}";
            var supplier = inv.Supcode is { } code ? supplierNames.GetValueOrDefault(code, code) : string.Empty;

            if (paid && count == 0)
            {
                yield return new DataExceptionRow(
                    Types.SupplierPaidNoSettlement,
                    reference,
                    supplier,
                    $"Marked paid but no settlement is recorded — a {amount:N2} payable settled by nothing",
                    amount);
            }
            else if (count > 1)
            {
                yield return new DataExceptionRow(
                    Types.SupplierDuplicateSettlement,
                    reference,
                    supplier,
                    $"{count} settlements against one {amount:N2} invoice — each stands for the whole of it",
                    amount * (count - 1));
            }
        }
    }

    private static class Types
    {
        public const string DuplicatePayment = "Duplicate payment";
        public const string PaidNoPayment = "Paid, no payment";
        public const string LinesNotHeader = "Lines ≠ header";
        public const string Overpaid = "Overpaid";
        public const string OrphanedPayment = "Payment without an invoice";
        public const string SupplierPaidNoSettlement = "Supplier paid, not settled";
        public const string SupplierDuplicateSettlement = "Supplier settled twice";
    }
}

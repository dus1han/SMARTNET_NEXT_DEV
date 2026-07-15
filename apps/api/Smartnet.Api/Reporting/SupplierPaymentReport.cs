using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Payments to a supplier in a period — <c>supplier_invoice</c> joined to <c>supplier_inv_pay</c> on
/// <c>id = supinvid</c>, filtered to paid invoices. The controller does the read and the join; this
/// filters by the paid date and maps. The window is the <c>paiddate</c>, not the invoice date.
/// </summary>
public static class SupplierPaymentReport
{
    public static SupplierPaymentResponse Build(
        IReadOnlyList<(SupplierInvoice Invoice, SupplierInvPay Payment)> joined,
        IReadOnlyDictionary<string, string> supplierNames,
        ReportPeriod period)
    {
        var rows = joined
            .Where(x => string.Equals(x.Invoice.Paymentstat, "Paid", StringComparison.OrdinalIgnoreCase))
            .Where(x => period.ContainsIso(x.Payment.Paiddate))
            .Select(x => Row(x.Invoice, x.Payment, supplierNames))
            .OrderByDescending(r => r.PaidDate ?? DateOnly.MinValue)
            .ToList();

        return new SupplierPaymentResponse(
            Total: rows.Sum(r => r.Amount),
            Count: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static SupplierPaymentRow Row(
        SupplierInvoice invoice,
        SupplierInvPay payment,
        IReadOnlyDictionary<string, string> names)
    {
        var amount = LegacyValue.Money(invoice.Amount, out var amountOk);

        var paidDate = LegacyValue.Date(payment.Paiddate);
        var dateOk = string.IsNullOrWhiteSpace(payment.Paiddate) || paidDate is not null;

        return new SupplierPaymentRow(
            PaidDate: paidDate,
            InvoiceNo: invoice.Invno ?? string.Empty,
            InvoiceDate: LegacyValue.Date(invoice.Invdate),
            Amount: amount,
            PayMethod: payment.PayMethod,
            Reference: payment.Referenceno,
            SupplierName: invoice.Supcode is { Length: > 0 } code ? names.GetValueOrDefault(code, string.Empty) : string.Empty,
            HasDataIssue: !amountOk || !dateOk);
    }
}

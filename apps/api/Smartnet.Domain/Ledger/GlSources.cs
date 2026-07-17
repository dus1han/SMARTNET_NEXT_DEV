namespace Smartnet.Domain.Ledger;

/// <summary>
/// The <c>source_type</c> of a GL entry — which kind of money event raised it. One event of a kind posts
/// exactly once (the (source_type, source_id) idempotency key), and a void posts a distinct reversing entry.
/// The historical backfill uses the same strings, so a re-run never double-posts a live event.
/// </summary>
public static class GlSources
{
    public const string Invoice = "Invoice";
    public const string CreditNote = "CreditNote";
    public const string CustomerReceipt = "CustomerReceipt";
    public const string CustomerReceiptVoid = "CustomerReceiptVoid";
    public const string SupplierInvoice = "SupplierInvoice";
    public const string SupplierInvoiceVoid = "SupplierInvoiceVoid";
    public const string SupplierPayment = "SupplierPayment";
    public const string SupplierPaymentVoid = "SupplierPaymentVoid";
    public const string PayablesPayment = "PayablesPayment";
    public const string Expense = "Expense";
    public const string ExpenseVoid = "ExpenseVoid";
}

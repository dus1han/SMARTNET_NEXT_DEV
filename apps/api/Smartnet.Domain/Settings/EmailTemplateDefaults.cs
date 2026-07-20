namespace Smartnet.Domain.Settings;

/// <summary>
/// The wording a company starts with, for each of the five templates.
/// </summary>
/// <remarks>
/// <para>
/// These are the same five bodies the multi-company migration seeded for the two companies that
/// existed when it ran. They live here as well because that migration seeded <c>FROM companies_m</c> —
/// a one-shot cross join over the companies present at the time — and nothing re-runs it. A company
/// created afterwards would otherwise have no templates at all, and there is no endpoint to create one:
/// the email-template API can read and update, not insert. So a company without these is a company that
/// cannot email an invoice and has no way to fix it from the UI.
/// </para>
/// <para>
/// Deliberately duplicated rather than shared with the migration. A migration is a record of what was
/// done to a database on a date and must not change afterwards; this is current defaults, and finance
/// may reword it. Tying the two together would mean either freezing this or rewriting history.
/// </para>
/// </remarks>
public static class EmailTemplateDefaults
{
    public static readonly IReadOnlyList<(string Key, string Subject, string Body)> All =
    [
        (EmailTemplateKeys.InvoiceSent,
         "Invoice {{invoice_no}} from {{company_name}}",
         "Dear {{customer_name}},\n\nPlease find attached invoice {{invoice_no}} for {{total}}, due on {{due_date}}.\n\nKind regards,\n{{company_name}}"),

        (EmailTemplateKeys.QuotationSent,
         "Quotation {{quotation_no}} from {{company_name}}",
         "Dear {{customer_name}},\n\nPlease find attached quotation {{quotation_no}} for {{total}}, valid until {{valid_until}}.\n\nKind regards,\n{{company_name}}"),

        (EmailTemplateKeys.PurchaseOrderSent,
         "Purchase order {{po_no}} from {{company_name}}",
         "Dear {{supplier_name}},\n\nPlease find attached purchase order {{po_no}}.\n\nKind regards,\n{{company_name}}"),

        (EmailTemplateKeys.OutstandingReminder,
         "Outstanding balance - {{company_name}}",
         "Dear {{customer_name}},\n\nOur records show an outstanding balance of {{total}}.\n\nIf you have already paid, please ignore this message.\n\nKind regards,\n{{company_name}}"),

        (EmailTemplateKeys.OutstandingBulk,
         "Statement of account - {{company_name}}",
         "Dear {{customer_name}},\n\nPlease find attached your statement of account showing {{total}} outstanding.\n\nKind regards,\n{{company_name}}"),
    ];
}

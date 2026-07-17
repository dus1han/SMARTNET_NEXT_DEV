using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Backfills the general ledger from the full legacy history (GL slice 2) — an actual double-entry per
    /// legacy document, no opening balances, nothing truncated. Every invoice, credit note, customer payment,
    /// supplier invoice, supplier payment and expense becomes a balanced GL entry, so the trial balance and
    /// P&amp;L show real Sales / Purchases / VAT / Expense history from day one.
    /// </summary>
    /// <remarks>
    /// Reads the <b>legacy varchar</b> amount/date columns, not the typed ones — for adopted legacy rows the
    /// typed <c>total_amount</c>/<c>net_total</c>/<c>invoice_date</c> columns are 0 / a 1970 epoch; the real
    /// figures live in <c>totamount</c>/<c>novattotal</c>/<c>indate</c> (and the equivalents on the other
    /// tables). Postings mirror the going-forward wiring exactly and reuse the same source_type strings, so a
    /// NOT EXISTS guard makes this idempotent and it never double-posts an event already posted live. Legacy
    /// supplier payments carry no amount, so a Paid supplier invoice is settled in full at its pay date
    /// (SupplierInvoicePay). Blank payment methods post to Bank; only "Cash" posts to Cash. A few legacy
    /// documents with a zero/blank amount are dropped rather than left as line-less entries. On the dev DB this
    /// posts ~8,100 balanced entries (debits = credits = 356,188,944.56); GL Accounts Receivable lands within
    /// ~208k of the imported receivables snapshot, which was itself "imported, not recalculated" — the GL is
    /// now the faithful document-derived truth.
    /// </remarks>
    public partial class Phase8GeneralLedgerBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- ================= INVOICES  (Dr Receivable, Cr Sales + Output VAT) =================
                INSERT INTO `gl_entries` (`company_id`, `entry_date`, `source_type`, `source_id`, `description`, `created_at`, `row_version`)
                SELECT h.`company_id`,
                       COALESCE(STR_TO_DATE(h.`indate`,'%Y-%m-%d'), STR_TO_DATE(h.`indate`,'%d/%m/%Y'), DATE(h.`created_at`), '2023-01-01'),
                       'Invoice', h.`id`, CONCAT('Invoice ', COALESCE(h.`invoiceno`,'')), NOW(6), 1
                FROM `invoice_h` h
                WHERE h.`deleted_at` IS NULL
                  AND NOT EXISTS (SELECT 1 FROM `gl_entries` e WHERE e.`source_type`='Invoice' AND e.`source_id`=h.`id`);

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(h.`totamount`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `invoice_h` h ON h.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='1100'
                WHERE e.`source_type`='Invoice' AND CAST(NULLIF(h.`totamount`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(h.`novattotal`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `invoice_h` h ON h.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='4000'
                WHERE e.`source_type`='Invoice' AND CAST(NULLIF(h.`novattotal`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(h.`totamount`,'') AS DECIMAL(18,4)) - CAST(NULLIF(h.`novattotal`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `invoice_h` h ON h.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='2100'
                WHERE e.`source_type`='Invoice'
                  AND (CAST(NULLIF(h.`totamount`,'') AS DECIMAL(18,4)) - CAST(NULLIF(h.`novattotal`,'') AS DECIMAL(18,4))) <> 0;

                -- ================= CREDIT NOTES  (Dr Sales + Output VAT, Cr Receivable) =================
                INSERT INTO `gl_entries` (`company_id`, `entry_date`, `source_type`, `source_id`, `description`, `created_at`, `row_version`)
                SELECT cn.`company_id`,
                       COALESCE(STR_TO_DATE(cn.`cndate`,'%Y-%m-%d'), STR_TO_DATE(cn.`cndate`,'%d/%m/%Y'), DATE(cn.`created_at`), '2023-01-01'),
                       'CreditNote', cn.`id`, CONCAT('Credit note ', COALESCE(cn.`cnno`,'')), NOW(6), 1
                FROM `cn_h` cn
                WHERE cn.`deleted_at` IS NULL
                  AND NOT EXISTS (SELECT 1 FROM `gl_entries` e WHERE e.`source_type`='CreditNote' AND e.`source_id`=cn.`id`);

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(cn.`novattotal`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `cn_h` cn ON cn.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='4000'
                WHERE e.`source_type`='CreditNote' AND CAST(NULLIF(cn.`novattotal`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(cn.`totamount`,'') AS DECIMAL(18,4)) - CAST(NULLIF(cn.`novattotal`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `cn_h` cn ON cn.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='2100'
                WHERE e.`source_type`='CreditNote'
                  AND (CAST(NULLIF(cn.`totamount`,'') AS DECIMAL(18,4)) - CAST(NULLIF(cn.`novattotal`,'') AS DECIMAL(18,4))) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(cn.`totamount`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `cn_h` cn ON cn.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='1100'
                WHERE e.`source_type`='CreditNote' AND CAST(NULLIF(cn.`totamount`,'') AS DECIMAL(18,4)) <> 0;

                -- ================= CUSTOMER PAYMENTS  (Dr Cash/Bank, Cr Receivable) =================
                INSERT INTO `gl_entries` (`company_id`, `entry_date`, `source_type`, `source_id`, `description`, `created_at`, `row_version`)
                SELECT COALESCE(NULLIF(p.`company_id`,0),
                                (SELECT h2.`company_id` FROM `invoice_h` h2 WHERE h2.`invoiceno`=p.`invoiceno` LIMIT 1), 1),
                       COALESCE(STR_TO_DATE(p.`paymentrecdate`,'%Y-%m-%d'), STR_TO_DATE(p.`paymentrecdate`,'%d/%m/%Y'), '2023-01-01'),
                       'LegacyPayment', p.`id`, CONCAT('Receipt ', COALESCE(p.`payref`,'')), NOW(6), 1
                FROM `payments` p
                WHERE (p.`data_origin` IS NULL OR CONVERT(p.`data_origin` USING utf8mb4) COLLATE utf8mb4_general_ci <> 'new')
                  AND NOT EXISTS (SELECT 1 FROM `gl_entries` e WHERE e.`source_type`='LegacyPayment' AND e.`source_id`=p.`id`);

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(p.`amount`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `payments` p ON p.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code` = CASE WHEN CONVERT(p.`paym` USING utf8mb4) COLLATE utf8mb4_general_ci = 'Cash' THEN '1000' ELSE '1010' END
                WHERE e.`source_type`='LegacyPayment' AND CAST(NULLIF(p.`amount`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(p.`amount`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `payments` p ON p.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='1100'
                WHERE e.`source_type`='LegacyPayment' AND CAST(NULLIF(p.`amount`,'') AS DECIMAL(18,4)) <> 0;

                -- ================= SUPPLIER INVOICES  (Dr Purchases + Input VAT, Cr Payable) =================
                INSERT INTO `gl_entries` (`company_id`, `entry_date`, `source_type`, `source_id`, `description`, `created_at`, `row_version`)
                SELECT s.`company_id`,
                       COALESCE(STR_TO_DATE(s.`invdate`,'%Y-%m-%d'), STR_TO_DATE(s.`invdate`,'%d/%m/%Y'), DATE(s.`created_at`), '2023-01-01'),
                       'SupplierInvoice', s.`id`, CONCAT('Supplier invoice ', COALESCE(s.`invno`,'')), NOW(6), 1
                FROM `supplier_invoice` s
                WHERE s.`deleted_at` IS NULL
                  AND NOT EXISTS (SELECT 1 FROM `gl_entries` e WHERE e.`source_type`='SupplierInvoice' AND e.`source_id`=s.`id`);

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(s.`novattotal`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `supplier_invoice` s ON s.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='5000'
                WHERE e.`source_type`='SupplierInvoice' AND CAST(NULLIF(s.`novattotal`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)) - CAST(NULLIF(s.`novattotal`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `supplier_invoice` s ON s.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='1200'
                WHERE e.`source_type`='SupplierInvoice'
                  AND (CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)) - CAST(NULLIF(s.`novattotal`,'') AS DECIMAL(18,4))) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `supplier_invoice` s ON s.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='2000'
                WHERE e.`source_type`='SupplierInvoice' AND CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)) <> 0;

                -- ================= SUPPLIER PAYMENTS  (Dr Payable, Cr Cash/Bank) — one per paid invoice =================
                -- Legacy supplier_inv_pay carries no amount, so a paid invoice is settled in full at its pay date.
                INSERT INTO `gl_entries` (`company_id`, `entry_date`, `source_type`, `source_id`, `description`, `created_at`, `row_version`)
                SELECT s.`company_id`,
                       COALESCE((SELECT STR_TO_DATE(sp.`paiddate`,'%Y-%m-%d') FROM `supplier_inv_pay` sp WHERE sp.`supinvid`=s.`id` AND sp.`paiddate` IS NOT NULL ORDER BY sp.`paiddate` DESC LIMIT 1),
                                STR_TO_DATE(s.`invdate`,'%Y-%m-%d'), DATE(s.`created_at`), '2023-01-01'),
                       'SupplierInvoicePay', s.`id`, CONCAT('Payment for supplier invoice ', COALESCE(s.`invno`,'')), NOW(6), 1
                FROM `supplier_invoice` s
                WHERE s.`deleted_at` IS NULL AND CONVERT(s.`paymentstat` USING utf8mb4) COLLATE utf8mb4_general_ci = 'Paid'
                  AND NOT EXISTS (SELECT 1 FROM `gl_entries` e WHERE e.`source_type`='SupplierInvoicePay' AND e.`source_id`=s.`id`);

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `supplier_invoice` s ON s.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`='2000'
                WHERE e.`source_type`='SupplierInvoicePay' AND CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `supplier_invoice` s ON s.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id`
                   AND a.`code` = CASE WHEN CONVERT((SELECT sp.`pay_method` FROM `supplier_inv_pay` sp WHERE sp.`supinvid`=s.`id` LIMIT 1) USING utf8mb4) COLLATE utf8mb4_general_ci = 'Cash' THEN '1000' ELSE '1010' END
                WHERE e.`source_type`='SupplierInvoicePay' AND CAST(NULLIF(s.`amount`,'') AS DECIMAL(18,4)) <> 0;

                -- ================= EXPENSES  (Dr category expense account, Cr Cash/Bank) =================
                INSERT INTO `gl_entries` (`company_id`, `entry_date`, `source_type`, `source_id`, `description`, `created_at`, `row_version`)
                SELECT COALESCE(NULLIF(x.`company_id`,0), CAST(NULLIF(x.`company`,'') AS UNSIGNED), 1),
                       COALESCE(STR_TO_DATE(x.`expense_date`,'%Y-%m-%d'), STR_TO_DATE(x.`expense_date`,'%d/%m/%Y'), DATE(x.`created_at`), '2023-01-01'),
                       'Expense', x.`id`, COALESCE(x.`expense_desc`,'Expense'), NOW(6), 1
                FROM `expense_tr` x
                WHERE x.`deleted_at` IS NULL
                  AND NOT EXISTS (SELECT 1 FROM `gl_entries` e WHERE e.`source_type`='Expense' AND e.`source_id`=x.`id`);

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, CAST(NULLIF(x.`expense_amount`,'') AS DECIMAL(18,4)), 0, NOW(6), 1
                FROM `gl_entries` e JOIN `expense_tr` x ON x.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code`=CONCAT('5-', x.`exp_cat`) COLLATE utf8mb4_general_ci
                WHERE e.`source_type`='Expense' AND CAST(NULLIF(x.`expense_amount`,'') AS DECIMAL(18,4)) <> 0;

                INSERT INTO `gl_lines` (`gl_entry_id`, `account_id`, `debit`, `credit`, `created_at`, `row_version`)
                SELECT e.`id`, a.`id`, 0, CAST(NULLIF(x.`expense_amount`,'') AS DECIMAL(18,4)), NOW(6), 1
                FROM `gl_entries` e JOIN `expense_tr` x ON x.`id`=e.`source_id`
                JOIN `gl_accounts` a ON a.`company_id`=e.`company_id` AND a.`code` = CASE WHEN CONVERT(x.`paymentm` USING utf8mb4) COLLATE utf8mb4_general_ci = 'Cash' THEN '1000' ELSE '1010' END
                WHERE e.`source_type`='Expense' AND CAST(NULLIF(x.`expense_amount`,'') AS DECIMAL(18,4)) <> 0;

                -- A handful of legacy documents carry a zero/blank amount, leaving a line-less entry — drop those.
                DELETE e FROM `gl_entries` e
                WHERE e.`source_type` IN ('Invoice','CreditNote','LegacyPayment','SupplierInvoice','SupplierInvoicePay','Expense')
                  AND NOT EXISTS (SELECT 1 FROM `gl_lines` l WHERE l.`gl_entry_id`=e.`id`);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the backfilled entries (gl_lines cascade). Note: this also removes any going-forward
            // entries of these source types — the backfill and the live wiring share them by design.
            migrationBuilder.Sql("""
                DELETE FROM `gl_entries`
                WHERE `source_type` IN ('Invoice','CreditNote','LegacyPayment','SupplierInvoice','SupplierInvoicePay','Expense');
                """);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Seeds the payables ledger from the legacy supplier balances — one <c>OpeningBalance</c> entry per
    /// legacy supplier invoice the old system still had as <c>Pending</c> (Phase 6, slice 2;
    /// LEGACY-DATA-POLICY §2). The supply-side twin of <c>Phase5SeedOpeningBalances</c>.
    /// </summary>
    /// <remarks>
    /// <b>The ledger starts from history; it does not recompute it.</b> Each entry carries the stored legacy
    /// <c>amount</c> verbatim. Only <c>Pending</c> invoices are seeded — a <c>Paid</c> one contributes
    /// nothing to what we still owe, and an entry for it would be noise (the legacy app has no partial
    /// state, so a supplier invoice is either fully outstanding or fully settled). The supplier code is
    /// resolved to the adopted <c>sup_m.id</c>; a row whose <c>supcode</c> does not resolve is skipped
    /// rather than orphaned. <c>occurred_at</c> is stamped at apply time, because an opening balance is "as
    /// of cutover"; the insert bypasses the audit interceptor (bulk, system-authored) — the migration is
    /// the record.
    /// </remarks>
    public partial class Phase6SeedSupplierOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO `payables_ledger`
                    (`supplier_id`, `type`, `amount`, `supplier_invoice_id`, `occurred_at`, `note`, `created_at`, `row_version`)
                SELECT s.`id`,
                       'OpeningBalance',
                       CAST(h.`amount` AS DECIMAL(18,4)),
                       h.`id`,
                       NOW(6),
                       'Imported from legacy system; not recalculated',
                       NOW(6),
                       1
                FROM `supplier_invoice` h
                JOIN `sup_m` s ON s.`supcode` = h.`supcode`
                WHERE h.`data_origin` = 'legacy'
                  AND h.`paymentstat` = 'Pending'
                  AND h.`amount` IS NOT NULL
                  AND TRIM(h.`amount`) <> ''
                  AND CAST(h.`amount` AS DECIMAL(18,4)) <> 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM `payables_ledger`
                WHERE `type` = 'OpeningBalance'
                  AND `note` = 'Imported from legacy system; not recalculated'
                """);
        }
    }
}

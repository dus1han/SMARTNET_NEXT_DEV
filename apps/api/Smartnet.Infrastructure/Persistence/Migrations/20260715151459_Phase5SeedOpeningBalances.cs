using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Seeds the receivables ledger from the legacy balances — one <c>OpeningBalance</c> entry per
    /// legacy invoice that still owes something (Phase 5, slice 1; LEGACY-DATA-POLICY §2).
    /// </summary>
    /// <remarks>
    /// <b>The ledger starts from history; it does not recompute it.</b> Each entry carries the stored
    /// legacy <c>balance</c> <i>verbatim</i> — including the 45 rows that are negative (Finding 1). The
    /// new system therefore does not claim history is correct, only that it is faithfully reproduced: a
    /// customer's derived balance equals the legacy figure until the business chooses to correct a defect
    /// through a proper, audited adjustment (the Data Exceptions screen).
    ///
    /// <para><b>Verified safe before writing</b> (2026-07-15, against the dev copy): every
    /// <c>invoice_h.balance</c> is numeric (0 rows of junk), so <c>CAST(… AS DECIMAL)</c> cannot
    /// mis-read a comma; every invoice's <c>customer</c> code resolves to a <c>cus_m</c> row (0
    /// unmatched), so the join drops nothing. 353 invoices carry a non-zero balance, summing to
    /// Rs. 10,033,563.79 — the figure the ledger must reproduce, asserted after apply.</para>
    ///
    /// <para>Only non-zero balances are seeded: a fully-settled invoice contributes nothing to what a
    /// customer owes, and an entry for it would be noise. The sum is identical either way.</para>
    ///
    /// <para><b>Timing.</b> An opening balance is "as of cutover". On the dev copy that is now; in
    /// production this migration is applied <i>at cutover</i>, alongside routing invoices to the new
    /// stack — not before, or the snapshot would predate the last legacy payments. <c>occurred_at</c> is
    /// stamped at apply time for that reason. The insert bypasses the audit interceptor (it is bulk,
    /// system-authored), exactly as the multi-company seed rows did; the migration itself is the record.</para>
    /// </remarks>
    public partial class Phase5SeedOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO `receivables_ledger`
                    (`customer_id`, `type`, `amount`, `invoice_id`, `occurred_at`, `note`, `created_at`, `row_version`)
                SELECT c.`id`,
                       'OpeningBalance',
                       CAST(h.`balance` AS DECIMAL(18,4)),
                       h.`id`,
                       NOW(6),
                       'Imported from legacy system; not recalculated',
                       NOW(6),
                       1
                FROM `invoice_h` h
                JOIN `cus_m` c ON c.`cuscode` = h.`customer`
                WHERE h.`data_origin` = 'legacy'
                  AND h.`balance` IS NOT NULL
                  AND TRIM(h.`balance`) <> ''
                  AND CAST(h.`balance` AS DECIMAL(18,4)) <> 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM `receivables_ledger`
                WHERE `type` = 'OpeningBalance'
                  AND `note` = 'Imported from legacy system; not recalculated'
                """);
        }
    }
}

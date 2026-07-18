using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Rebuilds the receivables ledger from the documents themselves, replacing the imported opening
    /// balances (Phase 5) with a <c>Charge</c> per legacy invoice and a <c>Payment</c> per legacy payment.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> The opening-balance seed carried <c>invoice_h.balance</c> verbatim — a figure
    /// already net of every legacy payment. That made the balance right but the history absent: 2,226
    /// payments existed in the old system and not one appeared in the ledger, so nothing could show a
    /// customer what they had paid, and no payment could be voided because there was no entry to reverse.
    /// A ledger with one opaque line per invoice is a balance, not a ledger.</para>
    ///
    /// <para><b>Safe to switch, and measured before writing.</b> Of 2,485 legacy invoices, <b>2,482</b>
    /// satisfy <c>balance = total − payments</c> exactly, so their derived balance is unchanged by this
    /// migration. Three do not, and move by −73,100 in total:</para>
    /// <list type="bullet">
    ///   <item><c>STI-38</c> — total 71,000, paid 142,000. Paid twice.</item>
    ///   <item><c>SNI-915</c> — total 23,600, paid 47,200. Paid twice.</item>
    ///   <item><c>STI-1068</c> — total 21,500, no payment recorded, yet marked settled.</item>
    /// </list>
    ///
    /// <para>All three currently show a balance of zero, which is what hides the defect. Afterwards they
    /// show what the records actually say — two customers in credit, one still owing — and surface in Data
    /// Exceptions, which exists precisely to remove a duplicate payment or record a missing one. That is
    /// LEGACY-DATA-POLICY's position: carry the data faithfully and make the defect visible, rather than
    /// let a settled-looking balance conceal it.</para>
    ///
    /// <para><b>What is not migrated.</b> Three payment rows name an invoice absent from
    /// <c>invoice_h</c>; the join drops them, as the old system's own reports did. They are a data
    /// exception in their own right, left for that screen rather than invented into the ledger.</para>
    ///
    /// <para><b>Dates.</b> Entries are dated from the documents — <c>indate</c> and
    /// <c>paymentrecdate</c>, both of which parse cleanly on every row — not from this migration's run
    /// time. An aged-debt report only means anything if entries carry the dates things happened on.</para>
    /// </remarks>
    public partial class Phase8LedgerFromDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every opening balance, whatever wrote it — not just the Phase 5 seed's.
            //
            // Matching on the seed's note alone was tried and was wrong: SI-35 also carried an
            // OpeningBalance of 50,000 written later by the Data Exceptions resolver ("Recomputed after
            // removing duplicate payments"). It survived the delete, stacked on top of the rebuilt Charge
            // and Payments, and double-counted that invoice by exactly 50,000.
            //
            // Nothing is lost by removing it. `invoice_h.balance` already reflects that remediation, and
            // the rebuilt entries reproduce it from the documents: 144,750 − 50,000 − 44,750 = 50,000.
            // After this migration an opening balance is not a thing this ledger has.
            migrationBuilder.Sql("""
                DELETE FROM `receivables_ledger`
                WHERE `type` = 'OpeningBalance'
                """);

            // One Charge per legacy invoice — the customer owed its total on the day it was raised.
            migrationBuilder.Sql("""
                INSERT INTO `receivables_ledger`
                    (`customer_id`, `type`, `amount`, `invoice_id`, `occurred_at`, `note`, `created_at`, `row_version`)
                SELECT c.`id`,
                       'Charge',
                       CAST(h.`totamount` AS DECIMAL(18,4)),
                       h.`id`,
                       COALESCE(STR_TO_DATE(h.`indate`, '%Y-%m-%d'), NOW(6)),
                       CONCAT('Invoice ', h.`invoiceno`, ' - rebuilt from legacy documents'),
                       NOW(6),
                       1
                FROM `invoice_h` h
                JOIN `cus_m` c ON c.`cuscode` = h.`customer`
                WHERE h.`data_origin` = 'legacy'
                  AND h.`totamount` IS NOT NULL
                  AND TRIM(h.`totamount`) <> ''
                  AND CAST(h.`totamount` AS DECIMAL(18,4)) <> 0
                """);

            // One Payment per legacy payment, negative — it reduced what was owed, on the day it arrived.
            // data_origin is NULL on every legacy row; a receipt raised by this app writes 'new' and posts
            // its own ledger entry, so it must never be counted a second time here.
            migrationBuilder.Sql("""
                INSERT INTO `receivables_ledger`
                    (`customer_id`, `type`, `amount`, `invoice_id`, `occurred_at`, `note`, `created_at`, `row_version`)
                SELECT c.`id`,
                       'Payment',
                       -CAST(p.`amount` AS DECIMAL(18,4)),
                       h.`id`,
                       COALESCE(STR_TO_DATE(p.`paymentrecdate`, '%Y-%m-%d'), NOW(6)),
                       CONCAT('Payment ', COALESCE(NULLIF(TRIM(p.`payref`), ''), CONCAT('#', p.`id`)), ' - rebuilt from legacy documents'),
                       NOW(6),
                       1
                FROM `payments` p
                JOIN `invoice_h` h ON h.`invoiceno` = p.`invoiceno`
                JOIN `cus_m` c ON c.`cuscode` = h.`customer`
                WHERE (p.`data_origin` IS NULL OR p.`data_origin` <> 'new')
                  AND p.`amount` IS NOT NULL
                  AND TRIM(p.`amount`) <> ''
                  AND CAST(p.`amount` AS DECIMAL(18,4)) <> 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove what this migration wrote, and any opening balance still present, before restoring
            // the seed's. Deleting only the rebuilt entries was tried and left SI-35 holding both its
            // re-inserted opening and the remediation row it already had — the same double-count in
            // reverse. The rollback has to own the whole opening-balance set, exactly as Up does.
            migrationBuilder.Sql("""
                DELETE FROM `receivables_ledger`
                WHERE (`type` IN ('Charge', 'Payment') AND `note` LIKE '%- rebuilt from legacy documents')
                   OR `type` = 'OpeningBalance'
                """);

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
    }
}

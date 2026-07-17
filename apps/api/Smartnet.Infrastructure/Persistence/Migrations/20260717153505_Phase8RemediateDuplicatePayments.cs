using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Remediates DATA-AUDIT FINDING 1 — the Rs. 1,575,374 of duplicate customer payments (44 groups, 49
    /// extra rows). The legacy <c>CusPaymentsController.savePay</c> inserted a payment and then ran
    /// <c>UPDATE invoice_h SET balance = balance - amount</c> with no transaction, idempotency key or
    /// duplicate check, so a double-clicked Save recorded the same payment (same invoice, same amount, same
    /// date) two or more times and subtracted it twice. This deletes the duplicate rows, recomputes every
    /// affected balance from what remains, and brings the general ledger and the seeded receivables ledger
    /// back into agreement.
    /// </summary>
    /// <remarks>
    /// <b>A deliberate, business-confirmed correction — not the default policy.</b> LEGACY-DATA-POLICY §2 keeps
    /// legacy balances verbatim and corrects defects through audited adjustments, and originally listed the 45
    /// negatives as "left as-is". The business chose the direct cleanup: the duplicates are unambiguous — each
    /// is the same invoice/amount/date recorded within seconds.
    ///
    /// <para>Two shapes of damage, both fixed by recomputing <c>balance = total − Σ(surviving payments)</c>
    /// over the affected invoices:</para>
    /// <list type="bullet">
    ///   <item><b>45 negative balances</b> (−Rs. 1,554,424). 43 had duplicate rows; 2 (STI-665, STI-961) had
    ///   only their balance double-<i>subtracted</i> with no duplicate row — caught because they are negative.
    ///   All 45 recompute to exactly 0.</item>
    ///   <item><b>SI-35</b> — a duplicate that did <i>not</i> make the invoice negative: it had masked a real
    ///   Rs. 50,000 receivable (total 144,750, one 50,000 payment duplicated, so genuine payments are 94,750).
    ///   Removing the duplicate resurfaces the 50,000 owed. <b>Confirmed correct by the business (2026-07-17)</b>
    ///   — the customer does owe it — so it is remediated too, and a receivables opening entry is created for
    ///   it (the invoice had none, its legacy balance having been 0).</item>
    /// </list>
    ///
    /// <para><b>Verified on the dev copy (2026-07-17):</b> 49 duplicate rows and their 49 GL <c>LegacyPayment</c>
    /// entries removed; 45 invoices → 0 and SI-35 → 50,000; the trial balance stays balanced; and the
    /// dashboard's Outstanding equals the Outstanding report. A row-level snapshot taken before applying it is
    /// at <c>docs/remediation/finding1-backup-dev.txt</c>; the reconciliation checks are in
    /// <c>docs/remediation/RECONCILIATION.md</c>.</para>
    ///
    /// <para>A duplicate is keyed on (<c>invoiceno</c>, <c>amount</c>, <c>paymentrecdate</c>) exactly as the
    /// finding defined it; the earliest row (<c>MIN(id)</c>) of each group is the keeper. The affected set is
    /// every invoice that had a duplicate row removed <i>or</i> carries a negative balance. Naturally
    /// idempotent — a second run finds no duplicate groups, no negatives, and reseeds the same openings. Order
    /// matters: the GL entries go while the duplicate payment ids still exist to join on, then the payments,
    /// then the balances, then the receivables openings are reseeded to the recomputed balances.</para>
    /// </remarks>
    public partial class Phase8RemediateDuplicatePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- The extra (non-keeper) payment rows of every duplicate group.
                CREATE TEMPORARY TABLE `_dup_pay_ids` AS
                SELECT p.`id`
                FROM `payments` p
                JOIN (
                    SELECT `invoiceno`, `amount`, `paymentrecdate`, MIN(`id`) AS keep_id
                    FROM `payments`
                    GROUP BY `invoiceno`, `amount`, `paymentrecdate`
                    HAVING COUNT(*) > 1
                ) g ON g.`invoiceno` <=> p.`invoiceno`
                   AND g.`amount` <=> p.`amount`
                   AND g.`paymentrecdate` <=> p.`paymentrecdate`
                WHERE p.`id` <> g.keep_id;

                -- The invoices to recompute: any that had a duplicate row removed, plus any left negative by a
                -- double-subtracted balance that had no duplicate row.
                CREATE TEMPORARY TABLE `_affected_inv` AS
                SELECT h.`id`
                FROM `invoice_h` h
                WHERE h.`deleted_at` IS NULL
                  AND ( h.`invoiceno` IN (
                            SELECT DISTINCT p.`invoiceno` FROM `payments` p
                            WHERE p.`id` IN (SELECT `id` FROM `_dup_pay_ids`))
                        OR CAST(NULLIF(h.`balance`, '') AS DECIMAL(18,4)) < 0 );

                -- 1) Remove the GL postings (Dr Cash/Bank, Cr Receivable) made for the duplicate receipts —
                --    lines first, then the balanced entry. Done while the payment ids still exist to join on.
                DELETE l FROM `gl_lines` l
                JOIN `gl_entries` e ON e.`id` = l.`gl_entry_id`
                WHERE e.`source_type` = 'LegacyPayment'
                  AND e.`source_id` IN (SELECT `id` FROM `_dup_pay_ids`);

                DELETE FROM `gl_entries`
                WHERE `source_type` = 'LegacyPayment'
                  AND `source_id` IN (SELECT `id` FROM `_dup_pay_ids`);

                -- 2) Remove the duplicate payment rows themselves.
                DELETE FROM `payments` WHERE `id` IN (SELECT `id` FROM `_dup_pay_ids`);

                -- 3) Recompute every affected balance from what remains: total − Σ(surviving payments).
                --    Negatives land at 0; SI-35 lands at its real 50,000 outstanding.
                UPDATE `invoice_h` h
                JOIN `_affected_inv` a ON a.`id` = h.`id`
                SET h.`balance` = CAST(
                    CAST(NULLIF(h.`totamount`, '') AS DECIMAL(18,4)) - COALESCE((
                        SELECT SUM(CAST(NULLIF(p.`amount`, '') AS DECIMAL(18,4)))
                        FROM `payments` p WHERE p.`invoiceno` = h.`invoiceno`
                    ), 0) AS CHAR)
                WHERE h.`deleted_at` IS NULL;

                -- 4) Reseed the receivables opening entries to match the recomputed balances (Phase 5 seeded one
                --    per non-zero legacy balance). Drop the affected invoices' openings, then re-insert one for
                --    each whose balance is now non-zero — so a settled invoice (0) has none and SI-35 gains a
                --    50,000 opening it never had.
                DELETE r FROM `receivables_ledger` r
                JOIN `_affected_inv` a ON a.`id` = r.`invoice_id`
                WHERE r.`type` = 'OpeningBalance';

                INSERT INTO `receivables_ledger`
                    (`customer_id`, `type`, `amount`, `invoice_id`, `occurred_at`, `note`, `created_at`, `row_version`)
                SELECT c.`id`, 'OpeningBalance',
                       CAST(NULLIF(h.`balance`, '') AS DECIMAL(18,4)), h.`id`, NOW(6),
                       'Recomputed after removing duplicate payments (Finding 1 remediation)', NOW(6), 1
                FROM `_affected_inv` a
                JOIN `invoice_h` h ON h.`id` = a.`id`
                JOIN `cus_m` c ON c.`cuscode` = h.`customer`
                WHERE CAST(NULLIF(h.`balance`, '') AS DECIMAL(18,4)) <> 0;

                DROP TEMPORARY TABLE `_dup_pay_ids`;
                DROP TEMPORARY TABLE `_affected_inv`;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately one-way. This deletes duplicate rows and recomputes balances; the removed rows
            // cannot be reconstructed from what remains, so there is no faithful automatic inverse. To roll
            // back, restore the affected rows from the snapshot taken before applying it
            // (docs/remediation/finding1-backup-dev.txt on dev; the equivalent pre-run export on live).
        }
    }
}

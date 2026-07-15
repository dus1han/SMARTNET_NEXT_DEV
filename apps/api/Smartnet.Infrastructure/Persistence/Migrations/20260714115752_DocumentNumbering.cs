using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes a duplicate document number impossible.
    /// </summary>
    /// <remarks>
    /// Closes ISSUES B4 — which is not hypothetical. The live data already contains two different
    /// quotations, for two different customers, both numbered <c>STQ-0</c>. The legacy app takes a
    /// number from a ticket table and never checks that it is unused, and nothing in the schema
    /// stops the duplicate from landing.
    ///
    /// <para><b>The series values are deliberately not seeded here.</b> They are read from the
    /// legacy data at run time by <c>NumberSeriesInitialiser</c>, because this migration reaches
    /// production long after it is written — by which time the last invoice number will have moved
    /// on. A migration that hardcoded "1571" would be wrong the day after it was authored, and
    /// wrong in the worst way: it would reissue numbers that are already on printed invoices.</para>
    /// </remarks>
    public partial class DocumentNumbering : Migration
    {
        /// <remarks>
        /// quotation_h is <b>deliberately absent</b>. It holds the two STQ-0 rows above, so the
        /// index cannot be created while they stand. They are NOT renumbered: somebody has a PDF
        /// with STQ-0 printed on it, and LEGACY-DATA-POLICY.md is explicit that legacy data is left
        /// as-is while defects surface for the business to resolve. It goes to Data Exceptions.
        /// Add quotation_h here once that is settled.
        /// </remarks>
        private static readonly (string Table, string Column)[] UniquelyNumbered =
        [
            ("invoice_h", "invoiceno"),
            ("cn_h", "cnno"),
            ("po_h", "po_no"),
            ("jobs_m", "jobno"),
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Checked before writing this: invoice_h (2,485 rows), cn_h, po_h and jobs_m each have
            // zero duplicates and zero blanks today, so every one of these indexes builds cleanly.
            //
            // This also changes the LEGACY app's behaviour: if it ever tries to issue a duplicate
            // number it now fails with a constraint violation instead of silently writing one.
            // That is the intended outcome — and it is exactly why the duplicate check above
            // mattered, because an index that could not build would have taken the old app down.
            foreach (var (table, column) in UniquelyNumbered)
            {
                migrationBuilder.Sql(
                    $"CREATE UNIQUE INDEX `UX_{table}_{column}` ON `{table}` (`{column}`)");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in UniquelyNumbered)
            {
                migrationBuilder.Sql($"DROP INDEX `UX_{table}_{column}` ON `{table}`");
            }
        }
    }
}

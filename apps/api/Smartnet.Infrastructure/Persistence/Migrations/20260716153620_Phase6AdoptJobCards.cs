using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adopts <c>jobs_m</c> onto the new side and creates the new <c>jobcard_l</c> line table (Phase 6,
    /// slice 3).
    /// </summary>
    /// <remarks>
    /// <b>Partly HAND-WRITTEN.</b> <c>dotnet ef migrations add</c> generated <c>CreateTable</c> for
    /// <c>jobs_m</c> — it cannot know the table already exists in production — which would drop the live job
    /// history. That half is replaced by the additive SQL below; the <c>jobcard_l</c> <c>CreateTable</c> is
    /// kept as generated, because it <i>is</i> a genuinely new table (the legacy job card had no line table).
    /// <b>If this migration is regenerated, the jobs_m half must be rewritten like this again.</b>
    ///
    /// <para><b>Additive, per DEVELOPMENT.md §8.</b> <c>jobs_m</c> has no key, so a surrogate <c>id</c>
    /// primary key is added; <c>company_id</c> already exists (multi-company migration), and every legacy
    /// column — the shared ones (<c>jobno</c>, <c>contactperson</c>, <c>faultD</c>, <c>remarks</c>,
    /// <c>jobdoneby</c>, <c>jstat</c>, <c>completionremarks</c>) and the varchar shadows (<c>company</c>,
    /// <c>customer</c>, <c>jdate</c>, <c>enteredby</c>, <c>entereddt</c>, <c>cost</c>, <c>sell</c>,
    /// <c>completedby</c>, <c>dompleteddt</c>, <c>items</c>) — is left exactly as it is for the surviving
    /// legacy Crystal job sheet. The new typed columns are added at the end.</para>
    /// </remarks>
    public partial class Phase6AdoptJobCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- jobs_m: the new typed columns (legacy columns and company_id already exist) --------------
            migrationBuilder.Sql("""
                ALTER TABLE `jobs_m`
                    ADD COLUMN `customer_id`  bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `job_date`     date          NOT NULL DEFAULT '1970-01-01',
                    ADD COLUMN `entered_by`   bigint        NULL,
                    ADD COLUMN `entered_at`   datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `cost_amount`  decimal(18,4) NULL,
                    ADD COLUMN `sell_amount`  decimal(18,4) NULL,
                    ADD COLUMN `completed_by` bigint        NULL,
                    ADD COLUMN `completed_at` datetime(6)   NULL,
                    ADD COLUMN `data_origin`  varchar(16)   NOT NULL DEFAULT 'legacy',
                    ADD COLUMN `created_by`   bigint        NULL,
                    ADD COLUMN `created_at`   datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`   bigint        NULL,
                    ADD COLUMN `updated_at`   datetime(6)   NULL,
                    ADD COLUMN `deleted_by`   bigint        NULL,
                    ADD COLUMN `deleted_at`   datetime(6)   NULL,
                    ADD COLUMN `row_version`  int           NOT NULL DEFAULT 1
                """);

            migrationBuilder.Sql("""
                ALTER TABLE `jobs_m`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            // --- jobcard_l: a genuinely new table — EF's generated CreateTable, kept as-is -----------------
            migrationBuilder.CreateTable(
                name: "jobcard_l",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    job_card_id = table.Column<long>(type: "bigint", nullable: true),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    serial = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort = table.Column<int>(type: "int", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    row_version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobcard_l", x => x.id);
                    table.ForeignKey(
                        name: "FK_jobcard_l_jobs_m_job_card_id",
                        column: x => x.job_card_id,
                        principalTable: "jobs_m",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_jobcard_l_job_card_id",
                table: "jobcard_l",
                column: "job_card_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // jobcard_l is a new table — drop it (this also removes its FK to jobs_m).
            migrationBuilder.DropTable(
                name: "jobcard_l");

            // Reverse the jobs_m adoption: drop the surrogate key, then the new columns. company_id and the
            // legacy columns predate this migration and outlive a rollback — no DropTable.
            migrationBuilder.Sql("ALTER TABLE `jobs_m` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `jobs_m` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `jobs_m` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `jobs_m`
                    DROP COLUMN `customer_id`,
                    DROP COLUMN `job_date`,
                    DROP COLUMN `entered_by`,
                    DROP COLUMN `entered_at`,
                    DROP COLUMN `cost_amount`,
                    DROP COLUMN `sell_amount`,
                    DROP COLUMN `completed_by`,
                    DROP COLUMN `completed_at`,
                    DROP COLUMN `data_origin`,
                    DROP COLUMN `created_by`,
                    DROP COLUMN `created_at`,
                    DROP COLUMN `updated_by`,
                    DROP COLUMN `updated_at`,
                    DROP COLUMN `deleted_by`,
                    DROP COLUMN `deleted_at`,
                    DROP COLUMN `row_version`
                """);
        }
    }
}

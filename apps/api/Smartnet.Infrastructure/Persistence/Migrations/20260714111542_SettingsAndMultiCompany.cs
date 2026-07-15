using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Smartnet.Domain.Settings;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SettingsAndMultiCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: true),
                    setting_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    setting_value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_app_settings", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // --- companies_m: ADOPTED, not created -----------------------------------------
            // EF generated a CreateTable here and a DropTable in Down(). Both were replaced by
            // hand: companies_m already exists and holds the two live trading entities (Smart
            // Technologies and Smart Net), and the legacy app still reads name and vatcode from
            // it. Everything below is additive, and `id` — AUTO_INCREMENT under a non-unique
            // index, like user_m — is promoted to a real primary key.
            foreach (var (column, type) in new[]
            {
                ("is_vat_registered", "tinyint(1) NOT NULL DEFAULT 0"),
                ("vat_number",          "varchar(64) NULL"),
                ("address_line1",       "varchar(200) NULL"),
                ("address_line2",       "varchar(200) NULL"),
                ("city",                "varchar(100) NULL"),
                ("country",             "varchar(100) NULL"),
                ("phone",               "varchar(50) NULL"),
                ("email",               "varchar(200) NULL"),
                ("website",             "varchar(200) NULL"),
                ("bank_name",           "varchar(100) NULL"),
                ("bank_branch",         "varchar(100) NULL"),
                ("bank_account_name",   "varchar(100) NULL"),
                ("bank_account_number", "varchar(64) NULL"),
                ("logo_key",            "varchar(255) NULL"),
                ("brand_colour",        "varchar(16) NULL"),
                ("created_by",          "bigint NULL"),
                ("created_at",          "datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)"),
                ("updated_by",          "bigint NULL"),
                ("updated_at",          "datetime(6) NULL"),
                ("deleted_by",          "bigint NULL"),
                ("deleted_at",          "datetime(6) NULL"),
                ("row_version",         "int NOT NULL DEFAULT 1"),
            })
            {
                migrationBuilder.Sql($"ALTER TABLE `companies_m` ADD COLUMN `{column}` {type}");
            }

            migrationBuilder.AddPrimaryKey(
                name: "PK_companies_m",
                table: "companies_m",
                column: "id");

            AddCompanyIdToDocuments(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "document_series",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    doc_type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prefix = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    next_number = table.Column<long>(type: "bigint", nullable: false),
                    padding = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_document_series", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: true),
                    recipient = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    template_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_ref = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    sent_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_log", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    template_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_email_templates", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mail_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    host = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    port = table.Column<int>(type: "int", nullable: false),
                    use_ssl = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    username = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password_encrypted = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    from_address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    from_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reply_to = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bcc = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    send_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    daily_limit = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_mail_settings", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tax_rates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    percentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_default = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                    table.PrimaryKey("PK_tax_rates", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_app_settings_company_id_setting_key",
                table: "app_settings",
                columns: new[] { "company_id", "setting_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_series_company_id_doc_type",
                table: "document_series",
                columns: new[] { "company_id", "doc_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_log_company_id_sent_at",
                table: "email_log",
                columns: new[] { "company_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_log_document_ref",
                table: "email_log",
                column: "document_ref");

            migrationBuilder.CreateIndex(
                name: "IX_email_templates_company_id_template_key",
                table: "email_templates",
                columns: new[] { "company_id", "template_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mail_settings_company_id",
                table: "mail_settings",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tax_rates_company_id_effective_from",
                table: "tax_rates",
                columns: new[] { "company_id", "effective_from" });

            // Last: every table the seed writes into now exists.
            SeedSettings(migrationBuilder);
        }

        /// <inheritdoc />

        /// <summary>
        /// Puts a real <c>company_id</c> on every document. DEVELOPMENT-PLAN decision 4.
        /// </summary>
        /// <remarks>
        /// "This is the decision most expensive to reverse. Adding company_id to documents on day
        /// one is nearly free; retrofitting it after 20,000 invoices exist is not."
        ///
        /// <para><b>The legacy schema already half-has this.</b> Eight document tables carry a
        /// <c>company</c> VARCHAR holding "1" or "2", and the data is genuinely split — 1,196
        /// invoices for Smart Technologies and 1,289 for Smart Net. So this is not an invention;
        /// it is the same fact, typed properly, indexed, and extended to the tables that were
        /// missing it.</para>
        ///
        /// <para>The legacy <c>company</c> column is left exactly where it is. The old app still
        /// reads it. It goes in Phase 9.</para>
        /// </remarks>
        /// <summary>Seeds the settings that must exist before anything can read them.</summary>
        private static void SeedSettings(MigrationBuilder migrationBuilder)
        {
            // --- The seven business rules, at their current behaviour -----------------------
            // Seeded globally (company_id NULL); a company may override any of them later.
            //
            // The values are chosen so that NOTHING CHANGES on the day this runs. In particular
            // credit_limit.enforced is FALSE, matching the legacy app, which checks the limit only
            // on service invoices. Seeding it true would silently start blocking invoices the
            // counter staff have always been able to raise, and they would find out at the till.
            foreach (var (key, value) in BusinessRules.Defaults)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO app_settings (company_id, setting_key, setting_value, created_at, row_version)
                    VALUES (NULL, '{key}', '{value}', UTC_TIMESTAMP(), 1)
                    """);
            }

            // --- Tax rates, per company -----------------------------------------------------
            // The live vat_validity holds one rate: 18%, effective 2024-01-01. It is copied here
            // as DECIMAL(9,4) rather than left as the legacy double.
            //
            // Both a VAT rate and a zero rate are seeded, because a document may carry either and
            // the Phase 5 tax engine snapshots the rate onto each LINE — which is what makes a
            // mixed-rate invoice possible. The legacy app cannot represent one at all.
            migrationBuilder.Sql("""
                INSERT INTO tax_rates
                    (company_id, name, percentage, effective_from, effective_to, is_default, created_at, row_version)
                SELECT c.id, 'VAT 18%', 18.0000, '2024-01-01', NULL, 1, UTC_TIMESTAMP(), 1
                FROM companies_m c
                """);

            migrationBuilder.Sql("""
                INSERT INTO tax_rates
                    (company_id, name, percentage, effective_from, effective_to, is_default, created_at, row_version)
                SELECT c.id, 'Zero-rated', 0.0000, '2024-01-01', NULL, 0, UTC_TIMESTAMP(), 1
                FROM companies_m c
                """);

            // --- Email templates ------------------------------------------------------------
            // Lifted out of the C# they are currently hardcoded in (A2b), so finance can reword a
            // dunning letter without a developer and a release.
            var templates = new (string Key, string Subject, string Body)[]
            {
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
            };

            foreach (var (key, subject, body) in templates)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO email_templates (company_id, template_key, subject, body, created_at, row_version)
                    SELECT c.id, '{key}', '{subject.Replace("'", "''", StringComparison.Ordinal)}',
                           '{body.Replace("'", "''", StringComparison.Ordinal)}', UTC_TIMESTAMP(), 1
                    FROM companies_m c
                    """);
            }

            // --- Numbering series: DELIBERATELY NOT SEEDED ----------------------------------
            //
            // It would be easy to insert a row per company per document type with next_number = 1.
            // It would also be wrong: there are already 2,508 invoices, and a series that starts at
            // 1 hands the next one a number that is 2,500 invoices old. Nothing in the schema would
            // stop it — invoice_h has no unique index on invoiceno (Finding 6) — so the duplicate
            // would simply land, silently, in the ledger.
            //
            // The true next number has to be read out of the legacy *_seq tables, and doing that
            // correctly is Phase 5's job, alongside the transactional allocator that replaces them.
            // So no series exists yet, and Phase 5 must fail loudly when it finds none.
            //
            // An absent series is a loud error. A series with a plausible wrong number is a silent
            // one. Given the choice, take the loud error.

            // --- Mail settings: also not seeded ---------------------------------------------
            // A row here needs a real SMTP password, and the whole point of A2 is that we do not
            // put one in source. An administrator sets it in the settings screen, where it is
            // encrypted at rest and write-only thereafter.
        }

        private static void AddCompanyIdToDocuments(MigrationBuilder migrationBuilder)
        {
            // Documents that already know their company, as a varchar.
            string[] withLegacyColumn =
            [
                "invoice_h", "quotation_h", "po_h", "cheques",
                "expense_tr", "jobs_m", "supplier_invoice", "del_invoice_h",
            ];

            // Documents that do NOT: payments and credit notes were never company-aware at all.
            // They hang off an invoice, so their company is their invoice's company.
            string[] derivedFromInvoice = ["payments", "cn_h", "del_cn_h"];

            foreach (var table in withLegacyColumn.Concat(derivedFromInvoice))
            {
                // NULLABLE, on purpose, and for two separate reasons:
                //
                //  1. The legacy app INSERTs into these tables without naming this column. A NOT
                //     NULL column with no default would break every one of those writes the moment
                //     this migration lands, and the old app is still the live app.
                //
                //  2. Some rows genuinely cannot be attributed — see the orphaned payments below.
                //     A wrong company_id is worse than an absent one: it silently files somebody's
                //     money under the wrong trading entity.
                migrationBuilder.Sql($"ALTER TABLE `{table}` ADD COLUMN `company_id` bigint NULL");
            }

            // Backfill from the varchar the legacy app has been maintaining all along.
            foreach (var table in withLegacyColumn)
            {
                migrationBuilder.Sql($"""
                    UPDATE `{table}`
                    SET company_id = CAST(`company` AS UNSIGNED)
                    WHERE `company` REGEXP '^[0-9]+$'
                    """);
            }

            // Payments and credit notes inherit their invoice's company.
            //
            // Safe to join on invoiceno, which is checked rather than assumed: invoice_h has no
            // primary key and no unique index on invoiceno (Finding 6), so nothing in the schema
            // prevents two invoices sharing a number. There are currently zero duplicates and zero
            // invoice numbers spanning both companies, so this join is unambiguous today.
            foreach (var table in new[] { "payments", "cn_h", "del_cn_h" })
            {
                migrationBuilder.Sql($"""
                    UPDATE `{table}` t
                    JOIN invoice_h i ON i.invoiceno = t.invoiceno
                    SET t.company_id = CAST(i.`company` AS UNSIGNED)
                    WHERE i.`company` REGEXP '^[0-9]+$'
                    """);
            }

            // Three payments reference an invoice that does not exist. They keep company_id NULL.
            // That is deliberate, and it is the policy: LEGACY-DATA-POLICY.md — legacy data is left
            // as-is, errors are prevented from cutover forward, and known defects surface in the
            // Data Exceptions screen for the business to correct when it chooses. Inventing a
            // company for an orphaned payment would bury the defect instead of reporting it.

            foreach (var table in withLegacyColumn.Concat(derivedFromInvoice))
            {
                // Every list in the app is scoped by company, so this index is on the hot path of
                // essentially every query written from Phase 3 onward.
                migrationBuilder.Sql($"CREATE INDEX `IX_{table}_company_id` ON `{table}` (`company_id`)");
            }
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            // NOT DropTable — companies_m holds the two live trading entities, and the legacy app
            // still reads them. Down() takes back only what Up() added.
            migrationBuilder.DropPrimaryKey(name: "PK_companies_m", table: "companies_m");

            foreach (var column in new[]
            {
                "is_vat_registered", "vat_number",
                "address_line1", "address_line2", "city", "country", "phone", "email", "website",
                "bank_name", "bank_branch", "bank_account_name", "bank_account_number",
                "logo_key", "brand_colour",
                "created_by", "created_at", "updated_by", "updated_at",
                "deleted_by", "deleted_at", "row_version",
            })
            {
                migrationBuilder.DropColumn(name: column, table: "companies_m");
            }

            migrationBuilder.DropTable(
                name: "document_series");

            migrationBuilder.DropTable(
                name: "email_log");

            migrationBuilder.DropTable(
                name: "email_templates");

            migrationBuilder.DropTable(
                name: "mail_settings");

            migrationBuilder.DropTable(
                name: "tax_rates");
        }
    }
}

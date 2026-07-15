using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// Invoices, mapped onto the adopted legacy <c>invoice_h</c> / <c>invoice_l</c> tables.
/// </summary>
/// <remarks>
/// The legacy app still reads these tables (its payments and job-card modules, and the new app's own
/// Phase 4 reports — confirmed 2026-07-15), so the adoption is <b>additive</b>: the typed properties map
/// to <b>new</b> columns added beside the legacy <c>varchar</c> ones, which are left exactly as they are.
/// Only the columns that are genuinely shared — the business number, the PO, the contact, a line's
/// description and item code — map to their legacy column directly. Everything money or date gets a new
/// <c>decimal</c>/<c>date</c> column; the legacy shadow is kept in step by the save pipeline (Phase 5,
/// slice 1) for legacy readers, and collapsed into these types at the Phase 9 retype.
///
/// <para><c>data_origin</c> defaults to <c>legacy</c> at the database — so every existing row, and every
/// row the legacy app inserts, is marked legacy without the old app knowing the column exists. The new
/// app sets it to <c>new</c>. That column is the boundary the legacy payments screen is scoped by, so it
/// can never touch a new invoice's balance.</para>
/// </remarks>
public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoice_h");

        // The surrogate key the migration adds — invoice_h has none (Finding 6).
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        // Shared with the legacy app: the business number, PO and contact are the legacy columns.
        builder.Property(i => i.Number).HasColumnName("invoiceno").HasMaxLength(100);
        builder.Property(i => i.PurchaseOrderNo).HasColumnName("pono").HasMaxLength(100);
        builder.Property(i => i.ContactPerson).HasColumnName("contactperson").HasMaxLength(100);

        // New, typed columns — the new app's source of truth; the legacy varchar columns (customer,
        // company, indate, invtype, preparedby, totamount, …) sit beside them, written by the pipeline.
        builder.Property(i => i.CustomerId).HasColumnName("customer_id");
        builder.Property(i => i.CompanyId).HasColumnName("company_id");
        builder.Property(i => i.Date).HasColumnName("invoice_date");
        builder.Property(i => i.PreparedBy).HasColumnName("prepared_by");

        builder.Property(i => i.Type)
            .HasColumnName("invoice_type")
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(i => i.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(18,4)");
        builder.Property(i => i.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(i => i.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(18,4)");
        builder.Property(i => i.NetTotal).HasColumnName("net_total").HasColumnType("decimal(18,4)");
        builder.Property(i => i.TaxRateId).HasColumnName("tax_rate_id");
        builder.Property(i => i.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasColumnType("decimal(18,4)");
        builder.Property(i => i.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(18,4)");
        builder.Property(i => i.Total).HasColumnName("total_amount").HasColumnType("decimal(18,4)");
        builder.Property(i => i.Cost).HasColumnName("cost_amount").HasColumnType("decimal(18,4)");

        builder.Property(i => i.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        builder.ConfigureAuditColumns();

        // Only this app's own invoices. The 2,485 existing legacy rows share this table but their new
        // typed columns are meaningless defaults — the new app must never read one as an Invoice. The
        // legacy read-model (SmartnetLegacyDbContext.InvoiceH) is how legacy rows are read.
        builder.HasQueryFilter(i => i.DataOrigin == "new" && i.DeletedAt == null);

        builder.HasMany(i => i.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_l");

        // invoice_l has no key of its own; the migration adds one.
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.InvoiceId).HasColumnName("invoice_id");
        builder.Property(l => l.ItemId).HasColumnName("item_id");

        // Shared with the legacy line: the description and item code are the legacy columns. `desc` is
        // a legacy `text` column (a description can be long) — mapped as such so nothing tries to narrow
        // it and truncate an existing value.
        builder.Property(l => l.Description).HasColumnName("desc").HasColumnType("text");
        builder.Property(l => l.ItemCode).HasColumnName("itemcode").HasMaxLength(100);

        // New, typed money/quantity columns beside the legacy qty/rate/tot varchars.
        builder.Property(l => l.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(18,4)");
        builder.Property(l => l.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Gross).HasColumnName("gross").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Net).HasColumnName("net").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Cost).HasColumnName("cost").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();

        // The line is read as part of its invoice — always by parent.
        builder.HasIndex(l => l.InvoiceId);
    }
}

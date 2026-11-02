using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// Proforma invoices — tables of the new app's own. The legacy app has no proforma, so unlike quotations
/// and invoices there is no adopted table and no varchar shadow to keep in step.
/// </summary>
public class ProformaInvoiceConfiguration : IEntityTypeConfiguration<ProformaInvoice>
{
    public void Configure(EntityTypeBuilder<ProformaInvoice> builder)
    {
        builder.ToTable("proforma_invoices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Number).HasColumnName("number").HasMaxLength(100).IsRequired();
        builder.Property(p => p.CompanyId).HasColumnName("company_id");
        builder.Property(p => p.QuotationId).HasColumnName("quotation_id");
        builder.Property(p => p.QuotationNumber).HasColumnName("quotation_number").HasMaxLength(100);
        builder.Property(p => p.CustomerId).HasColumnName("customer_id");
        builder.Property(p => p.Date).HasColumnName("proforma_date");
        builder.Property(p => p.ContactPerson).HasColumnName("contact_person").HasMaxLength(100);
        builder.Property(p => p.Validity).HasColumnName("validity").HasMaxLength(100);
        builder.Property(p => p.PreparedBy).HasColumnName("prepared_by");

        builder.Property(p => p.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(18,4)");
        builder.Property(p => p.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(p => p.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.NetTotal).HasColumnName("net_total").HasColumnType("decimal(18,4)");
        builder.Property(p => p.TaxRateId).HasColumnName("tax_rate_id");
        builder.Property(p => p.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasColumnType("decimal(18,4)");
        builder.Property(p => p.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.Total).HasColumnName("total_amount").HasColumnType("decimal(18,4)");

        builder.Property(p => p.ConvertedToInvoiceId).HasColumnName("converted_to_invoice_id");
        builder.Property(p => p.ConvertedAt).HasColumnName("converted_at");
        builder.Property(p => p.ConvertedBy).HasColumnName("converted_by");
        builder.Ignore(p => p.IsConverted);

        builder.ConfigureAuditColumns();

        // A voided proforma is soft-deleted: it stays on record, but no ordinary read sees it, which is what
        // frees its quotation. The voided list reads past this filter on purpose.
        builder.HasQueryFilter(p => p.DeletedAt == null);

        // A number is unique within its company — the series guarantees it, the index proves it.
        builder.HasIndex(p => new { p.CompanyId, p.Number }).IsUnique();
        builder.HasIndex(p => p.QuotationId);
        builder.HasIndex(p => p.ConvertedToInvoiceId);

        builder.HasMany(p => p.Lines)
            .WithOne(l => l.ProformaInvoice)
            .HasForeignKey(l => l.ProformaInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProformaInvoiceLineConfiguration : IEntityTypeConfiguration<ProformaInvoiceLine>
{
    public void Configure(EntityTypeBuilder<ProformaInvoiceLine> builder)
    {
        builder.ToTable("proforma_invoice_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.ProformaInvoiceId).HasColumnName("proforma_invoice_id");
        builder.Property(l => l.ItemId).HasColumnName("item_id");
        builder.Property(l => l.ItemCode).HasColumnName("item_code").HasMaxLength(100);
        builder.Property(l => l.Description).HasColumnName("description").HasColumnType("text");

        builder.Property(l => l.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(18,4)");
        builder.Property(l => l.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Gross).HasColumnName("gross").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Net).HasColumnName("net").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Cost).HasColumnName("cost").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>quotation_h</c> legacy varchar columns the new save writes alongside its typed ones — EF
/// shadow properties set by the save pipeline, one list so the mapping and the writing cannot drift. The
/// analogues of the invoice header shadows, with the quotation's own column names (<c>qdate</c>,
/// <c>quotecost</c>). <c>q_no</c>, <c>contactperson</c> and <c>q_valid</c> are <b>not</b> here — they map
/// to real entity properties, being shared columns with no type conversion.
/// </summary>
internal static class QuotationLegacyShadow
{
    public const string It = "it";
    public const string QDate = "qdate";
    public const string Customer = "customer";
    public const string TotAmount = "totamount";
    public const string PreparedBy = "preparedby";
    public const string CDateTime = "cdatetime";
    public const string QuoteCost = "quotecost";
    public const string NoVatTotal = "novattotal";
    public const string VType = "vtype";
    public const string VPer = "vper";
    public const string DiscountPer = "discountper";
    public const string BeforeDiscTot = "beforedisctot";
    public const string Company = "company";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (It, "it", 100), (QDate, "qdate", 100), (Customer, "customer", 100), (TotAmount, "totamount", 100),
        (PreparedBy, "preparedby", 100), (CDateTime, "cdatetime", 100), (QuoteCost, "quotecost", 100),
        (NoVatTotal, "novattotal", 100), (VType, "vtype", 100), (VPer, "vper", 100),
        (DiscountPer, "discountper", 50), (BeforeDiscTot, "beforedisctot", 100), (Company, "company", 100),
    ];
}

/// <summary>The <c>quotation_l</c> legacy varchar columns written beside a new line's typed ones.</summary>
/// <remarks>The line total column is <c>total</c> here (invoices call theirs <c>tot</c>).</remarks>
internal static class QuotationLineLegacyShadow
{
    public const string Qno = "qno";
    public const string Qty = "qty";
    public const string Rate = "rate";
    public const string Total = "total";

    public static readonly (string Name, string Column)[] All =
        [(Qno, "qno"), (Qty, "qty"), (Rate, "rate"), (Total, "total")];
}

/// <summary>
/// Quotations, mapped onto the adopted legacy <c>quotation_h</c> / <c>quotation_l</c> tables.
/// </summary>
/// <remarks>
/// Additive adoption, exactly as invoices were (see <see cref="InvoiceConfiguration"/>): the typed
/// properties map to <b>new</b> columns beside the legacy <c>varchar</c> ones, which the save keeps in
/// step for the surviving legacy reader (a customer's quote history). <c>company_id</c> already exists
/// (the multi-company migration added it); <c>q_no</c>, <c>contactperson</c>, <c>q_valid</c>, <c>desc</c>
/// and <c>itemcode</c> are shared columns and map directly. Everything money or date gets a new column.
/// </remarks>
public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotation_h");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).HasColumnName("id");

        // Shared with the legacy app: the business number, contact and validity are the legacy columns.
        builder.Property(q => q.Number).HasColumnName("q_no").HasMaxLength(100);
        builder.Property(q => q.ContactPerson).HasColumnName("contactperson").HasMaxLength(100);
        builder.Property(q => q.Validity).HasColumnName("q_valid").HasMaxLength(100);

        // New, typed columns — the new app's source of truth; the legacy varchar columns sit beside them.
        builder.Property(q => q.CustomerId).HasColumnName("customer_id");
        builder.Property(q => q.CompanyId).HasColumnName("company_id");
        builder.Property(q => q.Date).HasColumnName("quotation_date");
        builder.Property(q => q.PreparedBy).HasColumnName("prepared_by");

        builder.Property(q => q.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(18,4)");
        builder.Property(q => q.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(q => q.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(18,4)");
        builder.Property(q => q.NetTotal).HasColumnName("net_total").HasColumnType("decimal(18,4)");
        builder.Property(q => q.TaxRateId).HasColumnName("tax_rate_id");
        builder.Property(q => q.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasColumnType("decimal(18,4)");
        builder.Property(q => q.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(18,4)");
        builder.Property(q => q.Total).HasColumnName("total_amount").HasColumnType("decimal(18,4)");
        builder.Property(q => q.Cost).HasColumnName("cost_amount").HasColumnType("decimal(18,4)");

        // The conversion link — the back-link the legacy conversion never had. Plain scalar columns
        // (no navigation): the converter sets them, and refuses a second conversion, in one transaction.
        builder.Property(q => q.ConvertedToInvoiceId).HasColumnName("converted_to_invoice_id");
        builder.Property(q => q.ConvertedAt).HasColumnName("converted_at");
        builder.Property(q => q.ConvertedBy).HasColumnName("converted_by");
        builder.Ignore(q => q.IsConverted);

        builder.Property(q => q.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        // Legacy shadow columns, written alongside on save so the surviving legacy reader sees a whole
        // row. discountper and beforedisctot are NOT NULL (as on invoice_h), so a new quotation must
        // write them; the rest keep the reader whole. Shadow properties, not fields on the domain entity.
        foreach (var (name, column, length) in QuotationLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        // Only this app's own quotations; the existing legacy rows share the table but are never read as
        // a Quotation (their new typed columns are meaningless defaults).
        builder.HasQueryFilter(q => q.DataOrigin == "new" && q.DeletedAt == null);

        builder.HasMany(q => q.Lines)
            .WithOne(l => l.Quotation)
            .HasForeignKey(l => l.QuotationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> builder)
    {
        builder.ToTable("quotation_l");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.QuotationId).HasColumnName("quotation_id");
        builder.Property(l => l.ItemId).HasColumnName("item_id");

        // Shared with the legacy line: the description and item code are the legacy columns. `desc` is a
        // legacy `text` column, mapped as such so nothing narrows and truncates an existing value.
        builder.Property(l => l.Description).HasColumnName("desc").HasColumnType("text");
        builder.Property(l => l.ItemCode).HasColumnName("itemcode").HasMaxLength(100);

        // The legacy line shadow columns (qno, qty, rate, total), written beside the typed ones.
        foreach (var (name, column) in QuotationLineLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(100);
        }

        // New, typed money/quantity columns beside the legacy qty/rate/total varchars.
        builder.Property(l => l.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(18,4)");
        builder.Property(l => l.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Gross).HasColumnName("gross").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Net).HasColumnName("net").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Cost).HasColumnName("cost").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();

        builder.HasIndex(l => l.QuotationId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>cn_h</c> legacy varchar columns the new save writes alongside its typed ones — EF shadow
/// properties set by the save pipeline, one list so the mapping and the writing cannot drift. A credit note
/// has a smaller legacy header than an invoice: no customer, company, discount or contact columns (it
/// inherits those from its parent invoice), but its own <c>invoiceno</c> (the parent number, NOT NULL) and
/// <c>stockposting</c> (NOT NULL). <c>cnno</c> is <b>not</b> here — it maps to the entity's <c>Number</c>.
/// </summary>
internal static class CreditNoteLegacyShadow
{
    public const string InvoiceNo = "invoiceno";
    public const string CnDate = "cndate";
    public const string TotAmount = "totamount";
    public const string PreparedBy = "preparedby";
    public const string CDateTime = "cdatetime";
    public const string NoVatTotal = "novattotal";
    public const string VType = "vtype";
    public const string VPer = "vper";
    public const string StockPosting = "stockposting";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (InvoiceNo, "invoiceno", 100), (CnDate, "cndate", 100), (TotAmount, "totamount", 100),
        (PreparedBy, "preparedby", 100), (CDateTime, "cdatetime", 100), (NoVatTotal, "novattotal", 100),
        (VType, "vtype", 100), (VPer, "vper", 100), (StockPosting, "stockposting", 100),
    ];
}

/// <summary>The <c>cn_l</c> legacy varchar columns written beside a new line's typed ones.</summary>
/// <remarks>The line total column is <c>tot</c> (as on <c>invoice_l</c>); the header ref column is <c>cnno</c>.</remarks>
internal static class CreditNoteLineLegacyShadow
{
    public const string Cnno = "cnno";
    public const string Qty = "qty";
    public const string Rate = "rate";
    public const string Tot = "tot";

    public static readonly (string Name, string Column)[] All =
        [(Cnno, "cnno"), (Qty, "qty"), (Rate, "rate"), (Tot, "tot")];
}

/// <summary>
/// Credit notes, mapped onto the adopted legacy <c>cn_h</c> / <c>cn_l</c> tables.
/// </summary>
/// <remarks>
/// Additive adoption, exactly as invoices and quotations were (see <see cref="InvoiceConfiguration"/>): the
/// typed properties map to <b>new</b> columns beside the legacy <c>varchar</c> ones, which the save keeps in
/// step. <c>company_id</c> already exists (the multi-company migration added it to <c>cn_h</c>, backfilled
/// from the parent invoice); the new typed <c>invoice_id</c> is the surrogate parent link the legacy
/// <c>invoiceno</c> string only approximated. Everything money or date gets a new column; the retype is
/// Phase 9.
/// </remarks>
public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("cn_h");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        // Shared with the legacy app: the business number is the legacy cnno column.
        builder.Property(c => c.Number).HasColumnName("cnno").HasMaxLength(100);

        // New, typed columns — the new app's source of truth; the legacy varchar columns sit beside them.
        builder.Property(c => c.InvoiceId).HasColumnName("invoice_id");
        builder.Property(c => c.CustomerId).HasColumnName("customer_id");
        builder.Property(c => c.CompanyId).HasColumnName("company_id");
        builder.Property(c => c.Date).HasColumnName("credit_note_date");
        builder.Property(c => c.ReturnsStock).HasColumnName("returns_stock");
        builder.Property(c => c.PreparedBy).HasColumnName("prepared_by");

        builder.Property(c => c.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(18,4)");
        builder.Property(c => c.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(c => c.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(18,4)");
        builder.Property(c => c.NetTotal).HasColumnName("net_total").HasColumnType("decimal(18,4)");
        builder.Property(c => c.TaxRateId).HasColumnName("tax_rate_id");
        builder.Property(c => c.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasColumnType("decimal(18,4)");
        builder.Property(c => c.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(18,4)");
        builder.Property(c => c.Total).HasColumnName("total_amount").HasColumnType("decimal(18,4)");
        builder.Property(c => c.Cost).HasColumnName("cost_amount").HasColumnType("decimal(18,4)");

        builder.Property(c => c.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        // Legacy shadow columns, written alongside on save. invoiceno (the parent number) and stockposting
        // are NOT NULL, so a new credit note must write them; the rest keep a legacy reader whole. Shadow
        // properties, not fields on the domain entity.
        foreach (var (name, column, length) in CreditNoteLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        // Only this app's own credit notes; the existing legacy rows share the table but are never read as
        // a CreditNote (their new typed columns are meaningless defaults).
        builder.HasQueryFilter(c => c.DataOrigin == "new" && c.DeletedAt == null);

        builder.HasMany(c => c.Lines)
            .WithOne(l => l.CreditNote)
            .HasForeignKey(l => l.CreditNoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        builder.ToTable("cn_l");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.CreditNoteId).HasColumnName("credit_note_id");
        builder.Property(l => l.ItemId).HasColumnName("item_id");

        // Shared with the legacy line: the description and item code are the legacy columns. `desc` is a
        // legacy `text` column, mapped as such so nothing narrows and truncates an existing value.
        builder.Property(l => l.Description).HasColumnName("desc").HasColumnType("text");
        builder.Property(l => l.ItemCode).HasColumnName("itemcode").HasMaxLength(100);

        // The legacy line shadow columns (cnno, qty, rate, tot), written beside the typed ones.
        foreach (var (name, column) in CreditNoteLineLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(100);
        }

        // New, typed money/quantity columns beside the legacy qty/rate/tot varchars.
        builder.Property(l => l.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(18,4)");
        builder.Property(l => l.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Gross).HasColumnName("gross").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Net).HasColumnName("net").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Cost).HasColumnName("cost").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();

        builder.HasIndex(l => l.CreditNoteId);
    }
}

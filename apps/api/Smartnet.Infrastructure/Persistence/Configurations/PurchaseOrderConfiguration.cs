using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>po_h</c> legacy varchar columns the new save writes alongside its typed ones — EF shadow
/// properties set by the save pipeline, one list so the mapping and the writing cannot drift. The
/// analogues of the invoice/quotation header shadows, with the PO's own column names — its VAT columns are
/// <c>vatty</c>/<c>vatpercent</c>/<c>nonvattotal</c> (not <c>vtype</c>/<c>vper</c>/<c>novattotal</c>), and
/// it has <b>no</b> <c>it</c>, <c>contactperson</c>, <c>discountper</c> or <c>beforedisctot</c> columns.
/// <c>po_no</c> is <b>not</b> here — it maps to a real entity property, being a shared column.
/// </summary>
internal static class PurchaseOrderLegacyShadow
{
    public const string PoDate = "podate";
    public const string Supplier = "supplier";
    public const string TotAmount = "totamount";
    public const string PreparedBy = "preparedby";
    public const string CDateTime = "cdatetime";
    public const string NonVatTotal = "nonvattotal";
    public const string VatTy = "vatty";
    public const string VatPercent = "vatpercent";
    public const string Company = "company";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (PoDate, "podate", 100), (Supplier, "supplier", 100), (TotAmount, "totamount", 100),
        (PreparedBy, "preparedby", 100), (CDateTime, "cdatetime", 100), (NonVatTotal, "nonvattotal", 100),
        (VatTy, "vatty", 100), (VatPercent, "vatpercent", 100), (Company, "company", 100),
    ];
}

/// <summary>The <c>po_l</c> legacy varchar columns written beside a new line's typed ones.</summary>
/// <remarks>
/// The line total column is <c>total</c> here (as on <c>quotation_l</c>; invoices call theirs <c>tot</c>).
/// The legacy <c>itemno</c> — a cart sequence number, never a real item — is left null: the new item
/// linkage is the typed <c>item_id</c>/<c>item_code</c> columns.
/// </remarks>
internal static class PurchaseOrderLineLegacyShadow
{
    public const string Pono = "pono";
    public const string Qty = "qty";
    public const string Rate = "rate";
    public const string Total = "total";

    public static readonly (string Name, string Column)[] All =
        [(Pono, "pono"), (Qty, "qty"), (Rate, "rate"), (Total, "total")];
}

/// <summary>
/// Purchase orders, mapped onto the adopted legacy <c>po_h</c> / <c>po_l</c> tables (Phase 6, slice 1).
/// </summary>
/// <remarks>
/// Additive adoption, exactly as invoices and quotations were (see <see cref="QuotationConfiguration"/>):
/// the typed properties map to <b>new</b> columns beside the legacy <c>varchar</c> ones, which the save
/// keeps in step for the surviving legacy readers (SearchPO's list and reprint). <c>company_id</c> already
/// exists (the multi-company migration added it); <c>po_no</c> and <c>desc</c> are shared columns and map
/// directly. Everything money or date, plus the supplier and item linkage, gets a new column — the legacy
/// <c>po_l</c> line was free text, so <c>item_id</c>/<c>item_code</c>/<c>cost</c> are all new.
/// </remarks>
public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("po_h");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        // Shared with the legacy app: the business number is the legacy column.
        builder.Property(p => p.Number).HasColumnName("po_no").HasMaxLength(100);

        // New, typed columns — the new app's source of truth; the legacy varchar columns sit beside them.
        builder.Property(p => p.CompanyId).HasColumnName("company_id");
        builder.Property(p => p.SupplierId).HasColumnName("supplier_id");
        builder.Property(p => p.Date).HasColumnName("po_date");
        builder.Property(p => p.PreparedBy).HasColumnName("prepared_by");

        builder.Property(p => p.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(18,4)");
        builder.Property(p => p.DiscountPercent).HasColumnName("discount_percent").HasColumnType("decimal(18,4)");
        builder.Property(p => p.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.NetTotal).HasColumnName("net_total").HasColumnType("decimal(18,4)");
        builder.Property(p => p.TaxRateId).HasColumnName("tax_rate_id");
        builder.Property(p => p.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasColumnType("decimal(18,4)");
        builder.Property(p => p.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.Total).HasColumnName("total_amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.Cost).HasColumnName("cost_amount").HasColumnType("decimal(18,4)");

        builder.Property(p => p.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        // Legacy shadow columns, written alongside on save so the surviving legacy readers see a whole row.
        // All nullable in po_h, so unlike invoice_h they do not gate the insert; they keep the reader whole.
        foreach (var (name, column, length) in PurchaseOrderLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        // Only this app's own POs; the existing legacy rows share the table but are never read as a
        // PurchaseOrder (their new typed columns are meaningless defaults).
        builder.HasQueryFilter(p => p.DataOrigin == "new" && p.DeletedAt == null);

        builder.HasMany(p => p.Lines)
            .WithOne(l => l.PurchaseOrder)
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("po_l");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.PurchaseOrderId).HasColumnName("purchase_order_id");
        builder.Property(l => l.ItemId).HasColumnName("item_id");

        // Shared with the legacy line: the description is the legacy `desc` (a text column, mapped as such
        // so nothing narrows and truncates an existing value). The item code has no legacy column on po_l
        // (the legacy line was free text), so it is a new column.
        builder.Property(l => l.Description).HasColumnName("desc").HasColumnType("text");
        builder.Property(l => l.ItemCode).HasColumnName("item_code").HasMaxLength(100);

        // The legacy line shadow columns (pono, qty, rate, total), written beside the typed ones.
        foreach (var (name, column) in PurchaseOrderLineLegacyShadow.All)
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

        builder.HasIndex(l => l.PurchaseOrderId);
    }
}

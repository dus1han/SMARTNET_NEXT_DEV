using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>invoice_h</c> legacy varchar columns the new save writes alongside its typed ones — declared
/// as EF shadow properties (not fields on the domain entity) and set by the save pipeline. One list, so
/// the mapping and the writing cannot drift apart. Property names match the column, except
/// <see cref="Cost"/>, whose column <c>cost</c> would collide with the entity's typed <c>Cost</c>.
/// </summary>
internal static class InvoiceLegacyShadow
{
    public const string It = "it";
    public const string InvType = "invtype";
    public const string InDate = "indate";
    public const string Customer = "customer";
    public const string TotAmount = "totamount";
    public const string Balance = "balance";
    public const string PreparedBy = "preparedby";
    public const string CDateTime = "cdatetime";
    public const string Cost = "legacyCost";
    public const string NoVatTotal = "novattotal";
    public const string VType = "vtype";
    public const string VPer = "vper";
    public const string DiscountPer = "discountper";
    public const string BeforeDiscTot = "beforedisctot";
    public const string Company = "company";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (It, "it", 100), (InvType, "invtype", 100), (InDate, "indate", 100), (Customer, "customer", 100),
        (TotAmount, "totamount", 100), (Balance, "balance", 100), (PreparedBy, "preparedby", 100),
        (CDateTime, "cdatetime", 100), (Cost, "cost", 100), (NoVatTotal, "novattotal", 100),
        (VType, "vtype", 100), (VPer, "vper", 100), (DiscountPer, "discountper", 50),
        (BeforeDiscTot, "beforedisctot", 100), (Company, "company", 100),
    ];
}

/// <summary>The <c>invoice_l</c> legacy varchar columns written beside a new line's typed ones.</summary>
internal static class InvoiceLineLegacyShadow
{
    public const string Inno = "inno";
    public const string Qty = "qty";
    public const string Rate = "rate";
    public const string Tot = "tot";

    public static readonly (string Name, string Column)[] All =
        [(Inno, "inno"), (Qty, "qty"), (Rate, "rate"), (Tot, "tot")];
}

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

        // --- Legacy shadow columns -----------------------------------------------------------------
        // The new app's source of truth is the decimal columns above; these legacy varchar columns are
        // written *alongside* on save so the still-live legacy readers (payments, job cards) and the new
        // app's own Phase 4 reports — which read invoice_h by its legacy column names — see a complete
        // row. Three of them (discountper, beforedisctot, contactperson) are NOT NULL, so a new invoice
        // must write them just as a legacy one did; the rest keep the reports whole. They are shadow
        // properties, not fields on the honestly-typed domain entity, set by the save pipeline. All go
        // at the Phase 9 retype. (contactperson is a real property above, set non-null by the pipeline.)
        foreach (var (name, column, length) in InvoiceLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

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

        // The legacy line shadow columns (inno, qty, rate, tot), written beside the typed ones so the
        // outstanding-detail report — which reads invoice_l — sees a new invoice's lines. All nullable,
        // so unlike the header these do not gate the insert; they are for the reader.
        foreach (var (name, column) in InvoiceLineLegacyShadow.All)
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

        // The line is read as part of its invoice — always by parent.
        builder.HasIndex(l => l.InvoiceId);
    }
}

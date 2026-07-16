using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The payables ledger — the one table the new app writes when a supplier's balance moves. The supply-side
/// mirror of <see cref="LedgerEntryConfiguration"/> (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// A genuinely new table, so its migration is EF's generated <c>CreateTable</c>, not a hand-written
/// adoption. No query filter: a ledger entry is never soft-deleted.
/// </remarks>
public class PayablesLedgerEntryConfiguration : IEntityTypeConfiguration<PayablesLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PayablesLedgerEntry> builder)
    {
        builder.ToTable("payables_ledger");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.SupplierId).HasColumnName("supplier_id");

        // The enum stored as its name, not its ordinal.
        builder.Property(e => e.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(e => e.SupplierInvoiceId).HasColumnName("supplier_invoice_id");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);

        builder.ConfigureAuditColumns();

        // Real foreign keys. Restrict, not cascade: a supplier or supplier invoice with ledger history
        // cannot be hard-deleted out from under it (and the app soft-deletes anyway). The invoice link is
        // nullable — an existing legacy invoice's for an opening balance, a new one's for a purchase.
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SupplierInvoice>()
            .WithMany()
            .HasForeignKey(e => e.SupplierInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.SupplierId);
    }
}

/// <summary>
/// The <c>supplier_invoice</c> legacy varchar columns the new save writes alongside its typed ones — EF
/// shadow properties set by the save pipeline, one list so the mapping and the writing cannot drift.
/// <c>invno</c> is <b>not</b> here — it maps to the real <see cref="SupplierInvoice.SupplierReference"/>
/// property, being a shared column. <c>paymentstat</c> is written <c>Pending</c> on create and flipped to
/// <c>Paid</c> by the payment path once the derived outstanding reaches zero.
/// </summary>
internal static class SupplierInvoiceLegacyShadow
{
    public const string Amount = "amount";
    public const string PaymentStat = "paymentstat";
    public const string InvDate = "invdate";
    public const string NoVatTotal = "novattotal";
    public const string VType = "vtype";
    public const string VPer = "vper";
    public const string SupCode = "supcode";
    public const string Company = "company";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (Amount, "amount", 100), (PaymentStat, "paymentstat", 100), (InvDate, "invdate", 100),
        (NoVatTotal, "novattotal", 100), (VType, "vtype", 100), (VPer, "vper", 100),
        (SupCode, "supcode", 100), (Company, "company", 100),
    ];
}

/// <summary>
/// Supplier invoices, mapped onto the adopted legacy <c>supplier_invoice</c> table (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// Additive adoption. Unlike <c>invoice_h</c>/<c>po_h</c>, this table already had an <c>id</c> column, so
/// the migration promotes it to a real <c>bigint</c> primary key rather than adding one. The typed columns
/// map to <b>new</b> columns beside the legacy <c>varchar</c> ones, which the save keeps in step for the
/// surviving legacy readers (the supplier reports). <c>company_id</c> already exists (multi-company
/// migration); <c>invno</c> is the shared reference column. Header-only — there is no line table.
/// </remarks>
public class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("supplier_invoice");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        // Shared with the legacy app: the supplier's own reference is the legacy invno column.
        builder.Property(s => s.SupplierReference).HasColumnName("invno").HasMaxLength(100);

        // New, typed columns — the new app's source of truth; the legacy varchar columns sit beside them.
        builder.Property(s => s.CompanyId).HasColumnName("company_id");
        builder.Property(s => s.SupplierId).HasColumnName("supplier_id");
        builder.Property(s => s.Date).HasColumnName("invoice_date");

        builder.Property(s => s.NetTotal).HasColumnName("net_total").HasColumnType("decimal(18,4)");
        builder.Property(s => s.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasColumnType("decimal(18,4)");
        builder.Property(s => s.Amount).HasColumnName("total_amount").HasColumnType("decimal(18,4)");
        builder.Ignore(s => s.TaxAmount); // derived (Amount − NetTotal), not stored

        builder.Property(s => s.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        // Legacy shadow columns, written alongside on save so the surviving legacy supplier reports read a
        // whole row. All nullable in supplier_invoice, so they keep the reader whole rather than gating the
        // insert. Shadow properties, not fields on the domain entity.
        foreach (var (name, column, length) in SupplierInvoiceLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        // Only this app's own supplier invoices; the existing legacy rows share the table but are never
        // read as a SupplierInvoice (their new typed columns are meaningless defaults).
        builder.HasQueryFilter(s => s.DataOrigin == "new" && s.DeletedAt == null);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// Supplier payments — a new concept (supplier money allocated across invoices), so new tables
/// (<c>supplier_payments</c>/<c>supplier_payment_allocations</c>), not a legacy adoption. The legacy
/// <c>supplier_inv_pay</c> table is the shadow the save dual-writes, not the source of truth (Phase 7).
/// </summary>
public class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("supplier_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CompanyId).HasColumnName("company_id");
        builder.Property(p => p.SupplierId).HasColumnName("supplier_id");
        builder.Property(p => p.Date).HasColumnName("payment_date");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.Method).HasColumnName("method").HasMaxLength(50);
        builder.Property(p => p.Reference).HasColumnName("reference").HasMaxLength(200);
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        builder.Property(p => p.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        builder.ConfigureAuditColumns();

        builder.HasQueryFilter(p => p.DataOrigin == "new" && p.DeletedAt == null);

        // A resubmit with the same key must not create a second payment.
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Allocations)
            .WithOne(a => a.Payment)
            .HasForeignKey(a => a.SupplierPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>One allocation of a supplier payment against a supplier invoice.</summary>
public class SupplierPaymentAllocationConfiguration : IEntityTypeConfiguration<SupplierPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<SupplierPaymentAllocation> builder)
    {
        builder.ToTable("supplier_payment_allocations");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.SupplierPaymentId).HasColumnName("supplier_payment_id");
        // A plain scalar link to supplier_invoice.id (new OR legacy) — the payables ledger already carries the
        // real reference, and a legacy invoice is in the same table but excluded by the SupplierInvoice filter.
        builder.Property(a => a.SupplierInvoiceId).HasColumnName("supplier_invoice_id");
        builder.Property(a => a.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();

        builder.HasIndex(a => a.SupplierPaymentId);
        builder.HasIndex(a => a.SupplierInvoiceId);
    }
}

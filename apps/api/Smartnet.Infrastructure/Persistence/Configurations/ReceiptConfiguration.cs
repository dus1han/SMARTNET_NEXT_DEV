using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// Customer receipts — a new concept (customer money allocated across invoices), so new tables
/// (<c>customer_receipts</c>/<c>receipt_allocations</c>), not a legacy adoption. The legacy <c>payments</c>
/// table is the shadow the save dual-writes, not the source of truth (Phase 7, slice 1).
/// </summary>
public class CustomerReceiptConfiguration : IEntityTypeConfiguration<CustomerReceipt>
{
    public void Configure(EntityTypeBuilder<CustomerReceipt> builder)
    {
        builder.ToTable("customer_receipts");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CompanyId).HasColumnName("company_id");
        builder.Property(r => r.CustomerId).HasColumnName("customer_id");
        builder.Property(r => r.Date).HasColumnName("receipt_date");
        builder.Property(r => r.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(r => r.Method).HasColumnName("method").HasMaxLength(50);
        builder.Property(r => r.Reference).HasColumnName("reference").HasMaxLength(200);
        builder.Property(r => r.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        builder.Property(r => r.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        builder.ConfigureAuditColumns();

        builder.HasQueryFilter(r => r.DataOrigin == "new" && r.DeletedAt == null);

        // A resubmit with the same key must not create a second receipt — the Finding-1 fix, enforced by the DB.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Allocations)
            .WithOne(a => a.Receipt)
            .HasForeignKey(a => a.CustomerReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>One allocation of a receipt against an invoice.</summary>
public class ReceiptAllocationConfiguration : IEntityTypeConfiguration<ReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<ReceiptAllocation> builder)
    {
        builder.ToTable("receipt_allocations");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CustomerReceiptId).HasColumnName("customer_receipt_id");
        // A plain scalar link to invoice_h.id (new OR legacy) — the receivables ledger already carries the
        // real FK to the invoice, and a legacy invoice is in the same table but excluded by the Invoice filter.
        builder.Property(a => a.InvoiceId).HasColumnName("invoice_id");
        builder.Property(a => a.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();

        builder.HasIndex(a => a.CustomerReceiptId);
        builder.HasIndex(a => a.InvoiceId);
    }
}

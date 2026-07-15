using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The receivables ledger — the one table the new app writes when a customer's balance moves (B3).
/// </summary>
/// <remarks>
/// A genuinely new table, so — unlike <c>invoice_h</c>/<c>invoice_l</c> — its migration is EF's
/// generated <c>CreateTable</c>, not a hand-written adoption. It has no query filter: a ledger entry is
/// never soft-deleted, so there is no <c>deleted_at</c> to filter on.
/// </remarks>
public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("receivables_ledger");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CustomerId).HasColumnName("customer_id");

        // The enum stored as its name, not its ordinal — "Payment" survives a reordering of the enum
        // where an integer would silently re-map.
        builder.Property(e => e.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(e => e.InvoiceId).HasColumnName("invoice_id");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);

        builder.ConfigureAuditColumns();

        // Real foreign keys — the joins the legacy schema never had. Restrict, not cascade: a customer
        // or invoice with ledger history cannot be hard-deleted out from under it (and the app
        // soft-deletes anyway). The invoice link is nullable and points at invoice_h.id — an existing
        // legacy invoice's for an opening balance, a new one's for a charge.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // The balance is always read for one customer: "what do they owe, and how did it get there?"
        builder.HasIndex(e => e.CustomerId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The suspended account's register — a table of the new app's own. The money itself moves in the
/// receivables ledger; this is the list of what was moved and whether it has come back.
/// </summary>
public class SuspendedReceivableConfiguration : IEntityTypeConfiguration<SuspendedReceivable>
{
    public void Configure(EntityTypeBuilder<SuspendedReceivable> builder)
    {
        builder.ToTable("suspended_receivables");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.CompanyId).HasColumnName("company_id");
        builder.Property(s => s.CustomerId).HasColumnName("customer_id");
        builder.Property(s => s.InvoiceId).HasColumnName("invoice_id");
        builder.Property(s => s.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(100).IsRequired();
        builder.Property(s => s.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(s => s.SuspendedOn).HasColumnName("suspended_on");
        builder.Property(s => s.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();

        // The enum stored as its name, like the ledger's type — survives a reordering.
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);

        builder.Property(s => s.ReinstatedAt).HasColumnName("reinstated_at");
        builder.Property(s => s.ReinstatedBy).HasColumnName("reinstated_by");
        builder.Property(s => s.ReinstateNote).HasColumnName("reinstate_note").HasMaxLength(500);
        builder.Property(s => s.ReceiptId).HasColumnName("receipt_id");

        builder.ConfigureAuditColumns();

        // "Is this invoice suspended?" is asked on every invoice read, and the account lists by status.
        builder.HasIndex(s => new { s.InvoiceId, s.Status });
        builder.HasIndex(s => new { s.Status, s.CompanyId });
        builder.HasIndex(s => s.CustomerId);
    }
}

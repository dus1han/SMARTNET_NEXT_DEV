using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Ledger;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>The general-ledger chart of accounts (GL slice 1) — new tables, per company.</summary>
public class GlAccountConfiguration : IEntityTypeConfiguration<GlAccount>
{
    public void Configure(EntityTypeBuilder<GlAccount> builder)
    {
        builder.ToTable("gl_accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CompanyId).HasColumnName("company_id");
        builder.Property(a => a.Code).HasColumnName("code").HasMaxLength(24);
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(a => a.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.IsCashOrBank).HasColumnName("is_cash_or_bank");

        builder.ConfigureAuditColumns();
        builder.HasQueryFilter(a => a.DeletedAt == null);

        // A code is unique within a company — the posting engine resolves an account by (company, code).
        // company_id is a plain scalar (no DB FK): companies_m.id is int, not bigint, and the app references
        // companies by company_id without a hard constraint everywhere.
        builder.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
    }
}

/// <summary>General-ledger journal entries (GL slice 2) — new tables, append-only.</summary>
public class GlEntryConfiguration : IEntityTypeConfiguration<GlEntry>
{
    public void Configure(EntityTypeBuilder<GlEntry> builder)
    {
        builder.ToTable("gl_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.Date).HasColumnName("entry_date");
        builder.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(32);
        builder.Property(e => e.SourceId).HasColumnName("source_id");
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);

        builder.ConfigureAuditColumns();

        // One event posts exactly once — the idempotency guard.
        builder.HasIndex(e => new { e.SourceType, e.SourceId }).IsUnique();
        builder.HasIndex(e => e.CompanyId);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Entry)
            .HasForeignKey(l => l.GlEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>The debit/credit lines of a journal entry.</summary>
public class GlLineConfiguration : IEntityTypeConfiguration<GlLine>
{
    public void Configure(EntityTypeBuilder<GlLine> builder)
    {
        builder.ToTable("gl_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.GlEntryId).HasColumnName("gl_entry_id");
        builder.Property(l => l.AccountId).HasColumnName("account_id");
        builder.Property(l => l.Debit).HasColumnName("debit").HasColumnType("decimal(18,4)");
        builder.Property(l => l.Credit).HasColumnName("credit").HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();

        builder.HasIndex(l => l.AccountId);

        builder.HasOne<GlAccount>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

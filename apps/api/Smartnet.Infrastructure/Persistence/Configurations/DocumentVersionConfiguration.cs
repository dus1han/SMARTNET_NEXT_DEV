using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Auditing;

namespace Smartnet.Infrastructure.Persistence.Configurations;

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.CompanyId).HasColumnName("company_id");

        builder.Property(e => e.DocType)
            .HasColumnName("doc_type")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.DocId).HasColumnName("doc_id").IsRequired();
        builder.Property(e => e.VersionNo).HasColumnName("version_no").IsRequired();

        builder.Property(e => e.Snapshot)
            .HasColumnName("snapshot")
            .HasColumnType("json")
            .IsRequired();

        builder.Property(e => e.ChangedBy).HasColumnName("changed_by");
        builder.Property(e => e.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);

        // Also the guard against two concurrent edits both claiming to be version 4.
        builder.HasIndex(e => new { e.DocType, e.DocId, e.VersionNo }).IsUnique();
    }
}

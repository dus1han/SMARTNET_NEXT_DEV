using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>documents</c> table (Phase 7, slice 4) — uploaded-file metadata, no bytes.
/// </summary>
/// <remarks>
/// A genuinely new table rather than an adoption, so there is no <c>data_origin</c> and no legacy shadow:
/// nothing in the legacy app reads it. The materialised <c>docstore</c> rows land here as ordinary rows
/// carrying <see cref="StoredDocument.LegacyDocstoreId"/>, which is what keeps that migration idempotent.
/// </remarks>
public sealed class StoredDocumentConfiguration : IEntityTypeConfiguration<StoredDocument>
{
    public void Configure(EntityTypeBuilder<StoredDocument> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.CompanyId).HasColumnName("company_id");
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
        builder.Property(d => d.OriginalFileName).HasColumnName("original_filename").HasMaxLength(255).IsRequired();

        // Generated, so the length is known: 32 hex characters plus an extension.
        builder.Property(d => d.StoredName).HasColumnName("stored_name").HasMaxLength(64).IsRequired();
        builder.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(128).IsRequired();
        builder.Property(d => d.ByteSize).HasColumnName("byte_size");
        builder.Property(d => d.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();

        builder.Property(d => d.EntityType).HasColumnName("entity_type").HasMaxLength(32);
        builder.Property(d => d.EntityId).HasColumnName("entity_id");
        builder.Property(d => d.LegacyDocstoreId).HasColumnName("legacy_docstore_id");

        builder.ConfigureAuditColumns();

        // The download path resolves a document by stored name; unique so it can never resolve to two.
        builder.HasIndex(d => d.StoredName).IsUnique();

        // The company library listing, and the attachment panel on a record.
        builder.HasIndex(d => d.CompanyId);
        builder.HasIndex(d => new { d.EntityType, d.EntityId });

        // Unique, so a concurrent second run of the legacy migration is refused by the database rather than
        // relying on the tool's own check having seen the first run's write.
        builder.HasIndex(d => d.LegacyDocstoreId).IsUnique();

        builder.HasQueryFilter(d => d.DeletedAt == null);
    }
}

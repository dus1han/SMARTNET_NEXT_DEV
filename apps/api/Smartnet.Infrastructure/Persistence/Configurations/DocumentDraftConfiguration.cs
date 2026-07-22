using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>document_drafts</c> table — unraised work on the four create screens.
/// </summary>
/// <remarks>
/// A genuinely new table, not an adoption: nothing in the legacy app reads it, and nothing here writes a
/// legacy one. See <see cref="DocumentDraft"/> for why a draft is not a row in <c>quotation_h</c> with a
/// status column.
/// <para>
/// <b>No <c>ConfigureAuditColumns</c>, and no query filter.</b> A draft is not <c>IAuditable</c> — autosave
/// would write an <c>audit_log</c> diff every few seconds — so the columns are mapped by hand here and
/// stamped by the controller. Nor is it soft-deleted, so there is no <c>deleted_at</c> to filter on.
/// <c>row_version</c> is still a concurrency token: shared drafts are the case it exists for.
/// </para>
/// </remarks>
public sealed class DocumentDraftConfiguration : IEntityTypeConfiguration<DocumentDraft>
{
    public void Configure(EntityTypeBuilder<DocumentDraft> builder)
    {
        builder.ToTable("document_drafts");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.DocType)
            .HasColumnName("doc_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.CompanyId).HasColumnName("company_id");

        // longtext: a 200-line invoice's state is tens of kilobytes, past what varchar can hold in a row.
        // The server never reads inside it, so it is stored as text rather than a JSON column — nothing
        // queries a field within it, and a JSON type would only invite something to start.
        builder.Property(d => d.Payload)
            .HasColumnName("payload")
            .HasColumnType("longtext")
            .IsRequired();

        builder.Property(d => d.PartyName)
            .HasColumnName("party_name")
            .HasMaxLength(DocumentDraft.MaxPartyNameLength);

        // The same precision the document tables use for money, so a draft total and the raised
        // document's total are the same number rather than one rounded on the way through.
        builder.Property(d => d.Total)
            .HasColumnName("total")
            .HasPrecision(18, 2);

        builder.Property(d => d.LineCount).HasColumnName("line_count");

        builder.Property(d => d.CreatedBy).HasColumnName("created_by");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.Property(d => d.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        // The only query the lists make: this company's drafts of one type, most recently touched first.
        builder.HasIndex(d => new { d.CompanyId, d.DocType, d.UpdatedAt });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Notes;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>user_notes</c> table (Phase 7, slice 5) — personal notes.
/// </summary>
/// <remarks>
/// A genuinely new table, not an adoption: no <c>data_origin</c> and no legacy shadow, because nothing
/// in the legacy app reads it.
/// <para>
/// <b>Not called <c>notes</c>.</b> That name belongs to a legacy table (49 rows) that stays exactly
/// where it is per LEGACY-DATA-POLICY. Nothing here dual-writes it: the legacy screen was one shared
/// textarea appending a full snapshot per save, which has no per-note, per-author shape to keep in step
/// with. Migrating that content is a manual exercise, deliberately — see MIGRATION-DATA-CHECKS.md.
/// </para>
/// </remarks>
public sealed class UserNoteConfiguration : IEntityTypeConfiguration<UserNote>
{
    public void Configure(EntityTypeBuilder<UserNote> builder)
    {
        builder.ToTable("user_notes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");

        builder.Property(n => n.CompanyId).HasColumnName("company_id");

        builder.Property(n => n.Title)
            .HasColumnName("title")
            .HasMaxLength(UserNote.MaxTitleLength)
            .IsRequired();

        builder.Property(n => n.Body)
            .HasColumnName("body")
            .HasMaxLength(UserNote.MaxBodyLength)
            .IsRequired();

        builder.ConfigureAuditColumns();

        // The only query the screen makes: this user's notes, newest first. created_by is the
        // visibility rule, so it is what the index is for.
        builder.HasIndex(n => n.CreatedBy);

        builder.HasQueryFilter(n => n.DeletedAt == null);
    }
}

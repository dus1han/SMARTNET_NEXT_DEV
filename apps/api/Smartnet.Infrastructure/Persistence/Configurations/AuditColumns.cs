using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Auditing;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The seven audit columns, mapped once.
/// </summary>
/// <remarks>
/// Every auditable entity carries the same seven, and every configuration that spelled them out by
/// hand was one more chance to forget <c>IsConcurrencyToken()</c> on <c>row_version</c> — which is
/// not a cosmetic omission. Without it, two people editing the same customer are back to the legacy
/// behaviour: the second write silently wins, and the first person's change is gone with no error and
/// no trace.
/// </remarks>
public static class AuditColumns
{
    public static EntityTypeBuilder<TEntity> ConfigureAuditColumns<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IAuditable
    {
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        // The concurrency token. The SaveChanges interceptor increments it; the database checks it.
        builder.Property(e => e.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        return builder;
    }
}

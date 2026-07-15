using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Auditing;

namespace Smartnet.Infrastructure.Persistence.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.CompanyId).HasColumnName("company_id");

        builder.Property(e => e.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(64)
            .IsRequired();

        // Stored as the enum's name, not its ordinal: an audit log you have to decode against a
        // C# enum's declaration order is not readable evidence, and reordering the enum would
        // silently rewrite history.
        builder.Property(e => e.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.ChangedBy).HasColumnName("changed_by");
        builder.Property(e => e.ChangedAt).HasColumnName("changed_at").IsRequired();

        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);

        builder.Property(e => e.Changes).HasColumnName("changes").HasColumnType("json");

        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(255);
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(36);

        // The three questions the log actually gets asked: what happened to this record,
        // what has this user done, and what happened in this company in this period.
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.ChangedAt });
        builder.HasIndex(e => new { e.ChangedBy, e.ChangedAt });
        builder.HasIndex(e => new { e.CompanyId, e.ChangedAt });
    }
}

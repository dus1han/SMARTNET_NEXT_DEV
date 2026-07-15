using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Identity;

namespace Smartnet.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("user_m");

        // The legacy table has `id` as AUTO_INCREMENT but under a *non-unique* index, not a
        // primary key — one of the 46 keyless tables in Finding 6. The migration promotes it to
        // a real primary key, which EF requires in order to write to the table at all, and
        // which the audit log requires in order to name the row it is describing.
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.Username).HasColumnName("username").HasMaxLength(100);
        builder.Property(u => u.Name).HasColumnName("name").HasMaxLength(100);

        builder.Property(u => u.LegacyPassword).HasColumnName("password").HasMaxLength(100);

        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);
        builder.Property(u => u.PasswordChangedAt).HasColumnName("password_changed_at");
        builder.Property(u => u.MustChangePassword).HasColumnName("must_change_password");
        builder.Property(u => u.FailedLoginCount).HasColumnName("failed_login_count");
        builder.Property(u => u.LockedUntil).HasColumnName("locked_until");

        builder.Property(u => u.Utype).HasColumnName("utype").HasMaxLength(100);
        builder.Property(u => u.Cuscode).HasColumnName("cuscode").HasMaxLength(100);
        builder.Property(u => u.Ustat).HasColumnName("ustat").HasMaxLength(100);
        builder.Property(u => u.Addedby).HasColumnName("addedby").HasMaxLength(100);

        builder.Property(u => u.CreatedBy).HasColumnName("created_by");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedBy).HasColumnName("updated_by");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.DeletedBy).HasColumnName("deleted_by");
        builder.Property(u => u.DeletedAt).HasColumnName("deleted_at");

        builder.Property(u => u.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        // Login is a lookup by username on every single request that authenticates. The legacy
        // table has no index on it at all.
        builder.HasIndex(u => u.Username);
    }
}

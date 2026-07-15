using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Identity;

namespace Smartnet.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CompanyId).HasColumnName("company_id");
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(r => r.Description).HasColumnName("description").HasMaxLength(255);
        builder.Property(r => r.IsSystem).HasColumnName("is_system");

        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.DeletedBy).HasColumnName("deleted_by");
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");
        builder.Property(r => r.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        // A company cannot have two roles called "Sales". Global roles (company_id NULL) are
        // unique by name across the system.
        builder.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique();

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(p => p.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.RoleId).HasColumnName("role_id");
        builder.Property(p => p.Permission).HasColumnName("permission").HasMaxLength(64).IsRequired();

        // Granting the same permission twice is not more granted.
        builder.HasIndex(p => new { p.RoleId, p.Permission }).IsUnique();
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.RoleId).HasColumnName("role_id");
        builder.Property(r => r.CompanyId).HasColumnName("company_id");

        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.DeletedBy).HasColumnName("deleted_by");
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");
        builder.Property(r => r.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        builder.HasIndex(r => new { r.UserId, r.RoleId, r.CompanyId }).IsUnique();

        // Deleted assignments are soft-deleted and must not silently keep granting access.
        builder.HasQueryFilter(r => r.DeletedAt == null);
    }
}

public class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> builder)
    {
        builder.ToTable("user_permission_overrides");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.UserId).HasColumnName("user_id");
        builder.Property(o => o.Permission).HasColumnName("permission").HasMaxLength(64).IsRequired();
        builder.Property(o => o.Granted).HasColumnName("granted");

        builder.Property(o => o.CreatedBy).HasColumnName("created_by");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedBy).HasColumnName("updated_by");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.DeletedBy).HasColumnName("deleted_by");
        builder.Property(o => o.DeletedAt).HasColumnName("deleted_at");
        builder.Property(o => o.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        builder.HasIndex(o => new { o.UserId, o.Permission }).IsUnique();

        builder.HasQueryFilter(o => o.DeletedAt == null);
    }
}

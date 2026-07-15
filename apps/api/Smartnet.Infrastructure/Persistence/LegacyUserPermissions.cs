using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Identity;

namespace Smartnet.Infrastructure.Persistence;

/// <summary>
/// The legacy <c>user_permissions</c> table — 35 varchar columns holding "1" or "0".
/// </summary>
/// <remarks>
/// Mapped as an EF <b>property-bag entity</b> rather than a class with 35 hand-written string
/// properties. The columns are generated from <see cref="Permissions.LegacyPermissions"/>, so the
/// mapping and the permission catalogue are physically the same list and cannot drift apart —
/// which a 35-property class would eventually do, one forgotten column at a time.
///
/// <para>This table is a <b>projection</b>, not a source of truth. The new app's roles and
/// overrides decide who may do what; this table exists solely so the legacy app, which is still
/// live and still reads it, keeps working. It is dropped in Phase 9 with the rest of it.</para>
///
/// <para>It is deliberately not <c>IAuditable</c>: the audited change is the one made to
/// user_roles or user_permission_overrides. Auditing the projection as well would record every
/// permission change twice and imply the legacy table was edited directly, which it never is.</para>
/// </remarks>
public static class LegacyUserPermissions
{
    /// <summary>The EF shared-type entity name. Used to reach the set: <c>db.Set&lt;...&gt;(EntityName)</c>.</summary>
    public const string EntityName = "legacy_user_permissions";

    public const string UserIdColumn = "user_id";

    /// <summary>The legacy app writes "1" and "0" as varchar. Not booleans, not tinyints.</summary>
    public const string Granted = "1";
    public const string Denied = "0";

    public static void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName, builder =>
        {
            builder.ToTable("user_permissions");

            // varchar in the legacy schema, holding "1" / "2" — the user_m id as a string.
            builder.Property<string>(UserIdColumn).HasColumnName(UserIdColumn).HasMaxLength(100);
            builder.HasKey(UserIdColumn);

            foreach (var permission in Permissions.LegacyPermissions)
            {
                builder.Property<string>(permission)
                    .HasColumnName(permission)
                    .HasMaxLength(100)
                    .IsRequired();
            }
        });
}

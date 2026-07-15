using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Identity;

/// <summary>
/// A named bundle of permissions.
/// </summary>
/// <remarks>
/// The legacy app had no roles — it had 35 checkboxes per user, ticked by hand, with no way to
/// say "another storeman, same as the last one". Roles exist so that adding a person is a
/// decision about their job rather than 35 separate decisions about screens.
/// </remarks>
public class Role : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>
    /// Null means the role is global — it exists in every company. <c>Dev_Admin</c> is global by
    /// definition; a business role like "Sales" belongs to one company.
    /// </summary>
    public long? CompanyId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// A role the application depends on. <c>Dev_Admin</c> and <c>Company_Admin</c> cannot be
    /// deleted or emptied through the UI — otherwise one misclick locks everybody out of the
    /// screens needed to undo it.
    /// </summary>
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = [];

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }

    public const string DevAdmin = "Dev_Admin";
    public const string CompanyAdmin = "Company_Admin";
}

/// <summary>
/// One permission granted to a role. Presence is the grant — there is no <c>granted = false</c>
/// row, because a role that both grants and denies the same permission is a question with no
/// good answer.
/// </summary>
public class RolePermission
{
    public long Id { get; set; }
    public long RoleId { get; set; }
    public string Permission { get; set; } = null!;
}

/// <summary>Assigns a role to a user, within a company.</summary>
public class UserRole : IAuditable
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long RoleId { get; set; }

    /// <summary>
    /// The company this assignment applies in. Null for a global role such as
    /// <see cref="Role.DevAdmin"/>, whose whole point is that it is not confined to one company.
    /// </summary>
    public long? CompanyId { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>
/// A per-user exception to what their roles grant.
/// </summary>
/// <remarks>
/// There is always one person who needs one extra thing, and the alternative to modelling that is
/// a proliferation of near-identical roles ("Sales", "Sales but can also see cheques"). An
/// override can add a permission or take one away, and unlike the legacy checkboxes it is
/// visible, attributable and audited.
/// <para>
/// Overrides are the exception. If a role has three of them, it wanted to be a different role.
/// </para>
/// </remarks>
public class UserPermissionOverride : IAuditable
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Permission { get; set; } = null!;

    /// <summary>True adds the permission; false removes one the user's roles would have granted.</summary>
    public bool Granted { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

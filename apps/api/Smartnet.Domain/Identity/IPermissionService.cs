namespace Smartnet.Domain.Identity;

public interface IPermissionService
{
    /// <summary>
    /// What this user may actually do: everything their roles grant, with their personal
    /// overrides applied on top.
    /// </summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Only what this user's <b>roles</b> grant — before their personal overrides are applied.
    /// </summary>
    /// <remarks>
    /// The permissions editor needs this to be authoritative without touching roles: to make a
    /// user's effective permissions equal exactly the boxes an administrator ticked, a permission a
    /// role grants but the administrator unticked has to be explicitly <i>denied</i> by an override,
    /// and a permission no role grants but they ticked has to be explicitly <i>granted</i>. Knowing
    /// which is which is the difference between "add an override" and "remove a redundant one".
    /// </remarks>
    Task<IReadOnlySet<string>> GetRoleGrantedPermissionsAsync(
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the user's effective permissions and writes them back to the legacy
    /// <c>user_permissions</c> table, so the old app keeps agreeing with the new one.
    /// </summary>
    /// <remarks>
    /// Called after every change to a user's roles or overrides. Deleted in Phase 9, when the
    /// legacy app stops reading that table.
    /// </remarks>
    Task SyncToLegacyAsync(long userId, CancellationToken cancellationToken = default);
}

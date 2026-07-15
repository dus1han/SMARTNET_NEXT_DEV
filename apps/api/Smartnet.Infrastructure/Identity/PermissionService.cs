using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Identity;

/// <inheritdoc cref="IPermissionService"/>
public sealed class PermissionService : IPermissionService
{
    private readonly SmartnetDbContext _db;

    public PermissionService(SmartnetDbContext db) => _db = db;

    public async Task<IReadOnlySet<string>> GetRoleGrantedPermissionsAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        // The query filter on user_roles already excludes soft-deleted assignments, so a revoked
        // role stops granting immediately.
        var granted = await _db.UserRoles
            .Where(assignment => assignment.UserId == userId)
            .Join(
                _db.Roles.Where(role => role.DeletedAt == null),
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Id)
            .Join(
                _db.RolePermissions,
                roleId => roleId,
                permission => permission.RoleId,
                (_, permission) => permission.Permission)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(granted, StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        // What the user's roles grant, before overrides.
        var granted = await GetRoleGrantedPermissionsAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        var effective = new HashSet<string>(granted, StringComparer.Ordinal);

        // Then the personal exceptions, applied on top. An override can add a permission the
        // user's roles do not grant, or take away one that they do.
        var overrides = await _db.UserPermissionOverrides
            .Where(o => o.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var over in overrides)
        {
            if (over.Granted)
            {
                effective.Add(over.Permission);
            }
            else
            {
                // A revocation wins over any role that grants it. The narrower, more deliberate
                // statement about this one person beats the broader one about their job.
                effective.Remove(over.Permission);
            }
        }

        // Defence in depth: a permission that is no longer in the catalogue — renamed, retired —
        // must not survive in the database and keep granting something nothing enforces.
        effective.RemoveWhere(p => !Permissions.IsKnown(p));

        return effective;
    }

    public async Task SyncToLegacyAsync(long userId, CancellationToken cancellationToken = default)
    {
        var effective = await GetEffectivePermissionsAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        var key = userId.ToString(CultureInfo.InvariantCulture);

        var set = _db.Set<Dictionary<string, object>>(LegacyUserPermissions.EntityName);

        var row = await set
            .FirstOrDefaultAsync(
                r => (string)r[LegacyUserPermissions.UserIdColumn] == key,
                cancellationToken)
            .ConfigureAwait(false);

        var isNew = row is null;

        row ??= new Dictionary<string, object>
        {
            [LegacyUserPermissions.UserIdColumn] = key,
        };

        // Every legacy column is written on every sync — including the ones being turned OFF.
        // Writing only the grants would leave a revoked permission sitting at "1" in the table
        // the legacy app actually reads, so the user would keep the access in the old app after
        // losing it in the new one. That is the whole bug this method exists to not have.
        foreach (var permission in Permissions.LegacyPermissions)
        {
            row[permission] = effective.Contains(permission)
                ? LegacyUserPermissions.Granted
                : LegacyUserPermissions.Denied;
        }

        if (isNew)
        {
            set.Add(row);
        }
        else
        {
            set.Update(row);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

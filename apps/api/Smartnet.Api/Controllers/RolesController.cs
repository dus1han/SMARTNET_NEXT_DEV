using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

[ApiController]
[Route("api/roles")]
[RequirePermission(Permissions.RolesManage)]
public sealed class RolesController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly IPermissionService _permissions;

    public RolesController(SmartnetDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleSummary>>> List(CancellationToken cancellationToken) =>
        Ok(await _db.Roles
            .Where(role => role.DeletedAt == null)
            .OrderByDescending(role => role.IsSystem)
            .ThenBy(role => role.Name)
            .Select(role => new RoleSummary(
                role.Id,
                role.Name,
                role.Description,
                role.IsSystem,
                role.CompanyId,
                role.Permissions.Select(p => p.Permission).ToList(),
                role.RowVersion))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

    /// <summary>The permissions that exist, so the role editor is not a list of magic strings.</summary>
    /// <remarks>
    /// Includes <see cref="Permissions.SystemDevAdmin"/> — shown as "Developer", and the grant that unlocks
    /// the Companies and VAT rate screens. It is offered like any other permission but guarded like none of
    /// them: only a Dev_Admin may hand it out (see <c>CanGrant</c>), because a permission that satisfies
    /// every policy must not be reachable by an administrator working down a checklist.
    /// </remarks>
    [HttpGet("permissions")]
    public ActionResult<IReadOnlyList<PermissionCatalogueEntry>> Catalogue() =>
        Ok(Permissions.All
            .Select(p => new PermissionCatalogueEntry(p, Permissions.IsLegacy(p)))
            .ToList());

    [HttpPost]
    [RequireChangeReason]
    public async Task<ActionResult<RoleSummary>> Create(
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (await NameTaken(request.Name, request.CompanyId, null, cancellationToken).ConfigureAwait(false))
        {
            return Conflict(Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"A role called '{request.Name}' already exists here."));
        }

        if (!CanGrant(request.Permissions, out var refusal))
        {
            return refusal;
        }

        var role = new Role
        {
            Name = request.Name,
            Description = request.Description,
            CompanyId = request.CompanyId,
            IsSystem = false,
            Permissions = [.. request.Permissions.Distinct(StringComparer.Ordinal)
                .Select(p => new RolePermission { Permission = p })],
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new RoleSummary(
            role.Id, role.Name, role.Description, role.IsSystem, role.CompanyId,
            [.. role.Permissions.Select(p => p.Permission)],
            role.RowVersion));
    }

    [HttpPut("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Update(
        long id,
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystem)
        {
            // Dev_Admin and Company_Admin are what the administration screens are reached through.
            // Emptying one of them is a decision that cannot then be undone through the UI that it
            // just locked everybody out of.
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"'{role.Name}' is a system role and its permissions cannot be changed.");
        }

        // A role is a set of permissions held by everyone in it, so two administrators saving at once
        // does not lose one edit — it silently re-grants, or revokes, across every user in the role.
        if (this.StaleEdit(role, request.ExpectedRowVersion, "role") is { } stale)
        {
            return stale;
        }

        if (!CanGrant(request.Permissions, out var refusal))
        {
            return refusal;
        }

        if (await NameTaken(request.Name, request.CompanyId, id, cancellationToken).ConfigureAwait(false))
        {
            return Conflict(Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"A role called '{request.Name}' already exists here."));
        }

        role.Name = request.Name;
        role.Description = request.Description;
        role.CompanyId = request.CompanyId;

        var wanted = request.Permissions.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        foreach (var removed in role.Permissions.Where(p => !wanted.Contains(p.Permission)).ToList())
        {
            role.Permissions.Remove(removed);
        }

        var held = role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);

        foreach (var added in wanted.Where(p => !held.Contains(p)))
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = added });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Everyone holding this role can now do something different, so the legacy app — which
        // reads its own table, not ours — has to be brought back into agreement.
        var holders = await _db.UserRoles
            .Where(assignment => assignment.RoleId == id)
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var userId in holders)
        {
            await _permissions.SyncToLegacyAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var role = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystem)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"'{role.Name}' is a system role and cannot be deleted.");
        }

        var holders = await _db.UserRoles
            .Where(assignment => assignment.RoleId == id)
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (holders.Count > 0)
        {
            // Deleting it out from under them would silently strip their access, and the audit row
            // would say "role deleted" rather than "these five people lost these permissions".
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{holders.Count} user(s) still hold this role. Reassign them first.");
        }

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// You cannot give away what you do not have.
    /// </summary>
    /// <remarks>
    /// Without this, <c>roles.manage</c> is a backdoor to everything: a user who can edit roles
    /// simply writes <c>system.dev_admin</c> into a role and assigns it to themselves. Privilege
    /// escalation by the front door, exactly the shape of A5.
    /// </remarks>
    private bool CanGrant(string[] permissions, out ActionResult refusal)
    {
        var isDevAdmin = User.HasClaim(SmartnetClaims.Permission, Permissions.SystemDevAdmin);

        if (!isDevAdmin && permissions.Contains(Permissions.SystemDevAdmin, StringComparer.Ordinal))
        {
            refusal = Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Only a Dev_Admin may grant the Dev_Admin permission.");

            return false;
        }

        // A role may grant one dashboard or neither, never both.
        //
        // The same rule is enforced on a user's own permissions and was not enforced here, which is how
        // the seeded system roles came to hold the pair: seeding used Permissions.All, and nothing
        // objected. A user takes the union of their roles, so a role holding both hands them both — and
        // the permissions dialog, where the two are radio buttons, then shows the operations dashboard
        // selected for an administrator.
        //
        // Neither is allowed here, unlike on a user, because a role is a building block: "everything
        // except a dashboard" composes with a role that supplies one. It is a user who must end up with
        // exactly one, and UsersController is where that is checked.
        if (Permissions.DashboardPermissions.Count(permissions.Contains) > 1)
        {
            refusal = Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A role grants one dashboard, not both. The operations dashboard is not a subset of the management one.");

            return false;
        }

        refusal = null!;
        return true;
    }

    /// <remarks>
    /// Checked here as well as by the unique index, because MySQL treats NULLs as distinct: the
    /// index on (company_id, name) does NOT stop two global roles both called "Sales".
    /// </remarks>
    private Task<bool> NameTaken(string name, long? companyId, long? excluding, CancellationToken cancellationToken) =>
        _db.Roles.AnyAsync(
            r => r.Name == name
                 && r.CompanyId == companyId
                 && r.DeletedAt == null
                 && (excluding == null || r.Id != excluding),
            cancellationToken);
}

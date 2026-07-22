using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Exporting;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// User administration — the exact surface the legacy app left unguarded.
/// </summary>
/// <remarks>
/// <c>ManageUserController.getUserPer</c> and <c>updatepermission</c> were callable by any
/// logged-in user, including a customer-type account (ISSUES A5). Every endpoint here declares the
/// permission it requires, and the ones that change what someone can do also demand a written
/// reason.
/// </remarks>
[ApiController]
[Route("api/users")]
[RequirePermission(Permissions.Users)]
public sealed class UsersController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IPasswordHasher _hasher;
    private readonly IExcelExporter _excel;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public UsersController(
        SmartnetDbContext db,
        IPermissionService permissions,
        IPasswordHasher hasher,
        IExcelExporter excel,
        IAuditWriter audit,
        TimeProvider time)
    {
        _db = db;
        _permissions = permissions;
        _hasher = hasher;
        _excel = excel;
        _audit = audit;
        _time = time;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> List(CancellationToken cancellationToken)
    {
        var users = await _db.Users
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = new List<UserSummary>(users.Count);

        foreach (var user in users)
        {
            summaries.Add(await Summarise(user, cancellationToken).ConfigureAwait(false));
        }

        return Ok(summaries);
    }

    /// <summary>
    /// The list, as a real .xlsx workbook.
    /// </summary>
    /// <remarks>
    /// Generated on the server, not in the browser. The values here are already typed — a count is
    /// an <c>int</c>, a date is a <c>DateTime</c>, and in later phases a total is a <c>decimal</c> —
    /// and they go into the workbook as numeric cells. Rebuilt in the browser from JSON they would
    /// be strings that had been re-parsed in whatever locale the user's machine runs, and a money
    /// column that lands in Excel as text cannot be summed. The first thing anybody does with an
    /// exported list of invoices is total it.
    ///
    /// <para>Exporting is also an <b>audited</b> action (AUDIT.md §3). "Who exported the customer
    /// list?" is a question that currently has no answer at all.</para>
    /// </remarks>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var users = await _db.Users
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = new List<UserSummary>(users.Count);

        foreach (var user in users)
        {
            summaries.Add(await Summarise(user, cancellationToken).ConfigureAwait(false));
        }

        var workbook = _excel.Export<UserSummary>(
            "Users",
            [
                new("Username", u => u.Username),
                new("Name", u => u.Name),
                new("Roles", u => string.Join(", ", u.Roles.Select(r => r.Name))),
                new("Permissions", u => u.EffectivePermissions.Count, ExcelFormat.WholeNumber),
                new("Disabled", u => u.IsDisabled, ExcelFormat.Boolean),
                new("Locked out", u => u.IsLockedOut, ExcelFormat.Boolean),
                new("Must change password", u => u.MustChangePassword, ExcelFormat.Boolean),
            ],
            summaries);

        await _audit.RecordAsync(
            AuditAction.Export,
            nameof(User),
            "*",
            details: new { list = "users", rows = summaries.Count },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"users-{_time.GetUtcNow():yyyy-MM-dd}.xlsx");
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<UserSummary>> Get(long id, CancellationToken cancellationToken)
    {
        var user = await Find(id, cancellationToken).ConfigureAwait(false);

        return user is null
            ? NotFound()
            : Ok(await Summarise(user, cancellationToken).ConfigureAwait(false));
    }

    [HttpPost]
    public async Task<ActionResult<CreateUserResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var taken = await _db.Users
            .AnyAsync(u => u.Username == request.Username, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Conflict(Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"The username '{request.Username}' is already in use."));
        }

        var temporary = TemporaryPassword.Generate();

        var user = new User
        {
            Username = request.Username,
            Name = request.Name,
            PasswordHash = _hasher.Hash(temporary),

            // The legacy app authenticates against the plaintext column, so a user created here
            // must also be able to log into the old app until Phase 9 retires it.
            LegacyPassword = temporary,

            // The temporary password is known to whoever created the account. It is not a secret
            // between the user and the system until they have replaced it.
            MustChangePassword = true,

            Ustat = "Active",
            Addedby = $"{User.Identity?.Name} : {_time.GetUtcNow().UtcDateTime:yyyy-MM-dd HH:mm:ss}",
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AssignRoles(user.Id, request.RoleIds, cancellationToken).ConfigureAwait(false);

        // Shown once, and never again: it is not stored anywhere in retrievable form.
        return Ok(new CreateUserResponse(user.Id, temporary));
    }

    [HttpPut("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Update(
        long id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await Find(id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        // Before anything is written, and before the roles are reassigned below. This request replaces
        // the user's whole role set, so a stale one does not just lose a name change — it can put back a
        // role another administrator has just taken away, which is a privilege the person should not have.
        if (this.StaleEdit(user, request.ExpectedRowVersion, "user") is { } stale)
        {
            return stale;
        }

        user.Name = request.Name;

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title:
                    "Someone else changed this user while you were editing it. Reload to see their "
                    + "version, then make your changes again.");
        }

        await AssignRoles(id, request.RoleIds, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>Soft delete. The user's history stays attributable — see AUDIT.md.</summary>
    [HttpDelete("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Disable(long id, CancellationToken cancellationToken)
    {
        var user = await Find(id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        if (id == CurrentUserId)
        {
            // Not paternalism: an administrator who disables themselves may be the only person who
            // could re-enable them.
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "You cannot disable your own account.");
        }

        // The interceptor turns this into a soft delete and audits it, with the reason attached.
        _db.Users.Remove(user);

        // The legacy app has no soft delete; ustat is the only thing it understands.
        user.Ustat = "Inactive";

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    [HttpPost("{id:long}/reset-password")]
    [RequireChangeReason]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(
        long id,
        CancellationToken cancellationToken)
    {
        var user = await Find(id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        var temporary = TemporaryPassword.Generate();

        user.PasswordHash = _hasher.Hash(temporary);
        user.LegacyPassword = temporary;
        user.MustChangePassword = true;
        user.PasswordChangedAt = _time.GetUtcNow().UtcDateTime;

        // A reset is also the way out of a lockout.
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ResetPasswordResponse(temporary));
    }

    /// <summary>Adds or removes a single permission for one user, as an exception to their roles.</summary>
    [HttpPut("{id:long}/overrides")]
    [RequireChangeReason]
    public async Task<IActionResult> SetOverride(
        long id,
        SetOverrideRequest request,
        CancellationToken cancellationToken)
    {
        var user = await Find(id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        if (this.StaleEdit(user, request.ExpectedRowVersion, "user") is { } stale)
        {
            return stale;
        }

        // The override rows are not on the user, so nothing here would move the user's version and the
        // check above would pass straight through the next concurrent edit. See TouchForConcurrency.
        user.TouchForConcurrency(_time);

        var existing = await _db.UserPermissionOverrides
            .FirstOrDefaultAsync(
                o => o.UserId == id && o.Permission == request.Permission,
                cancellationToken)
            .ConfigureAwait(false);

        if (request.Granted is null)
        {
            // Null clears the exception: the user goes back to whatever their roles say.
            if (existing is not null)
            {
                _db.UserPermissionOverrides.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = id,
                Permission = request.Permission,
                Granted = request.Granted.Value,
            });
        }
        else
        {
            existing.Granted = request.Granted.Value;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // What the user can do has changed, so the legacy app must be told.
        await _permissions.SyncToLegacyAsync(id, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Sets a user's permissions directly — the whole set, in one go.
    /// </summary>
    /// <remarks>
    /// Permission assignment without roles. The request carries exactly the permissions this person
    /// should have; the server makes their effective set equal it, using overrides as the lever:
    /// grant what a role does not already provide, deny what a role provides but the list omits, and
    /// drop any override that has become redundant. Roles are left untouched — the two system
    /// administrators still get their access that way — but for an ordinary user this is the only
    /// screen anybody needs.
    /// <para>
    /// One transaction and one write-through, so the user's access in both apps changes together or
    /// not at all. And <c>system.dev_admin</c> may only be granted by a Dev_Admin, exactly as when it
    /// is granted through a role.
    /// </para>
    /// </remarks>
    [HttpPut("{id:long}/permissions")]
    [RequireChangeReason]
    public async Task<IActionResult> SetPermissions(
        long id,
        SetUserPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var user = await Find(id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        // Before any of the validation below, so a stale request is refused on the grounds that matter
        // rather than on whichever rule it happens to trip first.
        if (this.StaleEdit(user, request.ExpectedRowVersion, "user") is { } stale)
        {
            return stale;
        }

        // A permission set lives in override rows and roles, not in any column of the user — so without
        // this the user's version would not move and the next concurrent edit would sail past the check
        // above, silently reinstating a permission somebody had just revoked. See TouchForConcurrency.
        user.TouchForConcurrency(_time);

        var desired = new HashSet<string>(request.Permissions, StringComparer.Ordinal);

        // A permission nobody can enforce is not a permission. Reject the whole request rather than
        // silently dropping the unknown one, so a typo in a caller is a 400, not a quiet no-op.
        var unknown = desired.FirstOrDefault(p => !Permissions.IsKnown(p));

        if (unknown is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"'{unknown}' is not a known permission.");
        }

        // Exactly one dashboard. Neither leaves the user landing on a page they cannot load; both is a
        // contradiction rather than a superset, since the operations dashboard is defined by what it
        // withholds. Refused here rather than resolved silently: which dashboard somebody gets is a
        // decision, and guessing it is how a clerk ends up looking at the margin.
        var dashboards = Permissions.DashboardPermissions.Where(desired.Contains).ToList();

        if (dashboards.Count != 1)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: dashboards.Count == 0
                    ? "A user needs one dashboard — either the management or the operations one."
                    : "A user gets one dashboard, not both. The operations dashboard is not a subset of the management one.");
        }

        // The superuser bit is not something a Company_Admin can hand out — to anyone, by any route.
        if (desired.Contains(Permissions.SystemDevAdmin) && !CurrentUserIsDevAdmin)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Only a Dev_Admin may grant the Dev_Admin permission.");
        }

        var roleGranted = await _permissions
            .GetRoleGrantedPermissionsAsync(id, cancellationToken)
            .ConfigureAwait(false);

        var existing = await _db.UserPermissionOverrides
            .Where(o => o.UserId == id)
            .ToDictionaryAsync(o => o.Permission, o => o, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var permission in Permissions.All)
        {
            var wanted = desired.Contains(permission);
            var byRole = roleGranted.Contains(permission);
            existing.TryGetValue(permission, out var over);

            // The override needed to force `wanted`, given what the roles already do:
            //   want + role   → nothing (the role provides it)
            //   want + no role→ grant
            //   drop + role   → deny (the override overrules the role)
            //   drop + no role→ nothing
            bool? required = wanted == byRole ? null : wanted;

            if (required is null)
            {
                if (over is not null)
                {
                    _db.UserPermissionOverrides.Remove(over);
                }
            }
            else if (over is null)
            {
                _db.UserPermissionOverrides.Add(new UserPermissionOverride
                {
                    UserId = id,
                    Permission = permission,
                    Granted = required.Value,
                });
            }
            else if (over.Granted != required.Value)
            {
                over.Granted = required.Value;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Effective permissions changed, so the legacy app's flags must be recomputed to match.
        await _permissions.SyncToLegacyAsync(id, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<User?> Find(long id, CancellationToken cancellationToken) => _db.Users
        .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    private async Task AssignRoles(long userId, long[] roleIds, CancellationToken cancellationToken)
    {
        var current = await _db.UserRoles
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var assignment in current.Where(a => !roleIds.Contains(a.RoleId)))
        {
            _db.UserRoles.Remove(assignment);
        }

        var existing = current.Select(a => a.RoleId).ToHashSet();

        foreach (var roleId in roleIds.Where(id => !existing.Contains(id)))
        {
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The permission change is only half-applied until the legacy app agrees with it.
        await _permissions.SyncToLegacyAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserSummary> Summarise(User user, CancellationToken cancellationToken)
    {
        var roles = await _db.UserRoles
            .Where(assignment => assignment.UserId == user.Id)
            .Join(_db.Roles, a => a.RoleId, r => r.Id, (_, role) => role)
            .Select(role => new RoleSummary(
                role.Id,
                role.Name,
                role.Description,
                role.IsSystem,
                role.CompanyId,
                role.Permissions.Select(p => p.Permission).ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var effective = await _permissions
            .GetEffectivePermissionsAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return new UserSummary(
            user.Id,
            user.Username ?? string.Empty,
            user.Name ?? string.Empty,
            user.IsDisabled,
            user.MustChangePassword,
            user.IsLockedOut(_time.GetUtcNow().UtcDateTime),
            roles,
            [.. effective.Order(StringComparer.Ordinal)],
            // The version the edit screen echoes back, so two administrators cannot overwrite each other.
            user.RowVersion);
    }

    private long CurrentUserId => long.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value,
        System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether the caller holds the superuser bit — the same check the role editor makes.
    /// </summary>
    /// <remarks>
    /// Without it, <c>users</c> is a backdoor to everything: an administrator who can set a user's
    /// permissions grants <c>system.dev_admin</c> to themselves. Privilege escalation by the front
    /// door, which is the shape of ISSUES A5.
    /// </remarks>
    private bool CurrentUserIsDevAdmin =>
        User.HasClaim(SmartnetClaims.Permission, Permissions.SystemDevAdmin);
}

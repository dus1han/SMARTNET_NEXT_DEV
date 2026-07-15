using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Identity;

/// <summary>
/// Which companies a user may act in.
/// </summary>
/// <remarks>
/// <b>Today, in this business: all of them, for everyone.</b> Confirmed 2026-07-14. Smart Net and
/// Smart Technologies are two trading entities, not two tenants — the same staff raise documents for
/// both, all day. The company on a document says <i>which entity issued it</i>; it is not a wall
/// between two sets of people, and a <c>Company_Admin</c> administers the business, not one company
/// of it.
/// <para>
/// So the company switcher in the shell chooses <i>which entity you are issuing under</i>, and the
/// filtering that follows from it is a convenience — the right invoice numbers, the right letterhead,
/// the right VAT treatment — <b>not an authorisation boundary</b>. Do not write code that relies on
/// it as one, and do not tell anyone it is one.
/// </para>
/// <para>
/// The per-company mechanism below is kept because it is where the boundary would go if the business
/// ever acquires an entity whose books its existing staff should not see. It is dormant, not
/// load-bearing: every existing role assignment is global, so every user gets every company.
/// </para>
/// </remarks>
public interface ICompanyAccessService
{
    /// <summary>The companies this user may act in. Resolved at sign-in and put into their token.</summary>
    Task<IReadOnlyList<long>> GetAccessibleCompanyIdsAsync(
        long userId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CompanyAccessService : ICompanyAccessService
{
    private readonly SmartnetDbContext _db;
    private readonly IPermissionService _permissions;

    public CompanyAccessService(SmartnetDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<long>> GetAccessibleCompanyIdsAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var all = await _db.Companies
            .Where(c => c.DeletedAt == null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var permissions = await _permissions
            .GetEffectivePermissionsAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        // Dev_Admin gets everything by definition, without needing a role assignment to say so.
        //
        // Note that this is NOT what separates them from a Company_Admin — a Company_Admin also sees
        // every company, because in this business the companies are trading entities rather than
        // tenants (see the remarks on the interface). What separates them is the dev-only surfaces:
        // the audit log, the data-exceptions screen, the system role itself.
        if (permissions.Contains(Permissions.SystemDevAdmin))
        {
            return all;
        }

        var assignments = await _db.UserRoles
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A role assigned with no company is a GLOBAL assignment: it applies everywhere.
        //
        // **Every user is in exactly this state, and that is the intended end state, not a migration
        // artefact.** The staff work across both trading entities and are meant to. Scoping is opt-in
        // and currently opted into by nobody; it exists for a future entity whose books the existing
        // staff should not see, and until there is one, this branch is the only one that runs.
        if (assignments.Contains(null))
        {
            return all;
        }

        var scoped = assignments
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        // Intersected with the companies that actually exist, so a role still pointing at a
        // deleted company does not grant access to a ghost.
        return [.. all.Where(scoped.Contains)];
    }
}

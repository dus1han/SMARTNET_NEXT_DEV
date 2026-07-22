using Microsoft.AspNetCore.Mvc;
using Smartnet.Domain.Auditing;

namespace Smartnet.Api.Auditing;

/// <summary>
/// The check that stops two people editing one record and the second one silently winning.
/// </summary>
/// <remarks>
/// <para><b>Why the token alone is not enough.</b> Every auditable entity maps <c>row_version</c> as an
/// EF concurrency token, so an UPDATE carries <c>WHERE row_version = …</c>. That protects nothing on its
/// own when a controller loads the row and saves it in the same request: the "original" value EF compares
/// is the one it read a millisecond earlier, so it always matches. The comparison only means anything
/// against the version the <i>user</i> was looking at when they started typing — which has to come from
/// the client, and therefore has to be asked for.</para>
///
/// <para><b>This was built and then not wired up.</b> The documents modules pass an
/// <c>ExpectedRowVersion</c> and refuse a stale one. Customers, suppliers, items, users, roles, settings
/// and numbering did not — they were load-modify-save, so two people editing one customer meant the
/// second write won and the first person's change was gone with no error and no trace. That is precisely
/// the legacy behaviour <c>AuditColumns</c> says the token exists to prevent, and it names the customer
/// as its example.</para>
///
/// <para><b>A missing version is refused, not defaulted.</b> Treating "not supplied" as 0, or as "no
/// check", hands the old behaviour back to any caller that forgets — and forgetting is silent. An update
/// has to state which version it is replacing.</para>
/// </remarks>
public static class ConcurrencyGuard
{
    /// <summary>
    /// Null when the edit may proceed; otherwise the response to return.
    /// </summary>
    /// <param name="controller">The calling controller, for <c>Problem(...)</c>.</param>
    /// <param name="current">The entity as it stands in the database.</param>
    /// <param name="expected">The version the caller loaded, from the request.</param>
    /// <param name="noun">What the record is, for the message — "customer", "item", "role".</param>
    public static ObjectResult? StaleEdit(
        this ControllerBase controller,
        IAuditable current,
        int? expected,
        string noun)
    {
        if (expected is null)
        {
            return controller.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title:
                    $"This request did not say which version of the {noun} it was changing, so it cannot "
                    + "be applied safely. Reload the record and try again.");
        }

        if (current.RowVersion != expected)
        {
            // 409, and worded for the person who is about to lose their typing if they simply retry.
            // "Reload" is not a formality here: their form holds a version that no longer exists, and
            // saving it again would undo whatever the other person just did.
            return controller.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title:
                    $"Someone else changed this {noun} while you were editing it. Reload to see their "
                    + "version, then make your changes again.");
        }

        return null;
    }

    /// <summary>
    /// Moves an entity's version on because something it owns has changed, rather than the entity itself.
    /// </summary>
    /// <remarks>
    /// <para><b>The case this exists for.</b> A user's permissions live in <c>user_permission_overrides</c>
    /// and in their roles — not in any column of <c>user_m</c>. So setting somebody's permissions changed
    /// nothing on the user, their <c>row_version</c> did not move, and a version check against it passed
    /// straight through a concurrent permission edit. Two administrators on one account could each save,
    /// and the second silently reinstated a permission the first had just revoked. A guard that cannot
    /// see the change it is guarding is worse than none, because it reads as protection.</para>
    ///
    /// <para><b>Why assigning <c>UpdatedAt</c> is the mechanism.</b> The audit interceptor decides an
    /// entity was updated by looking for a property whose value actually differs — assignment alone is
    /// not enough, and neither is marking the entry <c>Modified</c>. Giving <c>UpdatedAt</c> a new value
    /// is what makes the entry qualify; the interceptor then stamps its own timestamp over it and
    /// increments <c>row_version</c> once. The value written here is a signal, not data.</para>
    ///
    /// <para>The user's version therefore means "this person's access as a whole", which is the thing two
    /// administrators are really contending over.</para>
    /// </remarks>
    public static void TouchForConcurrency(this IAuditable entity, TimeProvider time) =>
        entity.UpdatedAt = time.GetUtcNow().UtcDateTime;
}

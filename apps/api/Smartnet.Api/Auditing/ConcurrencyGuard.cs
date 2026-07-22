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
}

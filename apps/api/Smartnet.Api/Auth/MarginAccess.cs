using System.Security.Claims;
using Smartnet.Api.Auditing;
using Smartnet.Domain.Identity;

namespace Smartnet.Api.Auth;

/// <summary>
/// Whether a caller may see what things cost — and therefore what the business earns.
/// </summary>
/// <remarks>
/// <b>One question, asked in every place margin could escape.</b> Cost and profit appear on the sales
/// report, the customer sales report, the job card report, the profit-and-loss report, the invoice
/// detail and the item master. Scattering the check across six endpoints is how five of them stay
/// current and the sixth quietly does not, so it lives here and each caller asks rather than decides.
///
/// <para><b>It follows the dashboard.</b> A user holds either the management dashboard or the
/// operations one, and the operations one is defined by withholding exactly this. Deriving margin
/// visibility from that choice keeps it a single decision — a second permission could be set to
/// disagree with the first, and then which one wins is a question nobody wants to answer while looking
/// at a clerk who can see the markup.</para>
///
/// <para><b>Enforced on the server, never in the page.</b> A hidden column is still in the response, and
/// the response is one keystroke away in any browser. Where a caller may not see margin the figures are
/// not sent at all.</para>
/// </remarks>
public static class MarginAccess
{
    /// <summary>True when this caller may see cost, profit and margin anywhere in the system.</summary>
    public static bool CanSee(ClaimsPrincipal user) =>
        user.HasClaim(SmartnetClaims.Permission, Permissions.Dashboard)
        || user.HasClaim(SmartnetClaims.Permission, Permissions.SystemDevAdmin);

    /// <summary>
    /// The figure itself, or zero when the caller may not see it.
    /// </summary>
    /// <remarks>
    /// Zero rather than null for the report rows, whose columns are non-nullable decimals and whose
    /// Excel exports would otherwise need a parallel shape. A zeroed cost column beside a real revenue
    /// column reads as "not shown to you", which is the truth; the screens that carry these figures also
    /// drop the column entirely, so the zero is a backstop rather than the presentation.
    /// </remarks>
    public static decimal Redact(this ClaimsPrincipal user, decimal value) => CanSee(user) ? value : 0m;

    /// <summary>The figure itself, or null when the caller may not see it.</summary>
    public static decimal? Redact(this ClaimsPrincipal user, decimal? value) => CanSee(user) ? value : null;
}

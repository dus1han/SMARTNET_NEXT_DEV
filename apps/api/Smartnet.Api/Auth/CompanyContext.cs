using System.Globalization;
using Smartnet.Api.Auditing;

namespace Smartnet.Api.Auth;

/// <summary>
/// Which companies the caller may act in, and which one they are acting in right now.
/// </summary>
/// <remarks>
/// The <b>accessible</b> set is resolved at sign-in and baked into the token — it is not something
/// the client gets to assert. The <b>active</b> company is chosen by the client (the company
/// switcher in the shell) but is honoured only if it is in that set. So switching company is a UI
/// action, and forging one is simply ignored.
/// <para>
/// A Dev_Admin's set is every company that existed when they signed in. A company created after
/// that appears when their token next refreshes, which is at most <c>AccessTokenMinutes</c> away.
/// That is a deliberate trade: an enumerated set is a set that cannot be got wrong.
/// </para>
/// </remarks>
public interface ICompanyContext
{
    IReadOnlySet<long> Accessible { get; }

    /// <summary>The company this request acts in. Null when the caller can reach none.</summary>
    long? Active { get; }

    bool CanAccess(long companyId);
}

public sealed class CompanyContext : ICompanyContext
{
    public const string HeaderName = "X-Company-Id";

    private readonly IHttpContextAccessor _accessor;

    public CompanyContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public IReadOnlySet<long> Accessible =>
        _accessor.HttpContext?.User
            .FindAll(SmartnetClaims.AccessibleCompany)
            .Select(claim =>
                long.TryParse(claim.Value, CultureInfo.InvariantCulture, out var id) ? id : (long?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet()
        ?? [];

    public long? Active
    {
        get
        {
            var accessible = Accessible;

            if (accessible.Count == 0)
            {
                return null;
            }

            var requested = _accessor.HttpContext?.Request.Headers[HeaderName].ToString();

            if (long.TryParse(requested, CultureInfo.InvariantCulture, out var companyId)
                && accessible.Contains(companyId))
            {
                return companyId;
            }

            // No header, or a company this caller may not touch. Fall back to one they can, and
            // never to the one they asked for: silently acting in the wrong company is how money
            // gets filed under the wrong trading entity.
            return accessible.Min();
        }
    }

    public bool CanAccess(long companyId) => Accessible.Contains(companyId);
}

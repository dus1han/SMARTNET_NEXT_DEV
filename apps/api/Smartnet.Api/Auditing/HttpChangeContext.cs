using System.Security.Claims;
using Smartnet.Domain.Auditing;

namespace Smartnet.Api.Auditing;

/// <summary>
/// Reads the "who, why, from where" off the current HTTP request, so the persistence layer can
/// audit a change without any endpoint having to remember to pass it down.
/// </summary>
/// <remarks>
/// Scoped, matching the request and the DbContext. Outside a request (a background job, a test)
/// every value is null, which the interceptor tolerates: a system-initiated change is recorded
/// with no user, rather than being silently attributed to whoever happened to be last.
/// </remarks>
public sealed class HttpChangeContext : IChangeContext
{
    public const string ReasonHeader = "X-Change-Reason";
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IHttpContextAccessor _accessor;
    private readonly Auth.ICompanyContext _companies;

    public HttpChangeContext(IHttpContextAccessor accessor, Auth.ICompanyContext companies)
    {
        _accessor = accessor;
        _companies = companies;
    }

    private HttpContext? Http => _accessor.HttpContext;

    public long? UserId
    {
        get
        {
            var raw = Http?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(raw, out var id) ? id : null;
        }
    }

    /// <summary>
    /// The company the request is acting in, so that every audit row says which set of books it
    /// belongs to. Resolved by <see cref="Auth.ICompanyContext"/> from the token and the
    /// company-switcher header — never taken from the client unchecked.
    /// </summary>
    public long? CompanyId => _companies.Active;

    public string? Reason
    {
        get
        {
            var raw = Http?.Request.Headers[ReasonHeader].ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
    }

    public string? IpAddress => Http?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Truncate(Http?.Request.Headers.UserAgent.ToString(), 255);

    public string? CorrelationId => Http?.TraceIdentifier;

    /// <summary>The column is 255 wide; an over-long User-Agent must not fail the business save.</summary>
    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= max ? value
        : value[..max];
}

/// <summary>Claim types this application issues.</summary>
public static class SmartnetClaims
{
    /// <summary>One claim per company the user may act in. Resolved at sign-in, never asserted by the client.</summary>
    public const string AccessibleCompany = "company";

    public const string Permission = "perm";
    public const string MustChangePassword = "must_change_password";

    /// <summary>
    /// When the session began — carried unchanged through every renewal, as Unix seconds.
    /// </summary>
    /// <remarks>
    /// A renewing session needs two clocks, not one. The token's own <c>exp</c> is the idle limit and
    /// moves every time it is renewed; this one never moves, and is what bounds the whole session. Without
    /// it "renew while in use" means a cookie that, once stolen, stays valid for as long as somebody keeps
    /// using it — which is to say, forever.
    /// </remarks>
    public const string SessionStart = "sst";
}

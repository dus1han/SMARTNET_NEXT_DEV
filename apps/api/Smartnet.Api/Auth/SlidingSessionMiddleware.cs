using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Auth;

/// <summary>
/// Keeps a session alive while it is being used, and re-checks who the user is while doing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The session used to be an absolute hour from sign-in.</b> No refresh path existed — a comment
/// promised one "in slice 3" and it was never built — so a user was signed out mid-sentence exactly one
/// hour after signing in, however busy they had been. That is the fault this fixes.
/// </para>
/// <para>
/// <b>Renewal makes revocation faster, not slower.</b> This is the part that reads backwards. The token
/// carries the user's permissions, so the standing argument against a longer life is that a permission an
/// administrator removes keeps working until the token expires. But renewal re-resolves permissions,
/// company access and the user's own status from the database every time — so a revoked permission now
/// dies at the next renewal, where before nothing re-checked it at all for the token's whole life. A
/// disabled user stops being renewed and is gone within the idle window.
/// </para>
/// <para>
/// <b>Two clocks, deliberately.</b> The token's expiry is the idle limit and moves on each renewal; the
/// <c>sst</c> claim records when the session actually began and never moves. Without the second one,
/// "renew while in use" means a stolen cookie stays valid for as long as it keeps being used, which is
/// indefinitely. See <see cref="JwtOptions.AbsoluteSessionHours"/>.
/// </para>
/// <para>
/// <b>Past half-life only.</b> Renewing on every request would mean a database read per call and a
/// <c>Set-Cookie</c> on every response, to move an expiry that is mostly still fine. Waiting until the
/// token is half spent costs at most one renewal per user per half-window.
/// </para>
/// </remarks>
public sealed class SlidingSessionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public SlidingSessionMiddleware(RequestDelegate next, IOptions<JwtOptions> options, TimeProvider time)
    {
        _next = next;
        _options = options.Value;
        _time = time;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IPermissionService permissions,
        ICompanyAccessService companyAccess,
        SmartnetDbContext db,
        JwtTokenService tokens,
        IWebHostEnvironment environment)
    {
        // Before the endpoint runs, so the response has certainly not started and a cookie can still be
        // written. Nothing here alters the current request's identity — the claims it was authenticated
        // with stand — only what the next request will carry.
        await RenewIfDueAsync(context, permissions, companyAccess, db, tokens, environment)
            .ConfigureAwait(false);

        await _next(context).ConfigureAwait(false);
    }

    private async Task RenewIfDueAsync(
        HttpContext context,
        IPermissionService permissions,
        ICompanyAccessService companyAccess,
        SmartnetDbContext db,
        JwtTokenService tokens,
        IWebHostEnvironment environment)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        // Sign-in issues its own token and sign-out deliberately expires one; renewing across either
        // would fight the endpoint that owns the cookie.
        if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;

        if (UnixClaim(context.User, "exp") is not { } expires
            || UnixClaim(context.User, SmartnetClaims.SessionStart) is not { } sessionStart)
        {
            // A token minted before sessions carried a start time. It stays absolute and expires as it
            // always would have; the next sign-in gets the new behaviour. Renewing it without a start
            // time would create the unbounded session the cap exists to prevent.
            return;
        }

        var due = SessionRenewal.IsDue(
            now,
            expires,
            sessionStart,
            TimeSpan.FromMinutes(_options.AccessTokenMinutes),
            TimeSpan.FromHours(_options.AbsoluteSessionHours));

        if (!due)
        {
            return;
        }

        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } rawId
            || !long.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
        {
            return;
        }

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, context.RequestAborted)
            .ConfigureAwait(false);

        // Deleted, or disabled here or in the legacy app. Declining to renew is the whole mechanism:
        // their current token still works until it lapses, and then it is over.
        if (user is null || user.IsDisabled)
        {
            return;
        }

        var effective = await permissions
            .GetEffectivePermissionsAsync(user.Id, context.RequestAborted)
            .ConfigureAwait(false);

        var companies = await companyAccess
            .GetAccessibleCompanyIdsAsync(user.Id, context.RequestAborted)
            .ConfigureAwait(false);

        var (token, expiresAt) = tokens.Issue(user, effective, companies, sessionStart);

        context.Response.Cookies.Append(
            AuthCookie.Name,
            token,
            AuthCookie.Options(environment.IsDevelopment(), expiresAt));
    }

    /// <summary>Reads a Unix-seconds claim as UTC, or null when it is absent or unreadable.</summary>
    private static DateTime? UnixClaim(ClaimsPrincipal principal, string type) =>
        principal.FindFirstValue(type) is { } raw
        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}

using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Identity;

namespace Smartnet.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IPermissionService _permissions;
    private readonly ICompanyAccessService _companyAccess;
    private readonly JwtTokenService _tokens;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        IAuthService auth,
        IPermissionService permissions,
        ICompanyAccessService companyAccess,
        JwtTokenService tokens,
        IWebHostEnvironment environment)
    {
        _auth = auth;
        _permissions = permissions;
        _companyAccess = companyAccess;
        _tokens = tokens;
        _environment = environment;
    }

    /// <summary>
    /// The one endpoint that must be reachable without a token — and therefore the one the
    /// legacy app left open to SQL injection (<c>LoginController.cs:55</c>, unauthenticated).
    /// </summary>
    /// <remarks>
    /// The response type is declared, not merely implied. Without it the OpenAPI schema has no idea
    /// what this endpoint returns, and the generated TypeScript client silently omits
    /// <c>LoginResponse</c> — which is how a "typed" client ends up not typing the one call every
    /// session begins with.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth
            .LoginAsync(request.Username, request.Password, cancellationToken)
            .ConfigureAwait(false);

        switch (result.Outcome)
        {
            case LoginOutcome.Success:
                break;

            case LoginOutcome.LockedOut:
                // The one case worth distinguishing: the user needs to know that waiting fixes
                // it, or they will phone. It leaks only that the account exists, which someone
                // who has just locked it out already knows.
                return Problem(
                    statusCode: StatusCodes.Status423Locked,
                    title: "Too many failed attempts. Try again shortly.",
                    detail: result.LockedUntil is null
                        ? null
                        : $"The account unlocks at {result.LockedUntil:HH:mm} UTC.");

            case LoginOutcome.Disabled:
            case LoginOutcome.InvalidCredentials:
            default:
                // Same answer for "no such user", "wrong password" and "disabled". Anything else
                // is a free account-enumeration oracle.
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Incorrect username or password.");
        }

        var user = result.User!;

        // Resolved from roles and overrides at sign-in and baked into the token. A user who must
        // change their password still gets their real permissions — the middleware stops them
        // reaching anything with them until they do.
        var permissions = await _permissions
            .GetEffectivePermissionsAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        // Which companies they may act in, resolved here and carried in the token. The company
        // switcher chooses among these; it cannot add to them.
        var companies = await _companyAccess
            .GetAccessibleCompanyIdsAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        var (token, expiresAt) = _tokens.Issue(user, permissions, companies);

        Response.Cookies.Append(
            AuthCookie.Name,
            token,
            AuthCookie.Options(_environment.IsDevelopment(), expiresAt));

        return Ok(new LoginResponse(
            UserId: user.Id,
            Username: user.Username ?? string.Empty,
            Name: user.Name ?? string.Empty,
            MustChangePassword: result.MustChangePassword,
            Permissions: [.. permissions.Order(StringComparer.Ordinal)],
            ExpiresAt: expiresAt));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Append(
            AuthCookie.Name,
            string.Empty,
            AuthCookie.Expired(_environment.IsDevelopment()));

        return NoContent();
    }

    /// <summary>Who the caller is, according to the server rather than to the client's memory.</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<MeResponse> Me() => Ok(new MeResponse(
        UserId: CurrentUserId,
        Username: User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        MustChangePassword: User.HasClaim(SmartnetClaims.MustChangePassword, "true"),

        // Read from the token, not the database: this is exactly what the server will enforce on
        // the next request, so the UI cannot show a button the API would then refuse.
        Permissions: [.. User.FindAll(SmartnetClaims.Permission)
            .Select(c => c.Value)
            .Order(StringComparer.Ordinal)]));

    /// <summary>
    /// Reachable while <c>must_change_password</c> is set — it is the only thing that is. See
    /// <see cref="MustChangePasswordMiddleware"/>.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _auth.ChangePasswordAsync(
            CurrentUserId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken).ConfigureAwait(false);

        switch (result)
        {
            case ChangePasswordResult.Success:
                // The old token still asserts must_change_password, and the new permissions (in
                // Slice 3) may differ. Force a fresh login rather than reason about a stale token.
                Response.Cookies.Append(
                    AuthCookie.Name,
                    string.Empty,
                    AuthCookie.Expired(_environment.IsDevelopment()));

                return NoContent();

            case ChangePasswordResult.InvalidCurrentPassword:
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The current password is incorrect.");

            case ChangePasswordResult.NewPasswordTooWeak:
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: $"The new password must be at least {PasswordPolicy.MinimumLength} "
                         + "characters, and must not be a common password or contain your username.");

            case ChangePasswordResult.NotFound:
            default:
                // The token authenticated a user who no longer exists. Not a 404 — a dead session.
                return Unauthorized();
        }
    }

    private long CurrentUserId => long.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        CultureInfo.InvariantCulture);
}

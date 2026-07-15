namespace Smartnet.Api.Auth;

/// <summary>
/// The token lives in an httpOnly cookie, not in localStorage.
/// </summary>
/// <remarks>
/// httpOnly means JavaScript cannot read it, so a single XSS bug anywhere in the frontend does
/// not hand an attacker a bearer token they can walk away with. SameSite=Strict is what replaces
/// the anti-forgery tokens the legacy app never had (ISSUES A7): the browser simply will not
/// attach this cookie to a request originating from another site.
/// <para>
/// Secure is set outside Development only because localhost is served over plain HTTP; in every
/// deployed environment the cookie refuses to travel unencrypted.
/// </para>
/// </remarks>
public static class AuthCookie
{
    public const string Name = "smartnet_auth";

    public static CookieOptions Options(bool isDevelopment, DateTime expiresUtc) => new()
    {
        HttpOnly = true,
        Secure = !isDevelopment,
        SameSite = SameSiteMode.Strict,
        Expires = expiresUtc,
        Path = "/",
    };

    /// <summary>
    /// Must match <see cref="Options"/> in every attribute except the expiry, or the browser
    /// treats it as a different cookie and quietly declines to delete the real one.
    /// </summary>
    public static CookieOptions Expired(bool isDevelopment) => new()
    {
        HttpOnly = true,
        Secure = !isDevelopment,
        SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UnixEpoch,
        Path = "/",
    };
}

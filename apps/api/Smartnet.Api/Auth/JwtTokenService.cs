using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Smartnet.Api.Auditing;
using Smartnet.Domain.Identity;

namespace Smartnet.Api.Auth;

/// <summary>Mints the access token that the auth cookie carries.</summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    /// <param name="permissions">
    /// The user's effective permissions, resolved from their roles and overrides. Baked into the
    /// token, which is why the token is short-lived: a permission revoked by an administrator
    /// keeps working until the token carrying it expires.
    /// </param>
    /// <param name="companyIds">
    /// The companies this user may act in, resolved at sign-in. The company switcher picks one of
    /// these; anything else is ignored. The client never gets to assert its own access.
    /// </param>
    public (string Token, DateTime ExpiresAt) Issue(
        User user,
        IReadOnlySet<string> permissions,
        IReadOnlyList<long> companyIds)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            // The user id, not the username: a username can be changed, and every audit row
            // points at the id.
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // Carried in the token so that every endpoint can refuse to do anything except change the
        // password — enforced server-side, not by the frontend choosing to show that screen.
        if (user.MustChangePassword)
        {
            claims.Add(new Claim(SmartnetClaims.MustChangePassword, "true"));
        }

        // One claim per permission. The authorization policies read these, so an endpoint's
        // requirement is checked against what the server granted at sign-in — not against
        // anything the client says about itself.
        foreach (var permission in permissions)
        {
            claims.Add(new Claim(SmartnetClaims.Permission, permission));
        }

        foreach (var companyId in companyIds)
        {
            claims.Add(new Claim(
                SmartnetClaims.AccessibleCompany,
                companyId.ToString(CultureInfo.InvariantCulture)));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

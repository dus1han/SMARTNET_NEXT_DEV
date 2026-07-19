using System.ComponentModel.DataAnnotations;

namespace Smartnet.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The HMAC signing key. Comes from the environment — never from source, and never with a
    /// default. The legacy app hardcoded its database and SMTP passwords in C#; a signing key
    /// with a fallback value is the same mistake wearing a different hat, and a worse one,
    /// because anyone holding it can mint a token for any user.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt__SigningKey must be at least 32 bytes of real entropy.")]
    public string SigningKey { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = "smartnet";

    [Required]
    public string Audience { get; init; } = "smartnet";

    /// <summary>
    /// Short. The token carries the user's permissions, so a long-lived one keeps granting access
    /// that an administrator has already revoked. Slice 3 adds the refresh path.
    /// </summary>
    [Range(5, 480)]
    public int AccessTokenMinutes { get; init; } = 30;
}

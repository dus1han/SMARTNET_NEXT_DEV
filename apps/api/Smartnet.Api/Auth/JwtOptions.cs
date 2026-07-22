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
    /// How long a session lasts, from sign-in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Absolute, not idle.</b> There is no refresh path — an earlier note here promised one in "slice
    /// 3" and it was never built — so the clock starts at sign-in and does not reset with activity. A
    /// user is signed out mid-sentence exactly this long after signing in, however busy they have been.
    /// Raised from thirty minutes to an hour because being thrown out twice an afternoon while working is
    /// its own kind of broken.
    /// </para>
    /// <para>
    /// It cannot simply be set to a week. The token carries the user's permissions, so until it expires
    /// it keeps granting access an administrator has already revoked — that window is the real cost of
    /// every increase, and the reason the fix is a refresh path rather than a bigger number here.
    /// </para>
    /// </remarks>
    [Range(5, 480)]
    public int AccessTokenMinutes { get; init; } = 60;
}

namespace Smartnet.Api.Auth;

/// <summary>
/// Why a request was rejected as unauthenticated.
/// </summary>
/// <remarks>
/// The access log records that a 401 happened and nothing about why, and the three ordinary causes are
/// indistinguishable from outside it: a token that has expired, a cookie the browser declined to send,
/// and a signature that no longer verifies. Working out which one was behind "it logs me out after a few
/// minutes" meant pairing sign-in times against 401 times by hand and doing arithmetic on the gaps. This
/// is a line of log instead.
/// </remarks>
internal static partial class AuthLog
{
    /// <remarks>
    /// The cause travels as the exception rather than as a formatted type name: the generator attaches it
    /// to the entry for free, where <c>GetType().Name</c> would be a reflection call made on every
    /// rejection whether or not anything is listening.
    /// </remarks>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Token rejected on {Path}")]
    public static partial void TokenRejected(ILogger logger, string? path, Exception exception);

    /// <summary>
    /// A 401 where no token reached us at all — the other half of the question.
    /// </summary>
    /// <remarks>
    /// An expired token and an absent cookie both surface as a bare 401 and send the user to the sign-in
    /// screen identically, but they are opposite faults: one says the lifetime is too short, the other
    /// says the browser stopped sending a cookie it still holds — which points at SameSite, Secure, Path
    /// or the expiry attribute, none of which a longer lifetime would help.
    /// <para>
    /// Only logged when the request carried cookies. A request with none is a scanner, not a session, and
    /// this endpoint surface attracts a steady trickle of them.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "No auth cookie on {Path}, though the browser sent {CookieCount} other cookie(s)")]
    public static partial void NoAuthCookie(ILogger logger, string? path, int cookieCount);
}

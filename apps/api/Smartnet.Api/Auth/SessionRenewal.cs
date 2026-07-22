namespace Smartnet.Api.Auth;

/// <summary>
/// Whether a session in use should be handed a fresh token.
/// </summary>
/// <remarks>
/// A pure function, separated from the middleware for the same reason <c>BackupSchedule</c> is separated
/// from the backup job: the decision has edge cases worth testing — the half-life boundary, the absolute
/// cap, a clock that disagrees with itself — and none of them should need an HTTP server, a database and
/// a fifteen-minute wait to exercise.
/// </remarks>
public static class SessionRenewal
{
    /// <param name="expiresUtc">When the current token lapses.</param>
    /// <param name="sessionStartUtc">
    /// When the session began — carried through renewals, so this does not move.
    /// </param>
    /// <param name="lifetime">A full token's life. Renewal happens once half of it is spent.</param>
    /// <param name="absolute">The longest a session may live, however continuously it is used.</param>
    public static bool IsDue(
        DateTime utcNow,
        DateTime expiresUtc,
        DateTime sessionStartUtc,
        TimeSpan lifetime,
        TimeSpan absolute)
    {
        // The cap wins over everything. A session past it is not renewed again, and lapses when its
        // current token does — no error, no forced sign-out mid-request, just a predictable ending.
        if (utcNow - sessionStartUtc >= absolute)
        {
            return false;
        }

        // Already lapsed. Renewing here would resurrect a dead session from a cookie the browser is
        // entitled to have discarded, which is a sign-in, not a renewal.
        if (expiresUtc <= utcNow)
        {
            return false;
        }

        // Plenty of life left. Renewing on every request would mean a database read per call and a
        // Set-Cookie on every response, to move an expiry that is mostly still fine.
        return expiresUtc - utcNow <= lifetime / 2;
    }
}

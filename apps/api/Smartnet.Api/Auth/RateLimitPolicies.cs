using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Smartnet.Api.Auth;

public static class RateLimitPolicies
{
    /// <summary>
    /// Throttles login and change-password by source IP.
    /// </summary>
    /// <remarks>
    /// Per-account lockout (5 attempts, 15 minutes) already stops someone guessing one user's
    /// password. This stops the other shape of the attack: one guess each against a thousand
    /// accounts, which never trips any single account's lockout. The two defences are not
    /// redundant — they cover different axes, and the legacy app has neither.
    /// </remarks>
    public const string Login = "login";

    public static void AddSmartnetRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(Login, context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),

                    // No queue: a rejected login attempt should be rejected now, not held open.
                    QueueLimit = 0,
                }));
        });
}

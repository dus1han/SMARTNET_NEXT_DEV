using Microsoft.AspNetCore.Mvc;
using Smartnet.Api.Auditing;

namespace Smartnet.Api.Middleware;

/// <summary>
/// A user who must change their password can do exactly two things: change it, or log out.
/// </summary>
/// <remarks>
/// Enforced here rather than by the frontend routing them to the change-password screen. Both
/// live accounts still have four-character plaintext passwords that were readable to anyone with
/// the source code; "please change it" is not a control, and a client-side redirect is a
/// suggestion that any direct API call ignores.
/// </remarks>
public sealed class MustChangePasswordMiddleware
{
    private static readonly string[] AlwaysAllowed =
    [
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/me",
    ];

    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var mustChange =
            context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(SmartnetClaims.MustChangePassword, "true");

        if (!mustChange || IsAllowed(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "You must change your password before continuing.",
            Instance = context.Request.Path,
        };

        problem.Extensions["correlationId"] = context.TraceIdentifier;

        // A distinct code so the client can route to the change-password screen without having to
        // pattern-match on the message.
        problem.Extensions["code"] = "password_change_required";

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static bool IsAllowed(PathString path) =>
        AlwaysAllowed.Any(allowed => path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase));
}

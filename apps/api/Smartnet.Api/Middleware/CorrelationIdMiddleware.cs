using Serilog.Context;
using Smartnet.Api.Auditing;

namespace Smartnet.Api.Middleware;

/// <summary>
/// Gives every request an id, puts it on the log scope, the response, and the audit row.
/// </summary>
/// <remarks>
/// This is what makes the generic error message tolerable: the user sees "something went wrong,
/// reference abc123", they read that reference to you, and you find the exact request — and the
/// exact audit row — in the logs. The legacy app instead returned the stack trace to the browser
/// (ISSUES A9), which is the other way of answering the question.
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Honour an id from the caller so a chain of services shares one, but only if it is
        // plausible — an unbounded client-supplied string ends up in the logs and the database.
        var incoming = context.Request.Headers[HttpChangeContext.CorrelationIdHeader].ToString();

        context.TraceIdentifier = Guid.TryParse(incoming, out var supplied)
            ? supplied.ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers[HttpChangeContext.CorrelationIdHeader] = context.TraceIdentifier;

        using (LogContext.PushProperty("CorrelationId", context.TraceIdentifier))
        {
            await _next(context);
        }
    }
}

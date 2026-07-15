using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smartnet.Api.Middleware;

/// <summary>
/// The only place an unhandled exception becomes an HTTP response. Full detail goes to the
/// structured log; the client gets a generic message and a correlation id, never a stack trace.
/// </summary>
/// <remarks>
/// Closes ISSUES A9: every legacy controller ends
/// <c>catch (Exception ex) { return Json(new { data = "terror", te = ex.ToString() }); }</c>,
/// handing the browser its stack traces, its SQL and its schema.
/// <para>
/// This handler exists so that no endpoint needs a try/catch — and so that an endpoint that
/// grows one is visibly doing something unusual.
/// </para>
/// </remarks>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _log;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) => _log = log;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled {ExceptionType} on {Method} {Path} → {Status} [{CorrelationId}]")]
    private partial void LogUnhandled(
        Exception exception,
        string exceptionType,
        string method,
        string path,
        int status,
        string correlationId);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.TraceIdentifier;

        var (status, title) = exception switch
        {
            // Two users edited the same record. The second one now finds out — where the legacy
            // app would have let them silently overwrite the first.
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "This record was changed by someone else while you were editing it. "
                + "Reload it and reapply your change."),

            // A guard rail tripped, not a crash: the message is ours and is safe to show.
            InvalidOperationException or ArgumentException => (
                StatusCodes.Status400BadRequest,
                "The request could not be processed."),

            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "You do not have permission to do that."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Something went wrong. The error has been logged."),
        };

        // Everything the client is not told, the log is.
        LogUnhandled(
            exception,
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path,
            status,
            correlationId);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = httpContext.Request.Path,
        };

        // The one thing the client does get: the string that lets you find the request in the
        // logs when they read it back to you over the phone.
        problem.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = status;
        await httpContext.Response
            .WriteAsJsonAsync(problem, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}

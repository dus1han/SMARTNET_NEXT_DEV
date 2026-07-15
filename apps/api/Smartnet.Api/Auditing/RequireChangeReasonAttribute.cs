using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Smartnet.Api.Auditing;

/// <summary>
/// Rejects the request unless it carries an <c>X-Change-Reason</c> header of real substance.
/// </summary>
/// <remarks>
/// Applied to the actions AUDIT.md §5 makes mandatory: editing an issued invoice or credit note,
/// deleting anything, changing permissions, resetting a password, and changing tax rates,
/// numbering or company details.
///
/// <para>Enforced here, on the server, rather than by the form that is supposed to ask for it.
/// A rule the client enforces is a rule that a direct API call ignores.</para>
///
/// <para><b>Do not spread this attribute further than the spec says.</b> Demand a reason for
/// every keystroke and staff will type "." forever — which is worse than no reason at all,
/// because it looks like an audit trail and isn't.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireChangeReasonAttribute : ActionFilterAttribute
{
    /// <summary>Long enough to exclude "." and "x", short enough not to be a essay prompt.</summary>
    public int MinimumLength { get; init; } = 10;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var reason = context.HttpContext.Request.Headers[HttpChangeContext.ReasonHeader]
            .ToString()
            .Trim();

        if (reason.Length < MinimumLength)
        {
            var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [HttpChangeContext.ReasonHeader] = string.IsNullOrEmpty(reason)
                    ? ["This action requires a reason. Send it in the X-Change-Reason header."]
                    : [$"The reason must be at least {MinimumLength} characters and explain the change."],
            })
            {
                Title = "A reason is required for this change.",
                Status = StatusCodes.Status400BadRequest,
            };

            problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;

            context.Result = new BadRequestObjectResult(problem);
            return;
        }

        base.OnActionExecuting(context);
    }
}

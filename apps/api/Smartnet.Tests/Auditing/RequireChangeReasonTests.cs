using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Smartnet.Api.Auditing;

namespace Smartnet.Tests.Auditing;

/// <summary>
/// AUDIT.md §5: the reason rule is enforced server-side, "not by a hopeful frontend".
/// These tests are what makes that sentence true.
/// </summary>
public sealed class RequireChangeReasonTests
{
    [Fact]
    public void A_change_with_no_reason_is_rejected()
    {
        var context = ExecutingContext(reason: null);

        new RequireChangeReasonAttribute().OnActionExecuting(context);

        // The endpoint body never runs: a Result set in OnActionExecuting short-circuits it.
        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void A_reason_of_a_single_full_stop_is_rejected()
    {
        // The failure mode AUDIT.md warns about by name: staff typing "." forever produces
        // something that looks like an audit trail and isn't. A minimum length is the cheapest
        // defence against it.
        var context = ExecutingContext(reason: ".");

        new RequireChangeReasonAttribute().OnActionExecuting(context);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void The_rejection_carries_the_correlation_id_and_names_the_header()
    {
        var context = ExecutingContext(reason: null);

        new RequireChangeReasonAttribute().OnActionExecuting(context);

        var problem = context.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ValidationProblemDetails>().Subject;

        problem.Errors.Should().ContainKey(HttpChangeContext.ReasonHeader);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public void A_real_reason_is_allowed_through()
    {
        var context = ExecutingContext(
            reason: "Customer sent a corrected PO; the original line price was wrong.");

        new RequireChangeReasonAttribute().OnActionExecuting(context);

        // Result left unset means the filter did not short-circuit — the endpoint will run.
        context.Result.Should().BeNull();
    }

    private static ActionExecutingContext ExecutingContext(string? reason)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "corr-1" };

        if (reason is not null)
        {
            http.Request.Headers[HttpChangeContext.ReasonHeader] = reason;
        }

        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            controller: null!);
    }
}

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smartnet.Api.Auth;
using Smartnet.Domain.Identity;

namespace Smartnet.Tests.Identity;

/// <summary>
/// The guard rail. Every endpoint states, out loud, who may call it.
/// </summary>
/// <remarks>
/// ISSUES A5: the legacy app's <c>SessionExpireAttribute</c> checked only that <i>somebody</i> was
/// logged in. The permission flags were read in the <c>Index</c> actions to decide which menu
/// items to draw, and the data endpoints behind those menus checked nothing at all — so any
/// authenticated user could call <c>ManageUserController.updatepermission</c> and make themselves
/// an administrator.
///
/// <para>That did not happen because anyone decided it should. It happened because nothing forced
/// each new endpoint to say what it required, and eventually one of them didn't. These tests are
/// that forcing function: a new endpoint with no authorization decision fails the build, and the
/// developer who added it has to make the decision on purpose.</para>
/// </remarks>
public sealed class EndpointAuthorizationTests
{
    private static readonly IReadOnlyList<Type> Controllers =
    [
        .. typeof(Program).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract),
    ];

    public static TheoryData<string, MethodInfo> Endpoints()
    {
        var data = new TheoryData<string, MethodInfo>();

        foreach (var action in Controllers.SelectMany(Actions))
        {
            data.Add($"{action.DeclaringType!.Name}.{action.Name}", action);
        }

        return data;
    }

    [Fact]
    public void There_is_at_least_one_endpoint_to_check()
    {
        // Otherwise a refactor that moves or renames the controllers turns every test below into a
        // vacuous pass, and the guard rail silently stops guarding.
        Endpoints().Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void Every_endpoint_declares_who_may_call_it(string name, MethodInfo action)
    {
        var anonymous = Has<AllowAnonymousAttribute>(action);
        var authorized = Has<AuthorizeAttribute>(action); // RequirePermission derives from this

        (anonymous || authorized).Should().BeTrue(
            $"{name} declares neither [AllowAnonymous] nor an authorization attribute. Decide "
            + "which it is: an endpoint that reaches business data needs [RequirePermission], and "
            + "one that is genuinely public needs to say so.");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void No_endpoint_is_both_anonymous_and_authorized(string name, MethodInfo action)
    {
        // A contradiction like this reads as secured and behaves as open: AllowAnonymous wins.
        var anonymous = Has<AllowAnonymousAttribute>(action);
        var permissioned =
            action.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Any()
            || action.DeclaringType!.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Any();

        (anonymous && permissioned).Should().BeFalse(
            $"{name} is marked [AllowAnonymous] but also carries a permission requirement. "
            + "AllowAnonymous wins, so the permission is decorative.");
    }

    [Fact]
    public void Every_permission_required_by_an_endpoint_exists_in_the_catalogue()
    {
        var required = Controllers
            .SelectMany(c => c.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
                .Concat(Actions(c).SelectMany(a => a.GetCustomAttributes<RequirePermissionAttribute>(inherit: true))))
            .Select(a => a.Policy!)
            .Distinct(StringComparer.Ordinal);

        // A policy is registered for each catalogue entry at startup. A requirement outside the
        // catalogue has no policy, so ASP.NET throws a 500 on first call rather than a clean 403 —
        // and only for whoever happens to hit it first.
        foreach (var permission in required)
        {
            Permissions.IsKnown(permission).Should().BeTrue(
                $"an endpoint requires '{permission}', which is not in Permissions.All");
        }
    }

    [Fact]
    public void The_users_and_roles_endpoints_are_not_reachable_without_permission()
    {
        // The specific regression A5 describes, asserted by name so that deleting the attribute
        // fails a test that says why it mattered.
        RequiredPermissionOn("UsersController").Should().Be(Permissions.Users);
        RequiredPermissionOn("RolesController").Should().Be(Permissions.RolesManage);
    }

    private static string? RequiredPermissionOn(string controllerName) => Controllers
        .Single(c => c.Name == controllerName)
        .GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
        .SingleOrDefault()
        ?.Policy;

    /// <summary>Public, non-inherited methods are the actions; Object's members and helpers are not.</summary>
    private static IEnumerable<MethodInfo> Actions(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName);

    private static bool Has<TAttribute>(MethodInfo action)
        where TAttribute : Attribute =>
        action.GetCustomAttributes<TAttribute>(inherit: true).Any()
        || action.DeclaringType!.GetCustomAttributes<TAttribute>(inherit: true).Any();
}

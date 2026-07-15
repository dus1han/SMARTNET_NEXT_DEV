using Microsoft.AspNetCore.Authorization;
using Smartnet.Api.Auditing;
using Smartnet.Domain.Identity;

namespace Smartnet.Api.Auth;

/// <summary>
/// Registers one authorization policy per permission, and makes <c>Dev_Admin</c> satisfy all of
/// them.
/// </summary>
/// <remarks>
/// The policies are generated from <see cref="Permissions.All"/>, so a permission cannot be
/// enforced unless it is in the catalogue, and every permission in the catalogue is enforceable.
/// A typo in an endpoint's <c>[RequirePermission("expesnes")]</c> therefore fails at startup with
/// "no policy named expesnes" rather than quietly authorising nobody — or, worse, everybody.
/// </remarks>
public static class PermissionPolicies
{
    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in Permissions.All)
        {
            options.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    // Either the user holds this exact permission...
                    context.User.HasClaim(SmartnetClaims.Permission, permission)

                    // ...or they are Dev_Admin, who holds everything by definition. Without this,
                    // every new permission added in a later phase would have to be granted to the
                    // superuser by hand, and one day somebody would forget.
                    || context.User.HasClaim(SmartnetClaims.Permission, Permissions.SystemDevAdmin)));
        }
    }
}

/// <summary>
/// Declares the permission an endpoint requires.
/// </summary>
/// <remarks>
/// Every endpoint carries one of these, or <c>[AllowAnonymous]</c>, and nothing else is allowed —
/// there is a test that enumerates the endpoints and fails the build otherwise.
///
/// <para>This is what ISSUES A5 costs when it is missing. The legacy app read the permission flags
/// in its <c>Index</c> actions purely to decide which menu items to render, while the data
/// endpoints behind them checked nothing at all: <c>ManageUserController.updatepermission</c> was
/// callable by any logged-in user, which is privilege escalation to administrator. Hiding a button
/// is not authorisation.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
    {
        if (!Permissions.IsKnown(permission))
        {
            // Thrown at startup, when the attribute is constructed — not on the first request by
            // an unlucky user.
            throw new ArgumentException(
                $"'{permission}' is not a known permission. Add it to Permissions.All, or fix the "
                + "spelling. An endpoint guarded by a permission nobody can hold is unreachable; "
                + "one guarded by a policy that does not exist is a 500.",
                nameof(permission));
        }

        Policy = permission;
    }
}

using FluentValidation;
using Smartnet.Domain.Identity;

namespace Smartnet.Api.Contracts;

// --- Users ---------------------------------------------------------------------------------

public sealed record UserSummary(
    long Id,
    string Username,
    string Name,
    bool IsDisabled,
    bool MustChangePassword,
    bool IsLockedOut,
    IReadOnlyList<RoleSummary> Roles,
    IReadOnlyList<string> EffectivePermissions);

public sealed record CreateUserRequest(string Username, string Name, long[] RoleIds);

/// <remarks>
/// There is deliberately no password field. The server generates a single-use one and returns it
/// exactly once — the legacy app gave every new user the password <c>1234</c>
/// (<c>ManageUserController.cs:173</c>), which meant every account in the system shared a password
/// that was also written in the source code.
/// </remarks>
public sealed record CreateUserResponse(long Id, string TemporaryPassword);

public sealed record UpdateUserRequest(string Name, long[] RoleIds);

public sealed record ResetPasswordResponse(string TemporaryPassword);

/// <summary>An override: an exception to what this user's roles grant.</summary>
public sealed record SetOverrideRequest(string Permission, bool? Granted);

/// <summary>
/// The complete set of permissions a user should have — assigned directly, per person.
/// </summary>
/// <remarks>
/// This is permission assignment without the ceremony of roles. The administrator ticks exactly what
/// this person may do, and the server makes their effective permissions equal that set — adding an
/// override where a role does not already grant it, denying one where a role does. Roles still exist
/// for the two system administrators; for everyone else this is how access is given.
/// </remarks>
public sealed record SetUserPermissionsRequest(string[] Permissions);

// --- Roles ---------------------------------------------------------------------------------

public sealed record RoleSummary(
    long Id,
    string Name,
    string? Description,
    bool IsSystem,
    long? CompanyId,
    IReadOnlyList<string> Permissions);

public sealed record SaveRoleRequest(
    string Name,
    string? Description,
    long? CompanyId,
    string[] Permissions);

public sealed record PermissionCatalogueEntry(string Key, bool IsLegacy);

// --- Validators ----------------------------------------------------------------------------

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .MaximumLength(100)
            // The username ends up in the audit log and in the legacy app's varchar columns.
            .Matches("^[a-zA-Z0-9._-]+$")
            .WithMessage("Letters, numbers, dot, underscore and hyphen only.");

        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator() => RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
}

public sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(64);

        RuleForEach(r => r.Permissions)
            .Must(Permissions.IsKnown)
            // A role granting a permission nothing enforces is a role that lies to whoever reads it.
            .WithMessage("'{PropertyValue}' is not a known permission.");
    }
}

public sealed class SetOverrideRequestValidator : AbstractValidator<SetOverrideRequest>
{
    public SetOverrideRequestValidator() =>
        RuleFor(r => r.Permission)
            .NotEmpty()
            .Must(Permissions.IsKnown)
            .WithMessage("'{PropertyValue}' is not a known permission.");
}

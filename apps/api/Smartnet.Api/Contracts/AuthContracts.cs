using FluentValidation;

namespace Smartnet.Api.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    long UserId,
    string Username,
    string Name,
    bool MustChangePassword,
    IReadOnlyList<string> Permissions,
    DateTime ExpiresAt);

/// <remarks>
/// <see cref="Permissions"/> is for rendering — hiding a menu item the user cannot use. It is
/// <b>not</b> the security control: the server enforces the same list on every request, and a
/// client that lies to itself about this list gets 403s, not access.
/// </remarks>
public sealed record MeResponse(
    long UserId,
    string Username,
    bool MustChangePassword,
    IReadOnlyList<string> Permissions);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <remarks>
/// Presence checks only. The password policy itself is NOT validated here: rejecting a login
/// because the *submitted* password is too short would tell an attacker that the real one isn't,
/// and every legacy password is four characters anyway. Strength is enforced where it belongs —
/// when a password is set.
/// </remarks>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Username).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty();

        // The server re-checks this against PasswordPolicy regardless — this rule exists to give
        // the user a field-level message rather than a bare rejection.
        RuleFor(r => r.NewPassword)
            .NotEmpty()
            .MinimumLength(Domain.Identity.PasswordPolicy.MinimumLength)
            .MaximumLength(256)
            .NotEqual(r => r.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

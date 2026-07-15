namespace Smartnet.Domain.Identity;

public interface IAuthService
{
    /// <summary>
    /// Verifies a username and password, applying lockout, and upgrading a legacy plaintext
    /// password to an Argon2id hash on the way through.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a new password. Requires the current one, so that a stolen session cannot be used to
    /// lock the real owner out of their own account.
    /// </summary>
    Task<ChangePasswordResult> ChangePasswordAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public enum ChangePasswordResult
{
    Success,

    /// <summary>The current password did not verify.</summary>
    InvalidCurrentPassword,

    /// <summary>The new password does not meet policy.</summary>
    NewPasswordTooWeak,

    NotFound,
}

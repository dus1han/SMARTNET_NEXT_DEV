using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Identity;

/// <summary>
/// Authentication, and the migration of the legacy plaintext passwords out from under it.
/// </summary>
public sealed class AuthService : IAuthService
{
    /// <summary>Failed attempts before the account locks.</summary>
    private const int MaxFailedAttempts = 5;

    /// <summary>
    /// How long it locks for. Long enough to make online guessing pointless, short enough that a
    /// locked-out showroom is not a phone call to the developer.
    /// </summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly SmartnetDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public AuthService(
        SmartnetDbContext db,
        IPasswordHasher hasher,
        IAuditWriter audit,
        TimeProvider time)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _time = time;
    }

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Audited: "who has been trying to log in as someone who doesn't exist?" is exactly
            // the question a brute-force attempt answers. Today nothing records it at all.
            await RecordFailureAsync(username, "no such user", userId: null, cancellationToken)
                .ConfigureAwait(false);

            return LoginResult.Invalid();
        }

        if (user.IsLockedOut(now))
        {
            await RecordFailureAsync(username, "locked out", user.Id, cancellationToken)
                .ConfigureAwait(false);

            return new LoginResult(LoginOutcome.LockedOut, LockedUntil: user.LockedUntil);
        }

        if (user.IsDisabled)
        {
            await RecordFailureAsync(username, "account disabled", user.Id, cancellationToken)
                .ConfigureAwait(false);

            return new LoginResult(LoginOutcome.Disabled);
        }

        if (!Verify(user, password))
        {
            user.FailedLoginCount++;

            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntil = now.Add(LockoutDuration);
            }

            // Saved before the audit write so the counter survives even a failed attempt.
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await RecordFailureAsync(username, "wrong password", user.Id, cancellationToken)
                .ConfigureAwait(false);

            return LoginResult.Invalid();
        }

        // --- Authenticated from here -------------------------------------------------------

        // The hash-on-login upgrade. The password was just verified against the legacy plaintext
        // column, so this is the one moment we hold the cleartext and can hash it. The plaintext
        // column is deliberately NOT cleared: the legacy app is still live and still reads it to
        // log the same person in. It is dropped in Phase 9, when the old app is dead.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            user.PasswordHash = _hasher.Hash(password);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditAction.Login,
            nameof(User),
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            userId: user.Id,
            details: new { outcome = "success", username },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new LoginResult(
            LoginOutcome.Success,
            user,
            MustChangePassword: user.MustChangePassword);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return ChangePasswordResult.NotFound;
        }

        // Required even for a forced change. A session hijacked mid-flow must not be able to set
        // a password the real owner does not know and lock them out of their own account.
        if (!Verify(user, currentPassword))
        {
            return ChangePasswordResult.InvalidCurrentPassword;
        }

        if (!PasswordPolicy.IsAcceptable(newPassword, user.Username))
        {
            return ChangePasswordResult.NewPasswordTooWeak;
        }

        user.PasswordHash = _hasher.Hash(newPassword);
        user.PasswordChangedAt = _time.GetUtcNow().UtcDateTime;
        user.MustChangePassword = false;

        // The legacy app still authenticates against the plaintext column, so it has to keep
        // matching or the user cannot log into the old app after changing their password in the
        // new one. This line is the dual-write window, and it is the single ugliest thing in
        // Phase 1. It is deleted in Phase 9 together with the column.
        user.LegacyPassword = newPassword;

        // The interceptor audits this automatically, and the redaction list keeps both the hash
        // and the plaintext out of the log — recorded as changed, never with their values.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ChangePasswordResult.Success;
    }

    /// <summary>
    /// Verifies against the Argon2id hash if the user has one, and falls back to the legacy
    /// plaintext column if they do not.
    /// </summary>
    /// <remarks>
    /// The fallback is the whole reason a cutover is possible without resetting everyone's
    /// password on the same morning. It stops being reachable for a given user the moment they
    /// first log in, because that login writes their hash.
    /// </remarks>
    private bool Verify(User user, string password)
    {
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            return _hasher.Verify(password, user.PasswordHash);
        }

        // Ordinal, not culture-aware: a password is a byte sequence, not a word in a language.
        return !string.IsNullOrEmpty(user.LegacyPassword)
            && string.Equals(user.LegacyPassword, password, StringComparison.Ordinal);
    }

    private Task RecordFailureAsync(
        string username,
        string why,
        long? userId,
        CancellationToken cancellationToken) => _audit.RecordAsync(
            AuditAction.Login,
            nameof(User),
            userId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? username,
            userId: userId,
            details: new { outcome = "failure", reason = why, username },
            cancellationToken: cancellationToken);
}

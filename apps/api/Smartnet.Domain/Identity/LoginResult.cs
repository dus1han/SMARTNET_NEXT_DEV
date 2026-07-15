namespace Smartnet.Domain.Identity;

public enum LoginOutcome
{
    Success,

    /// <summary>Wrong username or wrong password. Deliberately not distinguished — see below.</summary>
    InvalidCredentials,

    /// <summary>Too many failed attempts. The lock expires by itself.</summary>
    LockedOut,

    /// <summary>The account is disabled, in either app.</summary>
    Disabled,
}

/// <summary>
/// The outcome of a login attempt.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="User">The authenticated user. Null unless <paramref name="Outcome"/> is Success.</param>
/// <param name="MustChangePassword">
/// True when the user got in but may do nothing else until they set a new password — because
/// theirs predates hashing, and every one of those was stored in plaintext in a database whose
/// credentials were published in source.
/// </param>
/// <param name="LockedUntil">When a locked-out account frees itself. UTC.</param>
public sealed record LoginResult(
    LoginOutcome Outcome,
    User? User = null,
    bool MustChangePassword = false,
    DateTime? LockedUntil = null)
{
    /// <remarks>
    /// <b>Never tell the caller which half was wrong.</b> "No such user" versus "wrong password"
    /// hands an attacker a free username oracle: they enumerate valid accounts first, then attack
    /// only those. One message for both.
    /// </remarks>
    public static LoginResult Invalid() => new(LoginOutcome.InvalidCredentials);
}

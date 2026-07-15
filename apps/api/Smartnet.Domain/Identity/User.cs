using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Identity;

/// <summary>
/// A user of the system, mapped onto the legacy <c>user_m</c> table.
/// </summary>
/// <remarks>
/// The legacy app is still live and still reads and writes this table, so every column it knows
/// about stays exactly as it is. The new columns are additive and defaulted, so a legacy INSERT
/// that names none of them still succeeds.
/// <para>
/// The plaintext <see cref="LegacyPassword"/> column therefore <b>survives Phase 1 on purpose</b>.
/// It is dropped in Phase 9, when the legacy app is dead — not before, or the old app stops being
/// able to log anyone in halfway through the migration.
/// </para>
/// </remarks>
public class User : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    public string? Username { get; set; }

    public string? Name { get; set; }

    /// <summary>
    /// The legacy plaintext password. Read once, to verify a login that predates the hash, and
    /// then never again for that user — see the hash-on-login upgrade in the auth service.
    /// </summary>
    /// <remarks>
    /// Named <c>LegacyPassword</c> rather than <c>Password</c> so that nobody reaches for it by
    /// accident. It is on the audit redaction list either way.
    /// </remarks>
    public string? LegacyPassword { get; set; }

    /// <summary>Argon2id. Null until this user next logs in and their password is upgraded.</summary>
    public string? PasswordHash { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>
    /// Set for anyone still on the legacy default. ManageUserController assigned every new user
    /// the password <c>1234</c>, and both live accounts are still four characters long.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public int FailedLoginCount { get; set; }

    /// <summary>Set by lockout. UTC, like everything else.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>Legacy: "Administrator" | "User". Superseded by roles in Slice 3, kept for the legacy app.</summary>
    public string? Utype { get; set; }

    /// <summary>Legacy customer-portal link. The portal is dropped (decision 2); no rows use it.</summary>
    public string? Cuscode { get; set; }

    /// <summary>Legacy: "Active" | otherwise. The new app disables users via <see cref="DeletedAt"/>.</summary>
    public string Ustat { get; set; } = "Active";

    /// <summary>
    /// Legacy: a display-name string like "Saboor A. : 2026-07-14 10:33:12". Kept because the
    /// legacy app writes it; superseded by <see cref="CreatedBy"/>, which stores the user id.
    /// </summary>
    public string Addedby { get; set; } = string.Empty;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }

    /// <summary>A user is locked out only while the clock says so — the lock expires by itself.</summary>
    public bool IsLockedOut(DateTime utcNow) => LockedUntil is not null && LockedUntil > utcNow;

    /// <summary>Disabled in the new app (soft delete) or in the legacy one (<c>ustat</c>).</summary>
    public bool IsDisabled =>
        DeletedAt is not null || !string.Equals(Ustat, "Active", StringComparison.OrdinalIgnoreCase);
}

namespace Smartnet.Domain.Auditing;

/// <summary>
/// Fields whose <i>value</i> must never reach the audit log. The log records that they changed;
/// it never records what they changed to.
/// </summary>
/// <remarks>
/// Matched on the property name, case-insensitively, across every entity — a deny-list keyed by
/// name rather than by (entity, property) pair, so that a new entity with a
/// <c>PasswordHash</c> property is covered the day it is written rather than the day someone
/// remembers to add it here.
/// <para>
/// This list is deliberately explicit and is reviewed when it changes. An audit log that leaks
/// the secrets it is auditing is worse than no audit log, because it is trusted.
/// </para>
/// </remarks>
public static class AuditRedaction
{
    public const string Placeholder = "***REDACTED***";

    private static readonly HashSet<string> RedactedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Credentials
        "Password",           // the legacy plaintext column, still present until Phase 9
        "PasswordHash",
        "PasswordEncrypted",
        "PasswordSalt",

        // Secrets held in settings
        "SmtpPassword",
        "ApiKey",
        "SigningKey",
        "ClientSecret",
        "ConnectionString",

        // Session / token material
        "RefreshToken",
        "RefreshTokenHash",
        "ResetToken",
        "ResetTokenHash",
    };

    public static bool IsRedacted(string propertyName) => RedactedPropertyNames.Contains(propertyName);
}

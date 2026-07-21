using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Backups;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Backups;

/// <summary>The FTP destination, resolved and ready to connect with.</summary>
/// <param name="Password">Decrypted here and nowhere else — never returned by an endpoint.</param>
public sealed record BackupDestination(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    string Password,
    bool UseTls,
    bool AcceptAnyCertificate,
    string RemotePath,
    string SafetyPath,
    int Retention);

/// <summary>Reads the configured destination, decrypting the stored password.</summary>
public interface IBackupDestinationProvider
{
    Task<BackupDestination?> CurrentAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// Read fresh each time rather than cached. Backups run once an hour and a restore once in a bad year,
/// so the cost is irrelevant, while a cache means an administrator who has just fixed the password
/// watches the next backup fail with the old one and has no way to know why.
/// </remarks>
public sealed class BackupDestinationProvider : IBackupDestinationProvider
{
    /// <summary>
    /// A named purpose, so this ciphertext cannot be decrypted by another protector — an FTP password
    /// must not be interchangeable with the SMTP one.
    /// </summary>
    public const string ProtectorPurpose = "Smartnet.BackupSettings.Password";

    private readonly SmartnetDbContext _db;
    private readonly IDataProtector _protector;

    public BackupDestinationProvider(SmartnetDbContext db, IDataProtectionProvider protection)
    {
        _db = db;
        _protector = protection.CreateProtector(ProtectorPurpose);
    }

    public async Task<BackupDestination?> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.BackupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
        {
            return null;
        }

        return new BackupDestination(
            settings.Enabled,
            settings.Host,
            settings.Port,
            settings.Username ?? string.Empty,
            Decrypt(settings.PasswordEncrypted),
            settings.UseTls,
            settings.AcceptAnyCertificate,
            settings.RemotePath,
            settings.SafetyPath,
            settings.Retention);
    }

    private string Decrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // Said out loud rather than swallowed. This used to return an empty string on the reasoning
            // that a failed login is a clearer signal than a 500 — which was wrong twice over. FTP does
            // not report "your password was blank", it reports a refused login, and the layer above
            // turned that into "could not reach the backup server". Hours went into the network before
            // anyone suspected the key ring.
            //
            // The only cause is a key ring that no longer matches the ciphertext, so name that and say
            // what fixes it. Persisting the ring (DataProtection__KeyPath) is what stops it recurring.
            throw new BackupSecretUnreadableException(ex);
        }
    }
}

/// <summary>
/// The stored FTP password cannot be decrypted, because the Data Protection key ring that encrypted it
/// is gone.
/// </summary>
public sealed class BackupSecretUnreadableException(Exception inner)
    : Exception(
        "The saved FTP password could not be read — the encryption key ring has changed since it was "
        + "stored, which happens when the key ring is not kept outside the container. Re-enter the "
        + "password under Administration → Backups to fix it now.",
        inner);

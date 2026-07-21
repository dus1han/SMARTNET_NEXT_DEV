using FluentValidation;

namespace Smartnet.Api.Contracts;

/// <summary>One backup on the store.</summary>
public sealed record BackupSummary(string Name, long SizeBytes, DateTime ModifiedUtc);

/// <summary>
/// The backup destination as the screen is allowed to see it.
/// </summary>
/// <remarks>
/// <b>There is no password field, and that is the point</b> — the same contract as the SMTP settings.
/// <see cref="HasPassword"/> says whether one is stored; the value itself is never returned, to anybody,
/// because the only reason to read a stored password back out is to take it somewhere else.
/// </remarks>
public sealed record BackupSettingsResponse(
    bool Enabled,
    string Host,
    int Port,
    string? Username,
    bool HasPassword,
    bool UseTls,
    bool AcceptAnyCertificate,
    string RemotePath,
    string SafetyPath,
    int Retention,
    /// <summary>
    /// Whether a restore could run at all. False when the deployment has no privileged database
    /// credential, which is a configuration answer rather than a fault — see BackupOptions.
    /// </summary>
    bool RestoreAvailable);

/// <param name="Password">
/// Null leaves the stored password exactly as it is — which is what the screen sends when the
/// administrator changes the port and does not retype the password. A value replaces it.
/// </param>
public sealed record SaveBackupSettingsRequest(
    bool Enabled,
    string Host,
    int Port,
    string? Username,
    string? Password,
    bool UseTls,
    bool AcceptAnyCertificate,
    string RemotePath,
    string SafetyPath,
    int Retention);

/// <summary>What a backup run produced.</summary>
public sealed record BackupTakenResponse(string Name);

/// <summary>
/// The confirmation a restore requires, beyond the permission and the change reason.
/// </summary>
/// <param name="Confirm">
/// Must be the exact word <c>RESTORE</c>. A restore replaces every record in the database, and a button
/// that does that on one click is a button somebody eventually presses by accident. Typing it is not
/// security — the permission is — it is the pause.
/// </param>
public sealed record RestoreRequest(string Confirm);

/// <param name="SafetyBackup">The copy taken immediately before — the undo, if this was a mistake.</param>
public sealed record RestoreCompletedResponse(string SafetyBackup);

public sealed class SaveBackupSettingsRequestValidator : AbstractValidator<SaveBackupSettingsRequest>
{
    public SaveBackupSettingsRequestValidator()
    {
        RuleFor(r => r.Host).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Port).InclusiveBetween(1, 65535);
        RuleFor(r => r.Username).MaximumLength(200);
        RuleFor(r => r.RemotePath).NotEmpty().MaximumLength(255);
        RuleFor(r => r.SafetyPath).NotEmpty().MaximumLength(255);

        // Fifteen is the business's number; the range is what the rotation can express without becoming
        // either pointless or a way to fill somebody else's disk.
        RuleFor(r => r.Retention).InclusiveBetween(1, 500);

        // Otherwise a pre-restore copy lands in the rotation and is pruned within fifteen hours — which
        // is exactly the window in which it is still wanted.
        RuleFor(r => r.SafetyPath)
            .NotEqual(r => r.RemotePath)
            .WithMessage("The pre-restore folder must be different from the backup folder, or the "
                + "rotation will delete the safety copies.");
    }
}

public sealed class RestoreRequestValidator : AbstractValidator<RestoreRequest>
{
    public RestoreRequestValidator() =>
        RuleFor(r => r.Confirm)
            .Equal("RESTORE")
            .WithMessage("Type RESTORE to confirm that every record in the database will be replaced.");
}

namespace Smartnet.Domain.Auditing;

/// <summary>
/// The actions recorded in <c>audit_log</c>.
/// </summary>
/// <remarks>
/// Mutations (Create/Update/Delete/Restore) are written automatically by the SaveChanges
/// interceptor. The rest are non-mutation events raised explicitly, because auditing only
/// writes misses the questions people actually ask: "who exported the customer list?",
/// "was this invoice ever emailed?". Today neither is answerable.
/// </remarks>
public enum AuditAction
{
    Create,
    Update,
    Delete,
    Restore,
    Login,
    Print,
    Email,
    Export,
}

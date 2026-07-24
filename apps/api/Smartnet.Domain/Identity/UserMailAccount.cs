using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Identity;

/// <summary>
/// Assigns a shared mailbox to a user.
/// </summary>
/// <remarks>
/// Many-to-many: a user can hold several mailboxes, and one mailbox can be shared by several users —
/// "sales@" belongs to everyone on the sales desk. This is only the association; the mailboxes
/// themselves are <see cref="Smartnet.Domain.Settings.MailAccount"/> on the Mail accounts screen.
/// <para>
/// Audited and soft-deleted like <see cref="UserRole"/>: unassigning a mailbox leaves the row with a
/// <see cref="DeletedAt"/> so who-had-what-when stays answerable, and re-assigning it restores that row
/// rather than adding a second (the unique index would refuse a second anyway).
/// </para>
/// </remarks>
public class UserMailAccount : IAuditable
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long MailAccountId { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

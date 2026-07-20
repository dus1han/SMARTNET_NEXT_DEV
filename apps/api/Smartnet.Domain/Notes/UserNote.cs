using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Notes;

/// <summary>
/// A personal note — a title and a body, belonging to the person who wrote it (Phase 7, slice 5).
/// </summary>
/// <remarks>
/// <para><b>Private to its author.</b> A note is visible only to the user who created it; the
/// <c>notes</c> permission decides who may use the feature at all, and <c>created_by</c> decides whose
/// notes they see. Both checks are server-side — the list query filters by author, and every read and
/// write of a single note re-checks ownership rather than trusting an id from the client.</para>
///
/// <para><b>What the legacy screen was.</b> The old <c>Notes</c> module was a single shared textarea:
/// <c>getNote</c> returned <c>SELECT note FROM notes ORDER BY id DESC LIMIT 1</c> and <c>saveNote</c>
/// <i>inserted a new row</i> every time, so 49 rows accumulated, each a full snapshot of the same
/// growing list. There were no titles, no list, no editing and no history — and, being one shared
/// document, no privacy. This replaces all of that.</para>
///
/// <para><b>Named <c>UserNote</c>, table <c>user_notes</c> — not <c>notes</c>.</b> The legacy schema
/// still owns the name <c>notes</c> (49 rows), which LEGACY-DATA-POLICY leaves in place. Taking that
/// name collides with it; MySQL refuses the <c>CREATE TABLE</c>, which is how the clash was first
/// caught. See <c>LegacyTableNameTests</c>, which now fails the build rather than the migration.</para>
///
/// <para><b><see cref="CompanyId"/> is recorded, not filtered on.</b> Notes are personal, so the list
/// does not change when the company switcher does. The column is kept because every table carries it
/// and because knowing which set of books someone was working in when they wrote a note is worth
/// having — but it is not part of the visibility rule.</para>
/// </remarks>
public class UserNote : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The company the author was acting in when the note was written. Recorded, not filtered.</summary>
    public long CompanyId { get; set; }

    /// <summary>What the note is called — the column the list is read and sorted by.</summary>
    public string Title { get; set; } = null!;

    /// <summary>The note itself. Who wrote it and when come from the audit columns.</summary>
    public string Body { get; set; } = null!;

    /// <summary>The author, and the owner. <c>CreatedBy</c> is the visibility rule.</summary>
    public long? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }

    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 8000;
}

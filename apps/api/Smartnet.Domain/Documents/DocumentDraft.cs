using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;

namespace Smartnet.Domain.Documents;

/// <summary>
/// Work in progress on a create screen — a quotation, invoice, purchase order or job card that has been
/// typed but not yet raised, held on the server so closing the tab or signing out does not lose it.
/// </summary>
/// <remarks>
/// <para><b>A draft is not a document.</b> It is deliberately <i>not</i> a row in <c>quotation_h</c> /
/// <c>invoice_h</c> / <c>po_h</c> / <c>jobs_m</c> with a status column, for two reasons that are not
/// matters of taste. First, the legacy ASP.NET app is still reading those tables under the strangler and
/// has no notion of a draft, so a half-typed invoice would appear in its lists, its reports and its
/// outstanding. Second, a document number is allocated inside the save transaction
/// (<c>DocumentNumberAllocator</c>) precisely so that two concurrent saves cannot take the same one — a
/// draft that held a number would either reserve it for as long as somebody left a tab open, or leave it
/// blank and collide with the unique index the cutover is building on <c>quotation_h</c>. So a draft
/// lives here, takes no number, touches no ledger and moves no stock. Raising it goes through the
/// ordinary create endpoint, unchanged, and the draft is then deleted.</para>
///
/// <para><b><see cref="Payload"/> is opaque to the server.</b> It is the create screen's own state,
/// serialised by the browser and handed back verbatim. The server never parses it, so a form that gains
/// a field needs no change here — and, more to the point, a draft can hold a state no create endpoint
/// would accept (half a line, no customer, a date not yet typed), which is the entire purpose. Validation
/// happens once, at the real save, by the code that already does it.</para>
///
/// <para><b>Not <see cref="Auditing.IAuditable"/>, on purpose.</b> The audit interceptor writes an
/// <c>audit_log</c> row for every save of every auditable entity, with a field-level diff. Autosave
/// writes every few seconds while somebody types, so making a draft auditable would bury the audit trail
/// under thousands of diffs of a JSON blob nobody will ever read — and AUDIT.md's whole claim is that the
/// log is worth reading. What is worth auditing is the document that gets raised, and that is audited
/// already. The columns below are kept and stamped by hand, so a draft still says who left it and when.</para>
///
/// <para><b>Shared, not private.</b> Any user who could raise the document can see and resume its draft
/// within the same company — one person books the work and another prices it. <see cref="RowVersion"/> is
/// what keeps them from overwriting each other: an autosave that carries a stale version is refused
/// rather than silently winning.</para>
///
/// <para><b>Hard-deleted.</b> Nothing here is soft-deleted. A draft that has been raised is superseded by
/// a real document, and a draft that was discarded is something somebody decided not to keep; a
/// tombstoned scratchpad serves no one and the table would grow without bound.</para>
/// </remarks>
public class DocumentDraft
{
    public long Id { get; set; }

    /// <summary>Which create screen this is a draft of — one of <see cref="DraftDocumentTypes.All"/>.</summary>
    public string DocType { get; set; } = null!;

    /// <summary>The company the draft belongs to, and the boundary it is visible within.</summary>
    public long CompanyId { get; set; }

    /// <summary>
    /// The create screen's state, as the browser serialised it. Never parsed here — see the remarks.
    /// </summary>
    public string Payload { get; set; } = null!;

    /// <summary>
    /// Who the draft is addressed to, for the list — a customer or a supplier name, as the screen knows it.
    /// </summary>
    /// <remarks>
    /// Denormalised from the payload by the browser rather than joined at read time, because the payload
    /// is opaque and because a draft may name a party that is only half-chosen. Null until one is picked.
    /// </remarks>
    public string? PartyName { get; set; }

    /// <summary>The draft's running total in major units, for the list. Null when nothing priceable is typed yet.</summary>
    public decimal? Total { get; set; }

    /// <summary>How many lines have been typed, for the list.</summary>
    public int LineCount { get; set; }

    /// <summary>Who started the draft, and when. Stamped here, not by the audit interceptor.</summary>
    public long? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Who last typed into it, and when — what the list sorts by.</summary>
    public long? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The concurrency token. Incremented on every save; an autosave carrying a stale one is refused (409),
    /// so two people in the same shared draft cannot silently overwrite each other.
    /// </summary>
    public int RowVersion { get; set; }

    /// <summary>
    /// The cap on <see cref="Payload"/>. Generous next to any real document — a 200-line invoice is a few
    /// tens of kilobytes — and there so that a runaway client cannot post megabytes every two seconds.
    /// </summary>
    public const int MaxPayloadLength = 262_144;

    public const int MaxPartyNameLength = 200;
}

/// <summary>
/// The document types that have a draft, and the permission each one's draft requires.
/// </summary>
/// <remarks>
/// <b>The rule is: you may hold a draft exactly when you may raise the document.</b> Anything else is a
/// hole — a draft carries the same commercial detail the document does (who the customer is, what they
/// are being charged), so gating it more loosely than the create endpoint would leak that detail to
/// someone the create endpoint refuses. The permissions below are therefore read off the four create
/// endpoints, not chosen afresh; see <c>DocumentDraftPermissionTests</c>, which asserts they still match.
/// </remarks>
public static class DraftDocumentTypes
{
    public static readonly IReadOnlyDictionary<string, string> PermissionByType =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DocumentTypes.Quotation] = Permissions.ItemQuotation,
            [DocumentTypes.Invoice] = Permissions.ItemInvoice,
            [DocumentTypes.PurchaseOrder] = Permissions.PurchaseOrder,
            [DocumentTypes.JobCard] = Permissions.JobCards,
        };

    public static readonly IReadOnlyList<string> All = [.. PermissionByType.Keys];

    public static bool IsKnown(string? docType) =>
        docType is not null && PermissionByType.ContainsKey(docType);

    /// <summary>The permission a draft of this type requires, or null when the type has no drafts.</summary>
    public static string? PermissionFor(string? docType) =>
        docType is not null && PermissionByType.TryGetValue(docType, out var permission) ? permission : null;
}

using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.MasterData;

/// <summary>
/// One structured contact for a customer — a real row, replacing the legacy <c>;</c>-separated strings
/// (Phase 6, slice 4).
/// </summary>
/// <remarks>
/// The legacy app stored a customer's contacts as two independent <c>;</c>-separated <c>varchar</c> columns
/// on <c>cus_m</c> — <c>contactp</c> (names) and <c>email</c> — split server-side wherever a contact was
/// needed. Here each contact is a row: a name, an optional role, phone and email, and whether it is the
/// primary. The legacy columns are kept **dual-written** (the names and emails re-joined with <c>;</c>) on
/// every customer save, so the still-live legacy app and its <c>getQuoteContactP</c> splitter keep reading.
///
/// <para>A genuinely new table (<c>customer_contacts</c>), so its migration is EF's generated
/// <c>CreateTable</c>. Soft-deletable, because the audit interceptor rewrites every delete as a soft delete
/// (nothing is hard-deleted); a query filter hides removed contacts, so reconciling a customer's list on
/// save drops the removed ones from view.</para>
/// </remarks>
public class CustomerContact : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The customer this contact belongs to, by surrogate key.</summary>
    public long CustomerId { get; set; }

    /// <summary>The contact's name — the value the legacy <c>contactp</c> string held one of.</summary>
    public string? Name { get; set; }

    /// <summary>Their role, e.g. "Accounts" or "Site manager". Optional; the legacy data had none.</summary>
    public string? Role { get; set; }

    public string? Phone { get; set; }

    /// <summary>Their email — the value the legacy <c>email</c> string held one of.</summary>
    public string? Email { get; set; }

    /// <summary>The default contact for the customer — the one a new document picks first.</summary>
    public bool IsPrimary { get; set; }

    public Customer Customer { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

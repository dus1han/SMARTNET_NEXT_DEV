using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.MasterData;

/// <summary>
/// A customer, mapped onto the legacy <c>cus_m</c> table.
/// </summary>
/// <remarks>
/// The legacy app is still live and still writes this table, so every column it knows about stays
/// exactly as it is. What is added is a real primary key (the table has none — Finding 6), the audit
/// columns, and a type for the credit limit, which is currently a <c>varchar(100)</c> holding money
/// (Finding 5).
/// </remarks>
public class Customer : IAuditable, ISoftDeletable
{
    /// <summary>
    /// Added by the migration. The legacy table identifies a customer by <see cref="Code"/> — a
    /// hand-typed string with no unique index on it, which nothing prevents two customers sharing.
    /// </summary>
    public long Id { get; set; }

    /// <summary>The business's own code: "C-1". Unique, now that there is an index saying so.</summary>
    public string? Code { get; set; }

    public string? Name { get; set; }

    /// <summary>"Company" or "Individual", as the legacy app writes it.</summary>
    public string? Type { get; set; }

    public string? ContactPerson { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? VatNumber { get; set; }

    /// <summary>
    /// Which trading entity this customer is <b>associated with</b> — the legacy <c>c_form</c>.
    /// </summary>
    /// <remarks>
    /// <b>An indication, not a boundary.</b> Confirmed against three years of documents: customers
    /// assigned to Smart Net have 533 invoices raised by Smart Technologies, and customers assigned
    /// to Smart Technologies have 128 raised by Smart Net. Both entities invoice each other's
    /// customers, routinely, and the same staff do it.
    /// <para>
    /// So this is a <i>default</i> when raising a document, and nothing else. There is no query filter
    /// on it, and there must not be one: 42 customers are assigned to no entity at all, and filtering
    /// by this column would hide 116 of them from the people who invoice them every week.
    /// </para>
    /// </remarks>
    public long? AssignedCompanyId { get; set; }

    /// <summary>
    /// The margin band applied to this customer's pricing — the legacy <c>pro</c>, pointing at
    /// <c>profit_percent.id</c> (5%, 10%, 15%…).
    /// </summary>
    /// <remarks>
    /// A foreign key in spirit only: the legacy schema has zero foreign keys (Finding 6), and this
    /// one is a <c>varchar</c> holding a number. It is what <c>getCustomerProfit</c> reads.
    /// </remarks>
    public long? ProfitPercentId { get; set; }

    /// <summary>
    /// The credit limit, which Phase 5's <c>creditlimitcheck</c> will enforce.
    /// </summary>
    /// <remarks>
    /// <b>198 of 223 customers have this set to zero</b>, so the wall Phase 5 builds applies to
    /// 25 of them. Worth knowing before building it.
    /// <para>
    /// Stored as <c>varchar(100)</c> in the legacy schema — money as text (Finding 5). The migration
    /// retypes it to <c>DECIMAL(18,4)</c>, which is safe because every value currently parses, and
    /// which means the database will now reject <c>"12,500"</c> rather than storing it.
    /// </para>
    /// </remarks>
    public decimal CreditLimit { get; set; }

    /// <summary>
    /// The customer's structured contacts (Phase 6, slice 4) — the real rows behind the legacy
    /// <c>;</c>-separated <see cref="ContactPerson"/> / <see cref="Email"/> strings, which are dual-written
    /// from these on save so the still-live legacy app keeps reading.
    /// </summary>
    public ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A job card — the new-side aggregate, mapped onto the adopted legacy <c>jobs_m</c> table (Phase 6,
/// slice 3). A service/repair tracking document, not a sale.
/// </summary>
/// <remarks>
/// The lightest document in the engine: it <b>charges nothing, issues nothing and is taxed at nothing</b>
/// — there is no ledger, no stock and no line totals. What it tracks is work: a customer's equipment
/// (serial-tracked lines), the fault, and the PENDING → CLOSED workflow that records the cost/sell and
/// completion when the job is done.
///
/// <para><b>Structured, serial-tracked lines — a new capability.</b> The legacy app stored line items as a
/// single newline-delimited text blob in <c>jobs_m.items</c> with one free-text "serial" per line
/// regardless of quantity. Here each line is a real <see cref="JobCardLine"/> row carrying its own serial
/// (one row per unit), so serials are searchable and editable; the legacy <c>items</c> blob is still
/// dual-written so the legacy Crystal job sheet keeps printing.</para>
///
/// <para><b>Additive adoption.</b> <c>jobs_m</c> is a fully NOT NULL, keyless, denormalized table — every
/// column must be populated on insert. The migration adds a surrogate <c>id</c> primary key and the typed
/// columns beside the legacy varchars, which the save keeps in step (including the misspelled legacy
/// <c>dompleteddt</c>). <c>company_id</c> already exists (multi-company migration); <c>jobno</c>,
/// <c>contactperson</c>, <c>faultD</c>, <c>remarks</c>, <c>jobdoneby</c>, <c>jstat</c> and
/// <c>completionremarks</c> are shared columns mapped directly.</para>
/// </remarks>
public class JobCard : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration; <c>jobs_m</c> has no key of any kind.</summary>
    public long Id { get; set; }

    /// <summary>The job-card number, allocated transactionally from <c>document_series</c> (legacy <c>jobno</c>).</summary>
    public string Number { get; set; } = null!;

    /// <summary>The trading entity. Nullable to match the multi-company <c>company_id</c>; always set on a new card.</summary>
    public long? CompanyId { get; set; }

    /// <summary>The customer, by surrogate key — a real reference, not the legacy <c>customer</c> code.</summary>
    public long CustomerId { get; set; }

    /// <summary>The date the job was booked in. Typed, not the legacy <c>jdate</c> varchar.</summary>
    public DateOnly Date { get; set; }

    public string? ContactPerson { get; set; }

    /// <summary>What is wrong with the equipment (legacy <c>faultD</c>).</summary>
    public string? FaultDescription { get; set; }

    /// <summary>Free-text remarks captured at booking (legacy <c>remarks</c>).</summary>
    public string? Remarks { get; set; }

    /// <summary>The technician the job is assigned to (legacy <c>jobdoneby</c>).</summary>
    public string? Technician { get; set; }

    /// <summary>The user who booked it, by id — not the legacy <c>enteredby</c> name string.</summary>
    public long? EnteredBy { get; set; }

    /// <summary>When it was booked (UTC). Typed, not the legacy <c>entereddt</c> varchar.</summary>
    public DateTime EnteredAt { get; set; }

    /// <summary><c>PENDING</c> until closed, then <c>CLOSED</c> (legacy <c>jstat</c>). The only two states.</summary>
    public string Status { get; set; } = JobCardStatus.Pending;

    // --- Set on close ------------------------------------------------------------------------------

    /// <summary>The cost of the work, recorded at close (legacy <c>cost</c>). Null until closed.</summary>
    public decimal? Cost { get; set; }

    /// <summary>What the customer is charged, recorded at close (legacy <c>sell</c>). Null until closed.</summary>
    public decimal? Sell { get; set; }

    /// <summary>Remarks recorded when the job is completed (legacy <c>completionremarks</c>).</summary>
    public string? CompletionRemarks { get; set; }

    /// <summary>Who closed it, by user id — not the legacy <c>completedby</c> name string. Null until closed.</summary>
    public long? CompletedBy { get; set; }

    /// <summary>When it was closed (UTC) — the properly-spelled column beside the legacy misspelled <c>dompleteddt</c>. Null until closed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>True once the job has been closed.</summary>
    public bool IsClosed => Status == JobCardStatus.Closed;

    /// <summary><c>new</c> for job cards this app raises; legacy rows are <c>legacy</c> and excluded by a query filter.</summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<JobCardLine> Lines { get; set; } = new List<JobCardLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>The two states a job card moves through — the legacy <c>jstat</c> values, verbatim.</summary>
public static class JobCardStatus
{
    public const string Pending = "PENDING";
    public const string Closed = "CLOSED";
}

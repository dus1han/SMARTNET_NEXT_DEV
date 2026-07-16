namespace Smartnet.Domain.Documents;

/// <summary>One serial-tracked line of a job card being created — one unit of the customer's equipment.</summary>
public sealed record NewJobCardLine(long? ItemId, string? Description, string? Serial);

/// <summary>A whole job card, booked in at once.</summary>
public sealed record NewJobCard(
    long CompanyId,
    long CustomerId,
    DateOnly Date,
    string? ContactPerson,
    string? FaultDescription,
    string? Remarks,
    string? Technician,
    IReadOnlyList<NewJobCardLine> Lines);

/// <summary>What the caller gets back — enough to show a toast and route to the new job card.</summary>
public sealed record JobCardCreated(long Id, string Number);

/// <summary>
/// Creates a job card — the whole of it, in one transaction (Phase 6, slice 3).
/// </summary>
/// <remarks>
/// The lightest creator in the engine: no tax, no ledger, no stock. It allocates the number transactionally
/// from <c>document_series</c>, writes the header and its structured serial lines (dual-writing the legacy
/// <c>items</c> blob and every NOT NULL <c>jobs_m</c> column so the legacy Crystal sheet still prints), and
/// takes a version-1 snapshot — all or none. The card is booked <c>PENDING</c>.
/// </remarks>
public interface IJobCardCreator
{
    Task<JobCardCreated> CreateAsync(NewJobCard request, CancellationToken cancellationToken = default);
}

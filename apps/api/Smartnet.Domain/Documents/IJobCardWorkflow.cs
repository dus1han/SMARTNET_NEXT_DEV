namespace Smartnet.Domain.Documents;

/// <summary>The figures and remarks recorded when a job is completed.</summary>
public sealed record CloseJobCard(decimal Cost, decimal Sell, string? CompletionRemarks);

/// <summary>Thrown when a job card that is not PENDING is closed — the legacy re-close hazard, refused.</summary>
public sealed class JobCardNotPendingException(string number)
    : Exception($"Job card {number} is not open, so it cannot be closed again.")
{
    public string Number { get; } = number;
}

/// <summary>
/// The job-card close workflow — the guarded <c>PENDING → CLOSED</c> transition (Phase 6, slice 3).
/// </summary>
/// <remarks>
/// Closing records the cost, sell and completion remarks, stamps who closed it and when, flips the status
/// and writes a new version snapshot — in one transaction. It is <b>guarded</b>: the card must be
/// <c>PENDING</c> (a second close is refused — the legacy <c>Session["selectedjno"]</c> re-close hazard),
/// it is <c>row_version</c>-checked, and a reason is required by the endpoint. Closing raises <b>no
/// invoice and moves no stock</b> — it is a status event, not a sale.
/// </remarks>
public interface IJobCardWorkflow
{
    Task CloseAsync(long jobCardId, CloseJobCard request, int expectedRowVersion, CancellationToken cancellationToken = default);
}

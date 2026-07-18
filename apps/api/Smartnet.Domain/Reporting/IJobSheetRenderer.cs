namespace Smartnet.Domain.Reporting;

/// <summary>Renders a job card to a printable job-sheet PDF, in the layout of the card's own company.</summary>
public interface IJobSheetRenderer
{
    /// <summary>The job sheet for <paramref name="jobId"/> as a PDF, or null if the job does not exist.</summary>
    Task<byte[]?> RenderAsync(long jobId, CancellationToken cancellationToken = default);
}

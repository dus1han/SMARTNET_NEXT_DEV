namespace Smartnet.Domain.Reporting;

/// <summary>How a data exception is corrected — each writes a real, audited change that makes the data
/// consistent, so the exception then stops being detected (self-clearing), never a silent edit.</summary>
public enum DataExceptionResolution
{
    /// <summary>A duplicate payment group: drop the extra rows, recompute the balance, reverse their GL.</summary>
    RemoveDuplicatePayments,

    /// <summary>A credit invoice settled with no payment: the money <b>was</b> received — record the missing
    /// payment (posts the receipt to the GL, clearing the still-open receivable). The balance stays settled.</summary>
    RecordPayment,

    /// <summary>A credit invoice settled with no payment: the balance was zeroed <b>in error</b> — restore the
    /// receivable so the invoice shows as owed again.</summary>
    RestoreReceivable,
}

/// <summary>
/// Applies a permission-gated, audited correction to a data exception (LEGACY-DATA-POLICY §4). Each correction
/// is a real change — a payment recorded, a duplicate removed, a balance restored — carrying the actor's reason,
/// done in one transaction, and dual-writing the legacy shadow so the still-live legacy reports stay in step.
/// </summary>
public interface IDataExceptionResolver
{
    /// <param name="reference">The invoice number the exception sits on.</param>
    /// <param name="reason">Why the correction is being made — required, recorded in the audit log.</param>
    Task ResolveAsync(
        DataExceptionResolution resolution,
        string reference,
        string reason,
        CancellationToken cancellationToken = default);
}

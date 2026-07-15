namespace Smartnet.Domain.Auditing;

/// <summary>
/// The ambient "who, why, from where" of the current request. Populated by middleware at the
/// edge and read by the SaveChanges interceptor at the persistence layer, so that no endpoint
/// has to pass it down by hand — and therefore no endpoint can forget to.
/// </summary>
public interface IChangeContext
{
    /// <summary>The acting user's id. Null only for anonymous requests (a failed login).</summary>
    long? UserId { get; }

    /// <summary>The company the request is acting within, if any.</summary>
    long? CompanyId { get; }

    /// <summary>
    /// The <c>X-Change-Reason</c> header. Mandatory for the actions listed in AUDIT.md §5 —
    /// enforced server-side, not by a hopeful frontend.
    /// </summary>
    string? Reason { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }

    /// <summary>Ties the audit row to a request and to the structured logs.</summary>
    string? CorrelationId { get; }
}

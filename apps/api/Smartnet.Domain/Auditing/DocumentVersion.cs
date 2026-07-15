namespace Smartnet.Domain.Auditing;

/// <summary>
/// A complete point-in-time snapshot of a financial document.
/// </summary>
/// <remarks>
/// The audit log answers "what changed?". This answers "what did the document <i>look like</i> on
/// 3 March — print it." Those are different questions and need different storage.
/// <para>
/// <b>Version 1 is written at creation</b>, not just on edit. Otherwise the original is the one
/// version you cannot recover.
/// </para>
/// <para>
/// The snapshot is <b>self-contained</b>: it carries the resolved tax rates, the company header
/// values and the line data exactly as they stood at save. Reprinting version 2 of an invoice
/// from last year therefore reproduces <i>that</i> document — not today's VAT rate applied to
/// last year's lines. This is precisely what the legacy system gets wrong.
/// </para>
/// </remarks>
public class DocumentVersion
{
    public long Id { get; set; }

    public long? CompanyId { get; set; }

    /// <summary>INVOICE | QUOTATION | CN | PO | SUPINV | JOBCARD | PAYMENT | CHEQUE | EXPENSE.</summary>
    public string DocType { get; set; } = null!;

    public long DocId { get; set; }

    /// <summary>1-based. Version 1 is the document as first created.</summary>
    public int VersionNo { get; set; }

    /// <summary>The complete header + lines + resolved tax, as saved. JSON.</summary>
    public string Snapshot { get; set; } = null!;

    public long? ChangedBy { get; set; }

    /// <summary>UTC.</summary>
    public DateTime ChangedAt { get; set; }

    public string? Reason { get; set; }
}

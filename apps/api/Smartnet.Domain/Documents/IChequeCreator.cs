namespace Smartnet.Domain.Documents;

/// <summary>A new cheque to record. <paramref name="SupplierId"/> is set only for a <c>Supplier</c> entry.</summary>
public sealed record NewCheque(
    long CompanyId,
    string EntryType,
    string PayTo,
    long? SupplierId,
    string? Bank,
    string? ChequeNumber,
    decimal Amount,
    DateOnly? ChequeDate,
    DateOnly? DueDate);

/// <summary>What the caller gets back after recording a cheque.</summary>
public sealed record ChequeCreated(long Id, decimal Amount);

/// <summary>
/// Records a cheque — a validated, audited write that dual-writes the full legacy <c>cheques</c> row so the
/// surviving <c>ChequeReport</c> keeps reading (Phase 7, slice 2). Standalone: no ledger, no balance.
/// </summary>
public interface IChequeCreator
{
    Task<ChequeCreated> CreateAsync(NewCheque request, CancellationToken cancellationToken = default);
}

/// <summary>Voids a cheque — soft, reason-gated (not the legacy hard delete).</summary>
public interface IChequeVoider
{
    Task VoidAsync(long chequeId, int expectedRowVersion, CancellationToken cancellationToken = default);
}

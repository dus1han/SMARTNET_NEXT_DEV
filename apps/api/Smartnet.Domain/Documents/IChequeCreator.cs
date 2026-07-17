namespace Smartnet.Domain.Documents;

/// <summary>Where a cheque was raised from — so it is not counted as a money event twice.</summary>
public static class ChequeSource
{
    /// <summary>Raised as the method of a supplier payment; the payment is the money event, this prints it.</summary>
    public const string SupplierPayment = "SupplierPayment";

    /// <summary>Raised as the method of an expense; the expense is the money event, this prints it.</summary>
    public const string Expense = "Expense";
}

/// <summary>
/// A new cheque to record. <paramref name="SupplierId"/> is set only for a <c>Supplier</c> entry;
/// <paramref name="SourceType"/>/<paramref name="SourceId"/> tie it to the supplier payment or expense it was
/// raised for (both <c>null</c> for a standalone/manual cheque).
/// </summary>
public sealed record NewCheque(
    long CompanyId,
    string EntryType,
    string PayTo,
    long? SupplierId,
    string? Bank,
    string? ChequeNumber,
    decimal Amount,
    DateOnly? ChequeDate,
    DateOnly? DueDate,
    string? SourceType = null,
    long? SourceId = null);

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

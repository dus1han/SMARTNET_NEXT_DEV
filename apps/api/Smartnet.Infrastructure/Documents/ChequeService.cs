using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Records cheques in the cheque register (Phase 7, slice 2) — a validated, audited write on the adopted
/// legacy <c>cheques</c> table. Standalone: no ledger, no balance.
/// </summary>
/// <remarks>
/// The typed columns are the source of truth; the legacy <c>varchar</c> columns are dual-written beside them
/// (via shadow properties) so the surviving <c>ChequeReport</c> reads a whole row. Void is soft and
/// reason-gated — not the legacy hard delete.
/// </remarks>
public sealed class ChequeService : IChequeCreator, IChequeVoider, IChequePrintRecorder
{
    private readonly SmartnetDbContext _db;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public ChequeService(SmartnetDbContext db, IChangeContext change, TimeProvider time)
    {
        _db = db;
        _change = change;
        _time = time;
    }

    /// <summary>
    /// Stamps the cheque as printed, typed column and legacy shadow together.
    /// </summary>
    /// <remarks>
    /// A reprint is allowed and overwrites this, exactly as the legacy app did — the difference is that
    /// the audit trail now keeps every one of them, so this column being "the last print" is a summary
    /// rather than the whole record.
    /// </remarks>
    public async Task<DateTime> RecordPrintAsync(long chequeId, CancellationToken cancellationToken = default)
    {
        var cheque = await _db.Cheques
            .FirstOrDefaultAsync(c => c.Id == chequeId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cheque {chequeId} does not exist.");

        var printedAt = _time.GetUtcNow().UtcDateTime;

        cheque.PrintedAt = printedAt;
        _db.Entry(cheque).Property(ChequeLegacyShadow.PrintedDt).CurrentValue =
            printedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return printedAt;
    }

    public async Task<ChequeCreated> CreateAsync(NewCheque request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        // A Supplier entry links to a supplier; a Manual entry is free-text and links to none.
        string supplierCode = string.Empty;
        long? supplierId = null;
        if (string.Equals(request.EntryType, "Supplier", StringComparison.OrdinalIgnoreCase) && request.SupplierId is { } sid)
        {
            var supplier = await _db.Suppliers
                .FirstOrDefaultAsync(s => s.Id == sid, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Supplier {sid} does not exist.");
            supplierId = supplier.Id;
            supplierCode = supplier.Code ?? string.Empty;
        }

        var cheque = new Cheque
        {
            CompanyId = request.CompanyId,
            SupplierId = supplierId,
            EntryType = string.Equals(request.EntryType, "Supplier", StringComparison.OrdinalIgnoreCase) ? "Supplier" : "Manual",
            PayTo = request.PayTo,
            SupplierCode = supplierCode,
            Bank = request.Bank ?? string.Empty,
            ChequeNumber = request.ChequeNumber ?? string.Empty,
            Amount = request.Amount,
            ChequeDate = request.ChequeDate,
            DueDate = request.DueDate,
            PrintedAt = null,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            DataOrigin = "new",
        };

        _db.Cheques.Add(cheque);
        SetLegacyShadow(cheque, await ActingUserNameAsync(cancellationToken).ConfigureAwait(false));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ChequeCreated(cheque.Id, cheque.Amount);
    }

    public async Task VoidAsync(long chequeId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var cheque = await _db.Cheques
            .FirstOrDefaultAsync(c => c.Id == chequeId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cheque {chequeId} does not exist.");

        if (cheque.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This cheque was changed by someone else while you were viewing it.");
        }

        // Soft delete — the legacy delete hard-removed the row; here its history is kept.
        cheque.DeletedAt = _time.GetUtcNow().UtcDateTime;
        cheque.DeletedBy = _change.UserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the legacy varchar columns beside the typed ones so the surviving ChequeReport reads a whole row.</summary>
    private void SetLegacyShadow(Cheque cheque, string enteredBy)
    {
        var entry = _db.Entry(cheque);
        void Set(string name, string value) => entry.Property(name).CurrentValue = value;

        Set(ChequeLegacyShadow.ChequeDate, DateText(cheque.ChequeDate));
        Set(ChequeLegacyShadow.DueDate, DateText(cheque.DueDate));
        Set(ChequeLegacyShadow.Amount, cheque.Amount.ToString(CultureInfo.InvariantCulture));
        Set(ChequeLegacyShadow.Company, cheque.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        Set(ChequeLegacyShadow.CreatedBy, enteredBy);
        Set(ChequeLegacyShadow.CreatedDt, _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(ChequeLegacyShadow.PrintedDt, string.Empty); // not printed here — printing is Phase 8
    }

    private static string DateText(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private async Task<string> ActingUserNameAsync(CancellationToken cancellationToken)
    {
        if (_change.UserId is not { } userId)
        {
            return "system";
        }

        var name = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name ?? u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return name ?? "system";
    }
}

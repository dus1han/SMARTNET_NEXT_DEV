using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IReceivableSuspensions"/>
/// <remarks>
/// Every move writes three things in one transaction, the same three a receipt writes:
/// <list type="bullet">
///   <item>a receivables-ledger entry tied to the invoice — the truth every derived outstanding reads;</item>
///   <item><c>invoice_h.balance</c> set to the freshly derived outstanding — the shadow the dashboard,
///   reports, dunning and the old app still read, written absolute so it cannot drift;</item>
///   <item>a GL posting between Accounts Receivable and Suspended Receivables, so the trial balance
///   still shows the money as owed, just not as collectable.</item>
/// </list>
/// </remarks>
public sealed class ReceivableSuspensionService : IReceivableSuspensions
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly IGeneralLedger _gl;
    private readonly ICustomerReceiptCreator _receipts;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public ReceivableSuspensionService(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        IGeneralLedger gl,
        ICustomerReceiptCreator receipts,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _gl = gl;
        _receipts = receipts;
        _change = change;
        _time = time;
    }

    public async Task<SuspensionRecorded> SuspendAsync(SuspendReceivable request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0m)
        {
            throw new InvalidOperationException("The amount to suspend must be positive.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // A receipt or a second suspension of the same invoice waits here, then sees this one's outstanding.
        await LockInvoiceAsync(request.InvoiceId, cancellationToken).ConfigureAwait(false);

        var invoice = await _legacy.InvoiceHs
            .Where(h => h.Id == request.InvoiceId)
            .Select(h => new { h.Id, h.Invoiceno, h.Company })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Invoice {request.InvoiceId} does not exist.");

        var number = invoice.Invoiceno ?? invoice.Id.ToString(CultureInfo.InvariantCulture);

        // The GL posting is per company, and an invoice whose company will not read cannot be posted.
        if (!long.TryParse(invoice.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId))
        {
            throw new InvalidOperationException($"Invoice {number} names no readable company, so it cannot be suspended.");
        }

        var active = await _db.SuspendedReceivables
            .AnyAsync(s => s.InvoiceId == invoice.Id && s.Status == SuspendedReceivableStatus.Suspended, cancellationToken)
            .ConfigureAwait(false);
        if (active)
        {
            throw new InvoiceAlreadySuspendedException(number);
        }

        var (outstanding, customerId) = await DerivedOutstandingAsync(invoice.Id, cancellationToken).ConfigureAwait(false);
        if (customerId is null || request.Amount > outstanding)
        {
            throw new SuspensionExceedsOutstandingException(number, outstanding, request.Amount);
        }

        var suspension = new SuspendedReceivable
        {
            CompanyId = companyId,
            CustomerId = customerId.Value,
            InvoiceId = invoice.Id,
            InvoiceNumber = number,
            Amount = request.Amount,
            SuspendedOn = request.Date,
            Reason = request.Reason.Trim(),
            Status = SuspendedReceivableStatus.Suspended,
        };
        _db.SuspendedReceivables.Add(suspension);

        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = customerId.Value,
            Type = LedgerEntryType.Suspended,
            Amount = -request.Amount,
            InvoiceId = invoice.Id,
            OccurredAt = request.Date.ToDateTime(TimeOnly.MinValue),
            Note = $"Suspended: {suspension.Reason}",
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // assigns the suspension's id

        await WriteLegacyBalanceAsync(invoice.Id, outstanding - request.Amount, cancellationToken).ConfigureAwait(false);

        // Off the collectable receivable, onto the suspended one — still an asset, still owed.
        await _gl.PostAsync(new GlPosting(
            companyId, request.Date, GlSources.ReceivableSuspension, suspension.Id, $"Invoice {number} suspended",
            [
                GlChart.SuspendedReceivable(request.Amount, 0m),
                GlChart.Receivable(0m, request.Amount),
            ]), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SuspensionRecorded(suspension.Id, suspension.Amount);
    }

    public async Task ReinstateAsync(long suspensionId, int expectedRowVersion, string? note, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ReinstateInTransactionAsync(suspensionId, expectedRowVersion, note, SuspendedReceivableStatus.Reinstated, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomerReceiptCreated> SettleAsync(
        long suspensionId, int expectedRowVersion, SettleSuspension payment, CancellationToken cancellationToken = default)
    {
        // A resubmit of a settle that already went through returns its receipt and moves nothing again.
        var existing = await _db.CustomerReceipts
            .FirstOrDefaultAsync(r => r.IdempotencyKey == payment.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new CustomerReceiptCreated(existing.Id, existing.Amount, AlreadyExisted: true);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var suspension = await ReinstateInTransactionAsync(
                suspensionId, expectedRowVersion, payment.Reference, SuspendedReceivableStatus.Settled, cancellationToken)
            .ConfigureAwait(false);

        // The ordinary receipt, against the invoice now owing again — same validation, ledger entry, legacy
        // payments row and GL posting as one taken on the Payments screen. It joins this transaction.
        var receipt = await _receipts.CreateAsync(
            new NewCustomerReceipt(
                suspension.CompanyId,
                suspension.CustomerId,
                payment.Date,
                payment.Method,
                payment.Reference,
                payment.IdempotencyKey,
                [new NewReceiptAllocation(suspension.InvoiceId, payment.Amount)]),
            cancellationToken).ConfigureAwait(false);

        suspension.ReceiptId = receipt.Id;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return receipt;
    }

    /// <summary>Puts the whole suspended amount back on its invoice, in the caller's transaction.</summary>
    private async Task<SuspendedReceivable> ReinstateInTransactionAsync(
        long suspensionId, int expectedRowVersion, string? note, SuspendedReceivableStatus outcome, CancellationToken cancellationToken)
    {
        var invoiceId = await _db.SuspendedReceivables
            .Where(s => s.Id == suspensionId)
            .Select(s => (long?)s.InvoiceId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Suspension {suspensionId} does not exist.");

        await LockInvoiceAsync(invoiceId, cancellationToken).ConfigureAwait(false);

        var suspension = await _db.SuspendedReceivables
            .FirstAsync(s => s.Id == suspensionId, cancellationToken)
            .ConfigureAwait(false);

        if (suspension.Status != SuspendedReceivableStatus.Suspended)
        {
            throw new SuspensionNotActiveException(suspension.Id);
        }

        // Reinstate a stale copy → concurrency conflict, as everywhere else.
        _db.Entry(suspension).Property(s => s.RowVersion).OriginalValue = expectedRowVersion;

        var now = _time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        suspension.Status = outcome;
        suspension.ReinstatedAt = now;
        suspension.ReinstatedBy = _change.UserId;
        suspension.ReinstateNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = suspension.CustomerId,
            Type = LedgerEntryType.Reinstated,
            Amount = suspension.Amount,
            InvoiceId = suspension.InvoiceId,
            OccurredAt = now,
            Note = $"Reinstated from the suspended account (suspension {suspension.Id})",
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (outstanding, _) = await DerivedOutstandingAsync(suspension.InvoiceId, cancellationToken).ConfigureAwait(false);
        await WriteLegacyBalanceAsync(suspension.InvoiceId, outstanding, cancellationToken).ConfigureAwait(false);

        await _gl.PostAsync(new GlPosting(
            suspension.CompanyId, today, GlSources.ReceivableReinstatement, suspension.Id,
            $"Invoice {suspension.InvoiceNumber} reinstated",
            [
                GlChart.Receivable(suspension.Amount, 0m),
                GlChart.SuspendedReceivable(0m, suspension.Amount),
            ]), cancellationToken).ConfigureAwait(false);

        return suspension;
    }

    private Task<int> LockInvoiceAsync(long invoiceId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlAsync($"SELECT id FROM invoice_h WHERE id = {invoiceId} FOR UPDATE", cancellationToken);

    /// <summary>The invoice's derived outstanding and its customer, summed from the receivables ledger.</summary>
    private async Task<(decimal Outstanding, long? CustomerId)> DerivedOutstandingAsync(long invoiceId, CancellationToken cancellationToken)
    {
        var row = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId == invoiceId)
            .GroupBy(e => e.InvoiceId)
            .Select(g => new { Outstanding = g.Sum(e => e.Amount), CustomerId = g.Max(e => e.CustomerId) })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? (0m, null) : (row.Outstanding, row.CustomerId);
    }

    /// <summary>The legacy shadow: <c>invoice_h.balance</c> set to the derived outstanding — absolute, never a decrement.</summary>
    private Task<int> WriteLegacyBalanceAsync(long invoiceId, decimal balance, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlAsync(
            $"UPDATE `invoice_h` SET `balance` = {balance.ToString(CultureInfo.InvariantCulture)} WHERE `id` = {invoiceId}",
            cancellationToken);
}

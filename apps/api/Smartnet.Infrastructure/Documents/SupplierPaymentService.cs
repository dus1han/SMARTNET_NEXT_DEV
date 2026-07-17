using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Records supplier payments (Phase 7) — money paid, allocated across one or more open supplier invoices.
/// The payables mirror of <see cref="CustomerReceiptService"/>.
/// </summary>
/// <remarks>
/// The truth of every allocation is a payables-ledger <see cref="PayablesLedgerEntryType.Payment"/> entry,
/// from which the outstanding is derived. Beside it the save <b>dual-writes the legacy shadow</b> so the
/// still-live supplier-payment report keeps reading: a <c>supplier_inv_pay</c> row per invoice, and
/// <c>paymentstat='Paid'</c> once an invoice's derived outstanding reaches zero. An idempotency key dedupes a
/// resubmit; the whole thing is one transaction. It settles new and adopted-legacy supplier invoices alike
/// (a legacy invoice's payable is its seeded opening balance).
/// </remarks>
public sealed class SupplierPaymentService : ISupplierPaymentCreator, ISupplierPaymentVoider
{
    private readonly SmartnetDbContext _db;
    private readonly IChequeCreator _cheques;
    private readonly IGeneralLedger _gl;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public SupplierPaymentService(SmartnetDbContext db, IChequeCreator cheques, IGeneralLedger gl, IChangeContext change, TimeProvider time)
    {
        _db = db;
        _cheques = cheques;
        _gl = gl;
        _change = change;
        _time = time;
    }

    public async Task<SupplierPaymentCreated> CreateAsync(NewSupplierPayment request, CancellationToken cancellationToken = default)
    {
        // Idempotency — a resubmit with the same key returns the first payment, not a second one.
        var existing = await _db.SupplierPayments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new SupplierPaymentCreated(existing.Id, existing.Amount, AlreadyExisted: true);
        }

        if (request.Allocations.Count == 0)
        {
            throw new InvalidOperationException("A payment must allocate to at least one invoice.");
        }

        _ = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Supplier {request.SupplierId} does not exist.");

        var invoiceIds = request.Allocations.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        var outstanding = await DerivedOutstandingAsync(invoiceIds, cancellationToken).ConfigureAwait(false);

        foreach (var group in request.Allocations.GroupBy(a => a.SupplierInvoiceId))
        {
            if (!outstanding.TryGetValue(group.Key, out var info) || info.SupplierId != request.SupplierId)
            {
                throw new SupplierPaymentInvoiceMismatchException(group.Key);
            }

            var applied = group.Sum(a => a.Amount);
            if (applied <= 0m)
            {
                throw new InvalidOperationException($"The allocation to supplier invoice {group.Key} must be positive.");
            }
            if (applied > info.Outstanding)
            {
                throw new SupplierPaymentAllocationExceedsOutstandingException(group.Key, info.Outstanding, applied);
            }
        }

        var total = request.Allocations.Sum(a => a.Amount);
        var occurredAt = request.Date.ToDateTime(TimeOnly.MinValue);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var payment = new SupplierPayment
        {
            CompanyId = request.CompanyId,
            SupplierId = request.SupplierId,
            Date = request.Date,
            Amount = total,
            Method = request.Method,
            Reference = request.Reference,
            IdempotencyKey = request.IdempotencyKey,
            DataOrigin = "new",
            Allocations = request.Allocations
                .Select(a => new SupplierPaymentAllocation { SupplierInvoiceId = a.SupplierInvoiceId, Amount = a.Amount })
                .ToList(),
        };
        _db.SupplierPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + allocations + audit

        foreach (var group in request.Allocations.GroupBy(a => a.SupplierInvoiceId))
        {
            var applied = group.Sum(a => a.Amount);
            var info = outstanding[group.Key];

            // The payment reduces what we owe — a negative payables entry, tied to the invoice.
            _db.PayablesLedger.Add(new PayablesLedgerEntry
            {
                SupplierId = request.SupplierId,
                Type = PayablesLedgerEntryType.Payment,
                Amount = -applied,
                SupplierInvoiceId = group.Key,
                OccurredAt = occurredAt,
                Note = request.Reference,
            });

            await DualWritePaymentAsync(group.Key, request.Date, request.Reference, request.Method, cancellationToken).ConfigureAwait(false);

            if (info.Outstanding - applied == 0m)
            {
                await _db.Database.ExecuteSqlAsync(
                    $"UPDATE `supplier_invoice` SET `paymentstat` = 'Paid' WHERE `id` = {group.Key}",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // the ledger entries + audit

        // The general-ledger entry: money out — Dr Accounts Payable, Cr Cash/Bank for the whole payment
        // (its allocations settle individual invoices in the payables ledger; the GL sees the movement).
        await _gl.PostAsync(new GlPosting(
            request.CompanyId, request.Date, GlSources.SupplierPayment, payment.Id, request.Reference,
            [
                GlChart.Payable(total, 0m),
                GlChart.CashOrBank(request.Method, 0m, total),
            ]), cancellationToken).ConfigureAwait(false);

        // Paid by cheque → raise a printable cheque linked to this payment (the payment is the money event,
        // the cheque only prints it — so it is never double-counted).
        if (string.Equals(request.Method, "Cheque", StringComparison.OrdinalIgnoreCase))
        {
            await _cheques.CreateAsync(new NewCheque(
                request.CompanyId, "Supplier", supplier.Name ?? "Supplier", request.SupplierId,
                request.ChequeBank, request.ChequeNumber, total,
                request.ChequeDate ?? request.Date, request.ChequeDueDate ?? request.Date,
                ChequeSource.SupplierPayment, payment.Id), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SupplierPaymentCreated(payment.Id, payment.Amount, AlreadyExisted: false);
    }

    public async Task VoidAsync(long paymentId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var payment = await _db.SupplierPayments
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Supplier payment {paymentId} does not exist.");

        if (payment.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This payment was changed by someone else while you were viewing it.");
        }

        var now = _time.GetUtcNow().UtcDateTime;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in payment.Allocations.GroupBy(a => a.SupplierInvoiceId))
        {
            var applied = group.Sum(a => a.Amount);

            // Reverse the payment with a compensating purchase — we owe it again (append-only; the original
            // entries are never rewritten). Its positive sign restores the derived payable.
            _db.PayablesLedger.Add(new PayablesLedgerEntry
            {
                SupplierId = payment.SupplierId,
                Type = PayablesLedgerEntryType.Purchase,
                Amount = applied,
                SupplierInvoiceId = group.Key,
                OccurredAt = now,
                Note = $"Payment {payment.Id} voided",
            });

            // The invoice is no longer fully settled — reopen the legacy flag for the surviving report.
            await _db.Database.ExecuteSqlAsync(
                $"UPDATE `supplier_invoice` SET `paymentstat` = 'Pending' WHERE `id` = {group.Key}",
                cancellationToken).ConfigureAwait(false);
        }

        payment.DeletedAt = now;
        payment.DeletedBy = _change.UserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Reverse the payment's GL entry — Dr Cash/Bank, Cr Accounts Payable (money back, we owe it again).
        if (payment.CompanyId is { } companyId)
        {
            await _gl.PostAsync(new GlPosting(
                companyId, DateOnly.FromDateTime(now), GlSources.SupplierPaymentVoid, payment.Id,
                $"Supplier payment {payment.Id} voided",
                [
                    GlChart.CashOrBank(payment.Method, payment.Amount, 0m),
                    GlChart.Payable(0m, payment.Amount),
                ]), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The derived outstanding and owning supplier of each supplier invoice, summed from the payables ledger.</summary>
    private async Task<Dictionary<long, (decimal Outstanding, long SupplierId)>> DerivedOutstandingAsync(
        IReadOnlyList<long> invoiceIds, CancellationToken cancellationToken)
    {
        var rows = await _db.PayablesLedger
            .Where(e => e.SupplierInvoiceId != null && invoiceIds.Contains(e.SupplierInvoiceId.Value))
            .GroupBy(e => e.SupplierInvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Outstanding = g.Sum(e => e.Amount), SupplierId = g.Max(e => e.SupplierId) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.InvoiceId, r => (r.Outstanding, r.SupplierId));
    }

    /// <summary>Writes the legacy shadow for one allocation: a <c>supplier_inv_pay</c> row (no amount column — date/ref/method).</summary>
    private async Task DualWritePaymentAsync(long supplierInvoiceId, DateOnly date, string? reference, string? method, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `supplier_inv_pay` (`supinvid`, `paiddate`, `referenceno`, `pay_method`, `data_origin`)
            VALUES ({supplierInvoiceId.ToString(CultureInfo.InvariantCulture)},
                    {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},
                    {reference},
                    {method ?? "CASH"},
                    'new')
            """,
            cancellationToken).ConfigureAwait(false);
    }
}

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Records customer receipts (Phase 7, slice 1) — money received, allocated across one or more open invoices.
/// The receivables counterpart of <see cref="SupplierInvoiceService"/>'s payment path.
/// </summary>
/// <remarks>
/// The truth of every allocation is a receivables-ledger <see cref="LedgerEntryType.Payment"/> entry, from
/// which the outstanding is derived. Beside it the save <b>dual-writes the legacy shadow</b> so the still-live
/// legacy outstanding report keeps reading: a legacy <c>payments</c> row (one per invoice, exactly as the old
/// <c>savePay</c> wrote) and <c>invoice_h.balance</c> set to the freshly derived outstanding — an absolute value
/// off the ledger rather than the legacy in-place <c>balance = balance - amount</c>, so the shadow can never
/// drift from the truth (B2/B3). An idempotency key makes a double-submit return the first receipt instead of
/// taking the money twice (Finding 1). The whole thing is one transaction.
/// </remarks>
public sealed class CustomerReceiptService : ICustomerReceiptCreator, ICustomerReceiptVoider
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public CustomerReceiptService(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _change = change;
        _time = time;
    }

    public async Task<CustomerReceiptCreated> CreateAsync(NewCustomerReceipt request, CancellationToken cancellationToken = default)
    {
        // Idempotency — a resubmit with the same key returns the first receipt, not a second payment (Finding 1).
        var existing = await _db.CustomerReceipts
            .FirstOrDefaultAsync(r => r.IdempotencyKey == request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new CustomerReceiptCreated(existing.Id, existing.Amount, AlreadyExisted: true);
        }

        if (request.Allocations.Count == 0)
        {
            throw new InvalidOperationException("A receipt must allocate to at least one invoice.");
        }

        _ = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");
        _ = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {request.CustomerId} does not exist.");

        var invoiceIds = request.Allocations.Select(a => a.InvoiceId).Distinct().ToList();
        var outstanding = await DerivedOutstandingAsync(invoiceIds, cancellationToken).ConfigureAwait(false);
        var numbers = await InvoiceNumbersAsync(invoiceIds, cancellationToken).ConfigureAwait(false);

        // Validate each invoice belongs to this customer and is not over-allocated (summing any duplicates).
        foreach (var group in request.Allocations.GroupBy(a => a.InvoiceId))
        {
            if (!outstanding.TryGetValue(group.Key, out var info))
            {
                throw new ReceiptInvoiceCustomerMismatchException(group.Key);
            }
            if (info.CustomerId != request.CustomerId)
            {
                throw new ReceiptInvoiceCustomerMismatchException(group.Key);
            }

            var applied = group.Sum(a => a.Amount);
            if (applied <= 0m)
            {
                throw new InvalidOperationException($"The allocation to invoice {group.Key} must be positive.");
            }
            if (applied > info.Outstanding)
            {
                throw new ReceiptAllocationExceedsOutstandingException(group.Key, info.Outstanding, applied);
            }
        }

        var total = request.Allocations.Sum(a => a.Amount);
        var occurredAt = request.Date.ToDateTime(TimeOnly.MinValue);
        var enteredBy = await ActingUserNameAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var receipt = new CustomerReceipt
        {
            CompanyId = request.CompanyId,
            CustomerId = request.CustomerId,
            Date = request.Date,
            Amount = total,
            Method = request.Method,
            Reference = request.Reference,
            IdempotencyKey = request.IdempotencyKey,
            DataOrigin = "new",
            Allocations = request.Allocations
                .Select(a => new ReceiptAllocation { InvoiceId = a.InvoiceId, Amount = a.Amount })
                .ToList(),
        };
        _db.CustomerReceipts.Add(receipt);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + allocations + audit; assigns ids

        foreach (var group in request.Allocations.GroupBy(a => a.InvoiceId))
        {
            var applied = group.Sum(a => a.Amount);
            var info = outstanding[group.Key];
            var number = numbers[group.Key];

            // The payment reduces what the customer owes — a negative receivables entry, tied to the invoice.
            _db.ReceivablesLedger.Add(new LedgerEntry
            {
                CustomerId = request.CustomerId,
                Type = LedgerEntryType.Payment,
                Amount = -applied,
                InvoiceId = group.Key,
                OccurredAt = occurredAt,
                Note = request.Reference,
            });

            // Dual-write the legacy shadow: a payments row per invoice (as savePay did) and the invoice_h
            // balance set to the freshly derived outstanding (absolute, off the ledger — never a drifting decrement).
            await DualWritePaymentAsync(number, applied, request.Date, request.Method, request.Reference, enteredBy, info.Outstanding - applied, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // the ledger entries + audit
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CustomerReceiptCreated(receipt.Id, receipt.Amount, AlreadyExisted: false);
    }

    public async Task VoidAsync(long receiptId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var receipt = await _db.CustomerReceipts
            .Include(r => r.Allocations)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer receipt {receiptId} does not exist.");

        if (receipt.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This receipt was changed by someone else while you were viewing it.");
        }

        var invoiceIds = receipt.Allocations.Select(a => a.InvoiceId).Distinct().ToList();
        var outstanding = await DerivedOutstandingAsync(invoiceIds, cancellationToken).ConfigureAwait(false);
        var numbers = await InvoiceNumbersAsync(invoiceIds, cancellationToken).ConfigureAwait(false);
        var enteredBy = await ActingUserNameAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in receipt.Allocations.GroupBy(a => a.InvoiceId))
        {
            var applied = group.Sum(a => a.Amount);
            var current = outstanding.TryGetValue(group.Key, out var info) ? info.Outstanding : 0m;
            var number = numbers.GetValueOrDefault(group.Key);

            // Reverse the payment with a compensating charge — the customer owes it again (append-only; the
            // original entries are never rewritten). Its positive sign restores the derived balance.
            _db.ReceivablesLedger.Add(new LedgerEntry
            {
                CustomerId = receipt.CustomerId,
                Type = LedgerEntryType.Charge,
                Amount = applied,
                InvoiceId = group.Key,
                OccurredAt = now,
                Note = $"Receipt {receipt.Id} voided",
            });

            if (number is not null)
            {
                // Legacy shadow: a reversing (negative) payments row so the old detail nets to nothing, and
                // the balance set back to the restored derived outstanding.
                await DualWritePaymentAsync(number, -applied, DateOnly.FromDateTime(now), receipt.Method,
                    $"VOID {receipt.Reference}", enteredBy, current + applied, cancellationToken).ConfigureAwait(false);
            }
        }

        receipt.DeletedAt = now;
        receipt.DeletedBy = _change.UserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The derived outstanding and owning customer of each invoice, summed from the receivables ledger.</summary>
    private async Task<Dictionary<long, (decimal Outstanding, long CustomerId)>> DerivedOutstandingAsync(
        IReadOnlyList<long> invoiceIds, CancellationToken cancellationToken)
    {
        var rows = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId != null && invoiceIds.Contains(e.InvoiceId.Value))
            .GroupBy(e => e.InvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Outstanding = g.Sum(e => e.Amount), CustomerId = g.Max(e => e.CustomerId) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.InvoiceId, r => (r.Outstanding, r.CustomerId));
    }

    /// <summary>The legacy invoice number of each invoice id (new or legacy — both live in invoice_h).</summary>
    private async Task<Dictionary<long, string>> InvoiceNumbersAsync(IReadOnlyList<long> invoiceIds, CancellationToken cancellationToken)
    {
        var rows = await _legacy.InvoiceHs
            .Where(h => invoiceIds.Contains(h.Id) && h.Invoiceno != null)
            .Select(h => new { h.Id, h.Invoiceno })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Id, r => r.Invoiceno!);
    }

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

    /// <summary>
    /// Writes the legacy shadow for one allocation: a <c>payments</c> row (matching <c>savePay</c>'s columns)
    /// and <c>invoice_h.balance</c> set to the derived outstanding. A negative <paramref name="amount"/> is a
    /// void reversal.
    /// </summary>
    private async Task DualWritePaymentAsync(
        string invoiceNumber, decimal amount, DateOnly date, string? method, string? reference, string enteredBy, decimal newBalance, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `payments` (`invoiceno`, `amount`, `paymentrecdate`, `enteredby`, `entereddt`, `paym`, `payref`)
            VALUES ({invoiceNumber},
                    {Money(amount)},
                    {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},
                    {enteredBy},
                    {_time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)},
                    {method ?? "CASH"},
                    {reference ?? string.Empty})
            """,
            cancellationToken).ConfigureAwait(false);

        await _db.Database.ExecuteSqlAsync(
            $"UPDATE `invoice_h` SET `balance` = {Money(newBalance)} WHERE `invoiceno` = {invoiceNumber}",
            cancellationToken).ConfigureAwait(false);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

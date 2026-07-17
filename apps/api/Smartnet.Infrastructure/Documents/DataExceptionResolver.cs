using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Applies the permission-gated, audited corrections behind the Data Exceptions screen (LEGACY-DATA-POLICY §4).
/// </summary>
/// <remarks>
/// Every correction is one transaction and carries the actor's reason to the audit log. It writes real changes
/// (a payment recorded, a duplicate removed, a balance restored) and dual-writes the legacy shadow — legacy
/// writes go through the <see cref="SmartnetDbContext"/>'s own connection (raw SQL) so they share the
/// transaction, exactly as <see cref="CustomerReceiptService"/> does. Because each correction fixes the
/// underlying data, the exception then stops being detected — it self-clears, no "resolved" flag needed.
/// </remarks>
public sealed class DataExceptionResolver : IDataExceptionResolver
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly IGeneralLedger _gl;
    private readonly IChangeContext _change;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public DataExceptionResolver(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        IGeneralLedger gl,
        IChangeContext change,
        IAuditWriter audit,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _gl = gl;
        _change = change;
        _audit = audit;
        _time = time;
    }

    public async Task ResolveAsync(
        DataExceptionResolution resolution,
        string reference,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("A data-exception correction needs an invoice reference.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("A data-exception correction needs a reason.");
        }

        var invoice = await _legacy.InvoiceHs
            .Where(h => h.Invoiceno == reference)
            .Select(h => new { h.Id, h.Invoiceno, h.Company, h.Invtype, h.Totamount, h.Balance, h.Customer })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Invoice {reference} does not exist.");

        var companyId = long.TryParse(invoice.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)
            ? c
            : throw new InvalidOperationException($"Invoice {reference} has no company.");
        var total = LegacyValue.Money(invoice.Totamount);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        switch (resolution)
        {
            case DataExceptionResolution.RemoveDuplicatePayments:
                await RemoveDuplicatePaymentsAsync(reference, total, cancellationToken).ConfigureAwait(false);
                break;
            case DataExceptionResolution.RecordPayment:
                await RecordPaymentAsync(invoice.Id, reference, companyId, total, invoice.Balance, reason, cancellationToken).ConfigureAwait(false);
                break;
            case DataExceptionResolution.RestoreReceivable:
                await RestoreReceivableAsync(invoice.Id, reference, invoice.Customer, total, invoice.Balance, reason, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unknown resolution {resolution}.");
        }

        await _audit.RecordAsync(
            AuditAction.Update,
            "DataException",
            reference,
            userId: _change.UserId,
            reason: reason,
            details: new { resolution = resolution.ToString(), reference, amount = total },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops the duplicate payment rows for one invoice, reverses their GL, and recomputes the balance
    /// off what remains — the per-invoice form of the Finding 1 migration.</summary>
    private async Task RemoveDuplicatePaymentsAsync(string invoiceNo, decimal total, CancellationToken cancellationToken)
    {
        var payments = await _db.Database
            .SqlQuery<PaymentRow>($"SELECT `id`, `amount`, `paymentrecdate` AS `date` FROM `payments` WHERE `invoiceno` = {invoiceNo}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The non-keeper of each (amount, date) group — keep the earliest id, drop the rest.
        var dupIds = payments
            .GroupBy(p => (p.Amount, p.Date))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(p => p.Id).Skip(1))
            .Select(p => (long)p.Id)
            .ToList();

        if (dupIds.Count == 0)
        {
            throw new InvalidOperationException($"Invoice {invoiceNo} has no duplicate payments to remove.");
        }

        // Reverse the duplicate receipts' GL postings — lines then the balanced entry.
        var entryIds = await _db.GlEntries
            .Where(e => e.SourceType == GlSources.LegacyPayment && dupIds.Contains(e.SourceId))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        await _db.GlLines.Where(l => l.GlEntryId != null && entryIds.Contains(l.GlEntryId.Value))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _db.GlEntries.Where(e => entryIds.Contains(e.Id))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        // Drop the duplicate rows and recompute the balance = total − Σ(surviving payments).
        var remaining = payments.Where(p => !dupIds.Contains(p.Id)).Sum(p => LegacyValue.Money(p.Amount));
        var newBalance = total - remaining;

        // A handful of ids; delete each parameterised (ExecuteSqlAsync) rather than build an IN list by hand.
        foreach (var id in dupIds)
        {
            await _db.Database.ExecuteSqlAsync($"DELETE FROM `payments` WHERE `id` = {id}", cancellationToken).ConfigureAwait(false);
        }

        await _db.Database.ExecuteSqlAsync(
            $"UPDATE `invoice_h` SET `balance` = {Money(newBalance)} WHERE `invoiceno` = {invoiceNo}",
            cancellationToken).ConfigureAwait(false);

        await ReseedOpeningBalanceAsync(invoiceNo, newBalance, "Recomputed after removing duplicate payments (data exception)", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records the missing payment for a settled-but-unrecorded invoice: a legacy payments row plus a
    /// GL receipt (Dr Bank, Cr Receivable) that clears the still-open receivable. The balance stays settled.</summary>
    private async Task RecordPaymentAsync(
        long invoiceId, string invoiceNo, long companyId, decimal total, string? balanceRaw, string reason, CancellationToken cancellationToken)
    {
        if (LegacyValue.Money(balanceRaw) != 0m || total <= 0m)
        {
            throw new InvalidOperationException($"Invoice {invoiceNo} is not a settled invoice awaiting a payment record.");
        }

        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var enteredBy = await ActingUserNameAsync(cancellationToken).ConfigureAwait(false);

        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `payments` (`invoiceno`, `amount`, `paymentrecdate`, `enteredby`, `entereddt`, `paym`, `payref`, `data_origin`)
            VALUES ({invoiceNo}, {Money(total)}, {today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},
                    {enteredBy}, {_time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)},
                    {"Bank"}, {"Data exception correction"}, 'new')
            """,
            cancellationToken).ConfigureAwait(false);

        // The receipt the invoice never had — clears its open receivable. Keyed on the invoice, so idempotent.
        await _gl.PostAsync(new GlPosting(
            companyId, today, GlSources.DataExceptionPayment, invoiceId, $"Recorded missing payment for {invoiceNo}",
            [
                GlChart.Bank(total, 0m),
                GlChart.Receivable(0m, total),
            ]), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restores a receivable whose balance was zeroed in error — sets the balance back to the total and
    /// seeds the receivables opening. The GL receivable is already open (the invoice posted, no receipt), so it
    /// needs no change.</summary>
    private async Task RestoreReceivableAsync(
        long invoiceId, string invoiceNo, string? customerCode, decimal total, string? balanceRaw, string reason, CancellationToken cancellationToken)
    {
        if (LegacyValue.Money(balanceRaw) != 0m || total <= 0m)
        {
            throw new InvalidOperationException($"Invoice {invoiceNo} does not have a settled balance to restore.");
        }

        await _db.Database.ExecuteSqlAsync(
            $"UPDATE `invoice_h` SET `balance` = {Money(total)} WHERE `invoiceno` = {invoiceNo}",
            cancellationToken).ConfigureAwait(false);

        await ReseedOpeningBalanceAsync(invoiceNo, total, "Receivable restored — balance was zeroed in error (data exception)", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops any receivables opening for the invoice and re-inserts one for a non-zero balance, so the
    /// derived customer balance matches the corrected legacy balance (mirrors the Phase 5 seed).</summary>
    private async Task ReseedOpeningBalanceAsync(string invoiceNo, decimal balance, string note, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlAsync(
            $"""
            DELETE r FROM `receivables_ledger` r
            JOIN `invoice_h` h ON h.`id` = r.`invoice_id`
            WHERE r.`type` = 'OpeningBalance' AND h.`invoiceno` = {invoiceNo}
            """,
            cancellationToken).ConfigureAwait(false);

        if (balance == 0m)
        {
            return;
        }

        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `receivables_ledger`
                (`customer_id`, `type`, `amount`, `invoice_id`, `occurred_at`, `note`, `created_at`, `row_version`)
            SELECT c.`id`, 'OpeningBalance', {Money(balance)}, h.`id`, {_time.GetUtcNow().UtcDateTime}, {note},
                   {_time.GetUtcNow().UtcDateTime}, 1
            FROM `invoice_h` h JOIN `cus_m` c ON c.`cuscode` = h.`customer`
            WHERE h.`invoiceno` = {invoiceNo}
            """,
            cancellationToken).ConfigureAwait(false);
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

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record PaymentRow(int Id, string? Amount, string? Date);
}

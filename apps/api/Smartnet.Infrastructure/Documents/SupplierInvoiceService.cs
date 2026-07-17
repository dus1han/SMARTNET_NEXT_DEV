using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Records supplier invoices and their payments (Phase 6, slice 2) — the accounts-payable side of the
/// documents engine. Header-only, no stock; the payable and every payment are ledger entries, so the
/// outstanding is derived and partial payments accumulate.
/// </summary>
public sealed class SupplierInvoiceService : ISupplierInvoiceCreator, ISupplierInvoicePayments
{
    private readonly SmartnetDbContext _db;
    private readonly IPayablesLedger _ledger;
    private readonly IGeneralLedger _gl;
    private readonly IDocumentVersionWriter _versions;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public SupplierInvoiceService(
        SmartnetDbContext db,
        IPayablesLedger ledger,
        IGeneralLedger gl,
        IDocumentVersionWriter versions,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _ledger = ledger;
        _gl = gl;
        _versions = versions;
        _change = change;
        _time = time;
    }

    public async Task<SupplierInvoiceCreated> CreateAsync(NewSupplierInvoice request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Supplier {request.SupplierId} does not exist.");

        // One transaction: the header, its Purchase payable entry and the snapshot commit together or not
        // at all. No number is allocated and no stock moves — a supplier invoice is a payable record.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoice = new SupplierInvoice
        {
            SupplierReference = request.SupplierReference,
            CompanyId = request.CompanyId,
            SupplierId = request.SupplierId,
            Date = request.Date,
            NetTotal = request.NetTotal,
            TaxRatePercentage = request.TaxRatePercentage,
            Amount = request.Amount,
            DataOrigin = "new",
        };

        _db.SupplierInvoices.Add(invoice);
        SetLegacyShadow(invoice, supplier, company, paymentStat: "Pending");
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + audit; assigns id

        // The payable: what we now owe the supplier for this invoice.
        _db.PayablesLedger.Add(new PayablesLedgerEntry
        {
            SupplierId = request.SupplierId,
            Type = PayablesLedgerEntryType.Purchase,
            Amount = request.Amount,
            SupplierInvoiceId = invoice.Id,
            OccurredAt = request.Date.ToDateTime(TimeOnly.MinValue),
            Note = null,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _versions
            .WriteAsync(DocumentTypes.SupplierInvoice, invoice.Id, request.CompanyId, Snapshot(invoice, supplier, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        // The general-ledger entry for the purchase: Dr Purchases + Input VAT, Cr Accounts Payable (what we
        // now owe). A later supplier payment moves it from AP to Cash/Bank. Zero VAT lines are dropped.
        await _gl.PostAsync(new GlPosting(
            request.CompanyId, invoice.Date, GlSources.SupplierInvoice, invoice.Id, invoice.SupplierReference,
            [
                GlChart.Purchases(invoice.NetTotal, 0m),
                GlChart.InputVat(invoice.TaxAmount, 0m),
                GlChart.Payable(0m, invoice.Amount),
            ]), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SupplierInvoiceCreated(invoice.Id, invoice.SupplierReference, invoice.Amount);
    }

    public async Task<SupplierPaymentRecorded> RecordPaymentAsync(long supplierInvoiceId, RecordSupplierPayment payment, CancellationToken cancellationToken = default)
    {
        // The supplier comes off the payable, not the header — so this settles adopted legacy invoices too
        // (their supplier_id column is NULL, but their seeded OpeningBalance entry carries the resolved id).
        var supplierId = await _db.PayablesLedger
            .Where(e => e.SupplierInvoiceId == supplierInvoiceId)
            .Select(e => e.SupplierId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (supplierId == 0)
        {
            throw new InvalidOperationException($"Supplier invoice {supplierInvoiceId} does not exist or has no payable to settle.");
        }

        var outstanding = await _ledger.OutstandingForInvoiceAsync(supplierInvoiceId, cancellationToken).ConfigureAwait(false);
        if (payment.Amount > outstanding)
        {
            throw new SupplierPaymentExceedsOutstandingException(outstanding, payment.Amount);
        }

        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // The payment reduces what we owe — a negative payable entry, tied to the invoice.
        var payableEntry = new PayablesLedgerEntry
        {
            SupplierId = supplierId,
            Type = PayablesLedgerEntryType.Payment,
            Amount = -payment.Amount,
            SupplierInvoiceId = supplierInvoiceId,
            OccurredAt = payment.Date.ToDateTime(TimeOnly.MinValue),
            Note = payment.Reference,
        };
        _db.PayablesLedger.Add(payableEntry);

        // Dual-write the legacy supplier_inv_pay row so the legacy supplier-payment report keeps reading.
        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `supplier_inv_pay` (`supinvid`, `paiddate`, `referenceno`, `pay_method`, `data_origin`)
            VALUES ({supplierInvoiceId.ToString(CultureInfo.InvariantCulture)},
                    {payment.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},
                    {payment.Reference},
                    {payment.Method ?? "CASH"},
                    'new')
            """,
            cancellationToken).ConfigureAwait(false);

        var newOutstanding = outstanding - payment.Amount;
        if (newOutstanding == 0m)
        {
            // "Paid" is a derived fact (Σ ledger = 0); the legacy flag is dual-written for legacy readers.
            await _db.Database.ExecuteSqlAsync(
                $"UPDATE `supplier_invoice` SET `paymentstat` = 'Paid' WHERE `id` = {supplierInvoiceId}",
                cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // the ledger entry + audit; assigns id

        // The general-ledger entry for the payment: Dr Accounts Payable, Cr Cash/Bank — settling what we
        // owe. Keyed on the payables entry id (this path writes no payment header), so it posts exactly once.
        var companyId = await _db.SupplierInvoices
            .Where(s => s.Id == supplierInvoiceId)
            .Select(s => s.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (companyId is { } company)
        {
            await _gl.PostAsync(new GlPosting(
                company, payment.Date, GlSources.PayablesPayment, payableEntry.Id, payment.Reference,
                [
                    GlChart.Payable(payment.Amount, 0m),
                    GlChart.CashOrBank(payment.Method, 0m, payment.Amount),
                ]), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SupplierPaymentRecorded(supplierInvoiceId, payment.Amount, newOutstanding);
    }

    public async Task DeleteAsync(long supplierInvoiceId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.SupplierInvoices
            .FirstOrDefaultAsync(s => s.Id == supplierInvoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Supplier invoice {supplierInvoiceId} does not exist.");

        if (invoice.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This supplier invoice was changed by someone else while you were viewing it.");
        }

        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Reverse whatever is still outstanding on it to zero — through a new, compensating entry, never
        // by erasing the purchase or its payments (the legacy delete hard-deleted and orphaned payments).
        var outstanding = await _ledger.OutstandingForInvoiceAsync(supplierInvoiceId, cancellationToken).ConfigureAwait(false);
        if (outstanding != 0m)
        {
            _db.PayablesLedger.Add(new PayablesLedgerEntry
            {
                SupplierId = invoice.SupplierId,
                Type = PayablesLedgerEntryType.Payment,
                Amount = -outstanding,
                SupplierInvoiceId = supplierInvoiceId,
                OccurredAt = _time.GetUtcNow().UtcDateTime,
                Note = "Supplier invoice voided",
            });

            // Reverse the purchase in the GL for the unpaid portion only — Dr Accounts Payable, Cr Purchases +
            // Input VAT (split pro-rata to what is still outstanding). Any amount already paid stays: real
            // money changed hands and its payment entries remain. Nothing outstanding → nothing to reverse.
            if (invoice.CompanyId is { } companyId)
            {
                var proportion = invoice.Amount == 0m ? 0m : outstanding / invoice.Amount;
                var vatPortion = Math.Round(invoice.TaxAmount * proportion, 4, MidpointRounding.AwayFromZero);
                await _gl.PostAsync(new GlPosting(
                    companyId, DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime), GlSources.SupplierInvoiceVoid, invoice.Id,
                    $"Supplier invoice {invoice.Id} voided",
                    [
                        GlChart.Payable(outstanding, 0m),
                        GlChart.Purchases(0m, outstanding - vatPortion),
                        GlChart.InputVat(0m, vatPortion),
                    ]), cancellationToken).ConfigureAwait(false);
            }
        }

        // Soft delete — set deleted_at directly (not Remove(), whose FK-nulling cascade would fight the
        // ledger's Restrict FK). The interceptor stamps it as an audited update.
        invoice.DeletedAt = _time.GetUtcNow().UtcDateTime;
        invoice.DeletedBy = _change.UserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the legacy varchar columns beside the typed ones, so the surviving legacy supplier reports
    /// read a whole row. <paramref name="paymentStat"/> is <c>Pending</c> at creation; the payment path
    /// flips it to <c>Paid</c> when the derived outstanding reaches zero.
    /// </summary>
    private void SetLegacyShadow(SupplierInvoice invoice, Supplier supplier, Company company, string paymentStat)
    {
        var entry = _db.Entry(invoice);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        Set(SupplierInvoiceLegacyShadow.Amount, Money(invoice.Amount));
        Set(SupplierInvoiceLegacyShadow.PaymentStat, paymentStat);
        Set(SupplierInvoiceLegacyShadow.InvDate, invoice.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(SupplierInvoiceLegacyShadow.NoVatTotal, Money(invoice.NetTotal));
        Set(SupplierInvoiceLegacyShadow.VType, company.VatCode);
        Set(SupplierInvoiceLegacyShadow.VPer, Money(invoice.TaxRatePercentage));
        Set(SupplierInvoiceLegacyShadow.SupCode, supplier.Code);
        Set(SupplierInvoiceLegacyShadow.Company, invoice.CompanyId?.ToString(CultureInfo.InvariantCulture));
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static object Snapshot(SupplierInvoice invoice, Supplier supplier, Company company) => new
    {
        supplierInvoice = new
        {
            invoice.SupplierReference,
            invoice.Date,
            invoice.NetTotal,
            invoice.TaxRatePercentage,
            invoice.TaxAmount,
            invoice.Amount,
        },
        supplier = new { supplier.Id, supplier.Code, supplier.Name, supplier.VatNumber },
        company = new { company.Id, company.Name, company.VatNumber },
    };
}

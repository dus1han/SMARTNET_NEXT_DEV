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

/// <inheritdoc cref="IInvoiceEditor"/>
public sealed class InvoiceEditor : IInvoiceEditor
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly ITaxEngine _tax;
    private readonly IDocumentVersionWriter _versions;
    private readonly ILegacyInvoiceAdopter _adopter;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public InvoiceEditor(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ITaxEngine tax,
        IDocumentVersionWriter versions,
        ILegacyInvoiceAdopter adopter,
        IBusinessRuleReader rules,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _tax = tax;
        _versions = versions;
        _adopter = adopter;
        _rules = rules;
        _change = change;
        _time = time;
    }

    public async Task<InvoiceEdited> EditAsync(long invoiceId, EditInvoice request, CancellationToken cancellationToken = default)
    {
        // One transaction: the re-valued document, the reconciled lines, any ledger adjustment and the new
        // version commit together or not at all.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // IgnoreQueryFilters so a *legacy* invoice loads too — the new app edits documents it did not raise
        // by adopting them first (below). A legacy row's lines are not linked yet, so Include is deferred.
        var invoice = await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} does not exist.");

        // A paid invoice is not editable — the figures the money was taken against must not change under a
        // payment already made. A cash invoice's settlement is a payment too, so a cash invoice is never
        // edited; it is voided and re-issued. The payment must be deleted first (a Phase 7 action). A new
        // invoice's payments are in the ledger; a legacy invoice's are in the old `payments` table.
        var hasPayment = await _db.ReceivablesLedger
            .AnyAsync(e => e.InvoiceId == invoice.Id && e.Type == LedgerEntryType.Payment, cancellationToken)
            .ConfigureAwait(false)
            || await _legacy.Payments
            .AnyAsync(p => p.Invoiceno == invoice.Number, cancellationToken)
            .ConfigureAwait(false);
        if (hasPayment)
        {
            throw new InvoiceHasPaymentsException(invoice.Number);
        }

        // The load's row_version is replaced by the one the caller edited against, so the UPDATE's WHERE
        // clause checks *their* expectation: if the row moved under them, no row matches and EF throws.
        _db.Entry(invoice).Property(i => i.RowVersion).OriginalValue = request.ExpectedRowVersion;

        // A legacy invoice is adopted into the new model before it can be edited — its typed columns and
        // lines are materialised from the legacy data and a version-1 "as imported" snapshot is written, all
        // inside this transaction (the concurrency check above fires on that first save). After this it is a
        // normal new-side invoice and the edit below proceeds unchanged.
        await _adopter.MaterialiseInCurrentTransactionAsync(invoice, cancellationToken).ConfigureAwait(false);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == invoice.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {invoice.CompanyId} does not exist.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {invoice.CustomerId} does not exist.");

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(company.Id, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Re-run the engine at the invoice's *snapshotted* rate, not whatever is in force today — an edit
        // corrects figures, it does not silently re-rate a document to a rate it was never issued under.
        var rateName = invoice.TaxRatePercentage == 0m
            ? "No VAT"
            : $"VAT {invoice.TaxRatePercentage.ToString("0.##", CultureInfo.InvariantCulture)}%";

        var calc = _tax.Calculate(new TaxCalculationRequest(
            invoice.Date,
            company.IsVatRegistered,
            rounding,
            [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            AvailableRates: [],
            request.DocumentDiscountPercent,
            RateOverride: new TaxRateOverride(invoice.TaxRateId, rateName, invoice.TaxRatePercentage)));

        var oldTotal = invoice.Total;

        ReconcileLines(invoice, request, calc);

        // Update the header figures. The rate, company, customer, type and date are unchanged.
        invoice.PurchaseOrderNo = request.PurchaseOrderNo;
        invoice.ContactPerson = request.ContactPerson ?? string.Empty;
        invoice.DiscountPercent = request.DocumentDiscountPercent;
        invoice.DiscountAmount = calc.Totals.Discount;
        invoice.Subtotal = calc.Totals.Subtotal;
        invoice.NetTotal = calc.Totals.Net;
        invoice.TaxAmount = calc.Totals.Tax;
        invoice.Total = calc.Totals.Total;
        invoice.Cost = request.Lines.Sum(l => l.Cost ?? 0m);

        UpdateLegacyShadow(invoice);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + lines + audit; concurrency check

        AdjustLedger(invoice, oldTotal, calc.Totals.Total);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var versionNo = await _versions
            .WriteAsync(DocumentTypes.Invoice, invoice.Id, invoice.CompanyId, Snapshot(invoice, calc, customer, company), _change.Reason, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var outstanding = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId == invoice.Id)
            .SumAsync(e => e.Amount, cancellationToken)
            .ConfigureAwait(false);

        return new InvoiceEdited(invoice.Id, invoice.Number, invoice.Total, outstanding, versionNo);
    }

    /// <summary>
    /// Brings the invoice's lines in line with the edit <b>in place</b> (the legacy delete-and-reinsert is
    /// gone): a line the edit still carries is updated, a line with no id is added, and a line the edit no
    /// longer carries is soft-deleted — so its history survives. The engine's results align with the request
    /// lines by position. As it goes it nets the stock each item line issues, so an item invoice's stock is
    /// adjusted automatically — the extra units of an increased quantity are issued, the units of a reduced
    /// or removed line come back.
    /// </summary>
    private void ReconcileLines(Invoice invoice, EditInvoice request, TaxCalculationResult calc)
    {
        var existing = invoice.Lines.Where(l => l.DeletedAt is null).ToDictionary(l => l.Id);
        var kept = new HashSet<long>();

        // itemId → net *additional* quantity issued: positive means more left stock, negative means it came
        // back. Netted per item so a line whose quantity did not change posts no movement.
        var stock = new Dictionary<long, decimal>();
        void Issue(long? itemId, decimal qty) { if (itemId is { } id && qty != 0m) stock[id] = stock.GetValueOrDefault(id) + qty; }

        foreach (var (input, line) in request.Lines.Zip(calc.Lines))
        {
            if (input.Id is { } id && existing.TryGetValue(id, out var current))
            {
                // Reverse this line's old stock effect and apply its new one; same item nets to the delta.
                Issue(current.ItemId, -current.Quantity);
                Issue(input.ItemId, line.Quantity);

                current.ItemId = input.ItemId;
                current.ItemCode = input.ItemCode;
                current.Description = input.Description;
                current.Quantity = line.Quantity;
                current.UnitPrice = line.UnitPrice;
                current.DiscountPercent = line.DiscountPercent;
                current.Gross = line.Gross;
                current.Net = line.Net;
                current.Cost = input.Cost;
                SetLineShadow(current, invoice.Number);
                kept.Add(id);
            }
            else
            {
                var added = new InvoiceLine
                {
                    InvoiceId = invoice.Id,
                    ItemId = input.ItemId,
                    ItemCode = input.ItemCode,
                    Description = input.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    Gross = line.Gross,
                    Net = line.Net,
                    Cost = input.Cost,
                };
                invoice.Lines.Add(added); // via the tracked navigation, so the version snapshot sees it
                SetLineShadow(added, invoice.Number);
                Issue(input.ItemId, line.Quantity); // a new item line issues its full quantity
            }
        }

        // A line the edit dropped is soft-deleted (the interceptor rewrites the Remove as a soft delete), so
        // it is attributable and recoverable — never erased. Its issued stock comes back.
        foreach (var line in existing.Values.Where(l => !kept.Contains(l.Id)))
        {
            _db.InvoiceLines.Remove(line);
            Issue(line.ItemId, -line.Quantity);
        }

        PostStockAdjustments(invoice, stock);
    }

    /// <summary>Posts one stock movement per item whose issued quantity changed — more out, or some back.</summary>
    private void PostStockAdjustments(Invoice invoice, Dictionary<long, decimal> stock)
    {
        var occurredAt = invoice.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (var (itemId, additionalIssued) in stock.Where(kv => kv.Value != 0m))
        {
            _db.StockMovements.Add(new StockMovement
            {
                ItemId = itemId,
                // More issued (positive) decreases stock — an Issue with a negative signed quantity; less
                // issued (negative) returns stock — a Receipt with a positive signed quantity.
                Type = additionalIssued > 0m ? StockMovementType.Issue : StockMovementType.Receipt,
                Quantity = -additionalIssued,
                OccurredAt = occurredAt,
                Reason = $"Invoice {invoice.Number} edited — stock adjusted",
            });
        }
    }

    /// <summary>
    /// Adjusts the receivables ledger for a changed total with a single compensating <c>CHARGE</c> — the
    /// balance is never reset (B3). An editable invoice has no payment against it (a paid one is refused
    /// above), so the delta lands cleanly on what the customer owes.
    /// </summary>
    private void AdjustLedger(Invoice invoice, decimal oldTotal, decimal newTotal)
    {
        var delta = newTotal - oldTotal;
        if (delta == 0m)
        {
            return;
        }

        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = invoice.CustomerId,
            Type = LedgerEntryType.Charge,
            Amount = delta,
            InvoiceId = invoice.Id,
            OccurredAt = invoice.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Note = $"Adjustment on edit ({(delta > 0 ? "+" : string.Empty)}{delta.ToString(CultureInfo.InvariantCulture)})",
        });
    }

    /// <summary>Updates the legacy shadow columns an edit affects; identity columns (date, customer, …) stay.</summary>
    private void UpdateLegacyShadow(Invoice invoice)
    {
        var entry = _db.Entry(invoice);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        var hasItem = invoice.Lines.Any(l => l.DeletedAt is null && l.ItemId is not null);

        Set(InvoiceLegacyShadow.It, hasItem ? "ITEM" : "SERVICE");
        Set(InvoiceLegacyShadow.TotAmount, Money(invoice.Total));
        Set(InvoiceLegacyShadow.Balance, Money(invoice.Type == InvoiceType.Cash ? 0m : invoice.Total));
        Set(InvoiceLegacyShadow.Cost, Money(invoice.Cost));
        Set(InvoiceLegacyShadow.NoVatTotal, Money(invoice.NetTotal));
        Set(InvoiceLegacyShadow.DiscountPer, Money(invoice.DiscountPercent));
        Set(InvoiceLegacyShadow.BeforeDiscTot, Money(invoice.Subtotal));
    }

    private void SetLineShadow(InvoiceLine line, string number)
    {
        var entry = _db.Entry(line);
        entry.Property(InvoiceLineLegacyShadow.Inno).CurrentValue = number;
        entry.Property(InvoiceLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
        entry.Property(InvoiceLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
        entry.Property(InvoiceLineLegacyShadow.Tot).CurrentValue = Money(line.Gross);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The new version's snapshot — the document as it now stands, resolved not referenced.</summary>
    private static object Snapshot(Invoice invoice, TaxCalculationResult calc, Customer customer, Company company) => new
    {
        invoice = new
        {
            invoice.Number,
            invoice.Date,
            Type = invoice.Type.ToString(),
            invoice.PurchaseOrderNo,
            invoice.ContactPerson,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.NetTotal,
            invoice.TaxAmount,
            invoice.Total,
            invoice.Cost,
            tax = new { calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage },
        },
        customer = new { customer.Id, customer.Code, customer.Name, customer.VatNumber },
        company = new
        {
            company.Id,
            company.Name,
            company.VatNumber,
            company.AddressLine1,
            company.AddressLine2,
            company.City,
            company.Country,
        },
        lines = invoice.Lines.Where(l => l.DeletedAt is null).Select(l => new
        {
            l.ItemId,
            l.ItemCode,
            l.Description,
            l.Quantity,
            l.UnitPrice,
            l.DiscountPercent,
            l.Gross,
            l.Net,
            l.Cost,
        }),
    };
}

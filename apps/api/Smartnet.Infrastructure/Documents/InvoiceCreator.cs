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

/// <inheritdoc cref="IInvoiceCreator"/>
public sealed class InvoiceCreator : IInvoiceCreator
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IDocumentVersionWriter _versions;
    private readonly IReceivablesLedger _ledger;
    private readonly IGeneralLedger _gl;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public InvoiceCreator(
        SmartnetDbContext db,
        ITaxEngine tax,
        IDocumentNumberAllocator numbers,
        IDocumentVersionWriter versions,
        IReceivablesLedger ledger,
        IGeneralLedger gl,
        IBusinessRuleReader rules,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _tax = tax;
        _numbers = numbers;
        _versions = versions;
        _ledger = ledger;
        _gl = gl;
        _rules = rules;
        _change = change;
        _time = time;
    }

    public async Task<InvoiceCreated> CreateAsync(NewInvoice request, CancellationToken cancellationToken = default)
    {
        // One transaction: the number, the document, the ledger, the stock and the snapshot commit
        // together or not at all (B2). Conversion reuses the core below inside its own transaction.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var created = await CreateInCurrentTransactionAsync(request, sourceQuotationId: null, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task<InvoiceCreated> CreateInCurrentTransactionAsync(
        NewInvoice request,
        long? sourceQuotationId,
        CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "CreateInCurrentTransactionAsync must be called inside an open transaction — it does not "
                + "begin or commit one. Use CreateAsync for a stand-alone invoice.");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {request.CustomerId} does not exist.");

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(request.CompanyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Value the lines: the server is the authority, and this is the one place tax is computed.
        var calc = _tax.Calculate(new TaxCalculationRequest(
            request.Date,
            company.IsVatRegistered,
            rounding,
            [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            rates,
            request.DocumentDiscountPercent));

        await EnforceCreditLimit(request, customer, calc.Totals.Total, cancellationToken).ConfigureAwait(false);

        // The number is reserved under a row lock inside the caller's transaction (B4).
        var number = await _numbers
            .AllocateAsync(request.CompanyId, DocumentTypes.Invoice, request.Date, cancellationToken)
            .ConfigureAwait(false);

        // Cost basis: for a service invoice the user enters one document-level figure (item lines carry no
        // cost); for an item invoice it is the sum of the per-line costs carried from the item master.
        var cost = request.DocumentCost ?? request.Lines.Sum(l => l.Cost ?? 0m);
        var preparedByName = await PreparedByNameAsync(cancellationToken).ConfigureAwait(false);

        var invoice = new Invoice
        {
            Number = number,
            CompanyId = request.CompanyId,
            CustomerId = request.CustomerId,
            Date = request.Date,
            Type = request.Type,
            PurchaseOrderNo = request.PurchaseOrderNo,
            // contactperson is NOT NULL in the legacy table; an absent contact is an empty string, not null.
            ContactPerson = request.ContactPerson ?? string.Empty,
            PreparedBy = _change.UserId,

            Subtotal = calc.Totals.Subtotal,
            // The whole-document discount rate; per-line discounts are on the lines. DiscountAmount is the
            // total of both, as the engine computed it.
            DiscountPercent = request.DocumentDiscountPercent,
            DiscountAmount = calc.Totals.Discount,
            NetTotal = calc.Totals.Net,
            TaxRateId = calc.TaxRateId,
            TaxRatePercentage = calc.TaxRatePercentage,
            TaxAmount = calc.Totals.Tax,
            Total = calc.Totals.Total,
            Cost = cost,
            SourceQuotationId = sourceQuotationId,
            DataOrigin = "new",

            Lines = [.. request.Lines.Zip(calc.Lines, (input, line) => new InvoiceLine
            {
                ItemId = input.ItemId,
                ItemCode = input.ItemCode,
                Description = input.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                Gross = line.Gross,
                Net = line.Net,
                Cost = input.Cost,
            })],
        };

        _db.Invoices.Add(invoice);
        SetLegacyShadow(invoice, customer, company, preparedByName);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + lines + audit; assigns ids

        PostLedger(invoice, request);
        PostStockIssues(invoice, request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _versions
            .WriteAsync(DocumentTypes.Invoice, invoice.Id, request.CompanyId, Snapshot(invoice, calc, customer, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        // The general-ledger entry for the sale: Dr Cash/Receivable, Cr Sales + Output VAT. A cash invoice
        // is settled at issue, so its debit lands on Cash; a credit invoice's on Accounts Receivable (a later
        // receipt then moves it from AR to Cash/Bank). Zero VAT lines are dropped by the posting engine.
        await _gl.PostAsync(new GlPosting(
            request.CompanyId, invoice.Date, GlSources.Invoice, invoice.Id, $"Invoice {number}",
            [
                invoice.Type == InvoiceType.Cash ? GlChart.Cash(invoice.Total, 0m) : GlChart.Receivable(invoice.Total, 0m),
                GlChart.Sales(0m, invoice.NetTotal),
                GlChart.OutputVat(0m, invoice.TaxAmount),
            ]), cancellationToken).ConfigureAwait(false);

        // Outstanding = what the ledger now holds for this invoice: the full amount on credit, zero on
        // cash (the cash-at-issue payment settles it).
        var outstanding = request.Type == InvoiceType.Cash ? 0m : calc.Totals.Total;
        return new InvoiceCreated(invoice.Id, number, calc.Totals.Total, outstanding);
    }

    /// <summary>
    /// Guards the customer's credit limit — a <b>soft</b> gate. It flags an invoice, cash or credit, that
    /// would take the customer past their limit, but only when enforcement is on (off by default) and the
    /// caller has <i>not</i> acknowledged the breach; an acknowledged breach proceeds (the confirmation is
    /// the override — see <see cref="NewInvoice.AcknowledgeCreditLimit"/>). Measured against the derived
    /// ledger balance, never a stored one.
    /// </summary>
    /// <remarks>
    /// The limit gates the <b>sale</b>, not just credit terms: it applies to cash and credit invoices
    /// alike (confirmed 2026-07-15), unlike the legacy check, which ran on service invoices only.
    /// </remarks>
    private async Task EnforceCreditLimit(NewInvoice request, Customer customer, decimal total, CancellationToken cancellationToken)
    {
        // A zero limit means "no limit".
        if (customer.CreditLimit <= 0m)
        {
            return;
        }

        // The caller has seen the breach and confirmed it — the save proceeds. This is what makes the
        // gate soft: a breach is never a dead-end, it is a confirmation.
        if (request.AcknowledgeCreditLimit)
        {
            return;
        }

        var enforced = BusinessRules.AsBool(
            await _rules.ResolveAsync(request.CompanyId, BusinessRules.CreditLimitEnforced, cancellationToken).ConfigureAwait(false));
        if (!enforced)
        {
            return;
        }

        var balance = await _ledger.BalanceForCustomerAsync(customer.Id, cancellationToken).ConfigureAwait(false);
        if (balance + total > customer.CreditLimit)
        {
            throw new CreditLimitExceededException(customer.CreditLimit, balance, total);
        }
    }

    /// <summary>
    /// Writes the legacy varchar columns beside the typed ones, so a legacy reader and the Phase 4
    /// reports see a complete row (slice 0-A). The three NOT NULL columns are among them, so this is not
    /// optional decoration — the insert fails without it.
    /// </summary>
    private void SetLegacyShadow(Invoice invoice, Customer customer, Company company, string? preparedByName)
    {
        var entry = _db.Entry(invoice);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        var hasItem = invoice.Lines.Any(l => l.ItemId is not null);

        Set(InvoiceLegacyShadow.It, hasItem ? "ITEM" : "SERVICE");
        Set(InvoiceLegacyShadow.InvType, invoice.Type.ToString());
        Set(InvoiceLegacyShadow.InDate, invoice.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(InvoiceLegacyShadow.Customer, customer.Code);
        Set(InvoiceLegacyShadow.TotAmount, Money(invoice.Total));
        Set(InvoiceLegacyShadow.Balance, Money(invoice.Type == InvoiceType.Cash ? 0m : invoice.Total));
        Set(InvoiceLegacyShadow.PreparedBy, preparedByName);
        Set(InvoiceLegacyShadow.CDateTime, _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(InvoiceLegacyShadow.Cost, Money(invoice.Cost));
        Set(InvoiceLegacyShadow.NoVatTotal, Money(invoice.NetTotal));
        Set(InvoiceLegacyShadow.VType, company.VatCode);
        Set(InvoiceLegacyShadow.VPer, Money(invoice.TaxRatePercentage));
        Set(InvoiceLegacyShadow.DiscountPer, Money(invoice.DiscountPercent)); // the whole-document discount rate
        Set(InvoiceLegacyShadow.BeforeDiscTot, Money(invoice.Subtotal));
        Set(InvoiceLegacyShadow.Company, invoice.CompanyId?.ToString(CultureInfo.InvariantCulture));

        foreach (var line in invoice.Lines)
        {
            var lineEntry = _db.Entry(line);
            lineEntry.Property(InvoiceLineLegacyShadow.Inno).CurrentValue = invoice.Number;
            lineEntry.Property(InvoiceLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
            lineEntry.Property(InvoiceLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
            lineEntry.Property(InvoiceLineLegacyShadow.Tot).CurrentValue = Money(line.Gross);
        }
    }

    /// <summary>The signed-in user's name, for the legacy <c>preparedby</c> — null if none is set.</summary>
    private async Task<string?> PreparedByNameAsync(CancellationToken cancellationToken)
    {
        if (_change.UserId is not { } userId)
        {
            return null;
        }

        return await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name ?? u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Charges the invoice, and on a cash invoice settles it — with real entries, not a flag.</summary>
    private void PostLedger(Invoice invoice, NewInvoice request)
    {
        var occurredAt = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = invoice.CustomerId,
            Type = LedgerEntryType.Charge,
            Amount = invoice.Total,
            InvoiceId = invoice.Id,
            OccurredAt = occurredAt,
        });

        if (request.Type == InvoiceType.Cash)
        {
            _db.ReceivablesLedger.Add(new LedgerEntry
            {
                CustomerId = invoice.CustomerId,
                Type = LedgerEntryType.Payment,
                Amount = -invoice.Total,
                InvoiceId = invoice.Id,
                OccurredAt = occurredAt,
                Note = "Cash — settled at issue",
            });
        }
    }

    /// <summary>Issues stock for each item line — one append to the ledger the app already owns (B3).</summary>
    private void PostStockIssues(Invoice invoice, NewInvoice request)
    {
        var occurredAt = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (var line in request.Lines.Where(l => l.ItemId is not null))
        {
            _db.StockMovements.Add(new StockMovement
            {
                ItemId = line.ItemId!.Value,
                Type = StockMovementType.Issue,
                Quantity = -line.Quantity, // signed: an issue decreases stock
                OccurredAt = occurredAt,
                Reason = $"Invoice {invoice.Number}",
            });
        }
    }

    /// <summary>
    /// A self-contained snapshot — the document as issued, resolved not referenced, so a reprint
    /// reproduces it rather than re-resolving today's rate or today's company header.
    /// </summary>
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
        lines = invoice.Lines.Select(l => new
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

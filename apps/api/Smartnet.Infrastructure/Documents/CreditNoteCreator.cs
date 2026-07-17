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

/// <inheritdoc cref="ICreditNoteCreator"/>
public sealed class CreditNoteCreator : ICreditNoteCreator
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IDocumentVersionWriter _versions;
    private readonly IGeneralLedger _gl;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public CreditNoteCreator(
        SmartnetDbContext db,
        ITaxEngine tax,
        IDocumentNumberAllocator numbers,
        IDocumentVersionWriter versions,
        IGeneralLedger gl,
        IBusinessRuleReader rules,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _tax = tax;
        _numbers = numbers;
        _versions = versions;
        _gl = gl;
        _rules = rules;
        _change = change;
        _time = time;
    }

    public async Task<CreditNoteCreated> CreateAsync(NewCreditNote request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {request.CustomerId} does not exist.");

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(request.CompanyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // The rate is the parent invoice's, inherited verbatim — not resolved from the rate table at the
        // note's date. So a full credit nets exactly against the invoice it reverses, and crediting an old
        // invoice never depends on the rate table still covering that invoice's date. A note carries no
        // whole-document discount (the legacy credit note has none).
        var rateName = request.TaxRatePercentage == 0m
            ? "No VAT"
            : $"VAT {request.TaxRatePercentage.ToString("0.##", CultureInfo.InvariantCulture)}%";

        var calc = _tax.Calculate(new TaxCalculationRequest(
            request.Date,
            company.IsVatRegistered,
            rounding,
            [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            AvailableRates: [],
            DocumentDiscountPercent: 0m,
            RateOverride: new TaxRateOverride(request.TaxRateId, rateName, request.TaxRatePercentage)));

        // One transaction: the number, the header, the lines, the ledger credit, any stock receipt and the
        // snapshot commit together or not at all (B2). The number is reserved under a row lock inside the
        // transaction, so a failed save rolls it back (B4).
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var number = await _numbers
            .AllocateAsync(request.CompanyId, DocumentTypes.CreditNote, request.Date, cancellationToken)
            .ConfigureAwait(false);

        var lineCost = request.Lines.Sum(l => l.Cost ?? 0m);
        var preparedByName = await PreparedByNameAsync(cancellationToken).ConfigureAwait(false);

        var creditNote = new CreditNote
        {
            Number = number,
            CompanyId = request.CompanyId,
            CustomerId = request.CustomerId,
            InvoiceId = request.InvoiceId,
            Date = request.Date,
            ReturnsStock = request.ReturnsStock,
            PreparedBy = _change.UserId,

            Subtotal = calc.Totals.Subtotal,
            DiscountPercent = 0m,
            DiscountAmount = calc.Totals.Discount,
            NetTotal = calc.Totals.Net,
            TaxRateId = calc.TaxRateId,
            TaxRatePercentage = calc.TaxRatePercentage,
            TaxAmount = calc.Totals.Tax,
            Total = calc.Totals.Total,
            Cost = lineCost,
            DataOrigin = "new",

            Lines = [.. request.Lines.Zip(calc.Lines, (input, line) => new CreditNoteLine
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

        _db.CreditNotes.Add(creditNote);
        SetLegacyShadow(creditNote, request, preparedByName, company);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + lines + audit; assigns ids

        PostLedgerCredit(creditNote);
        PostStockReceipts(creditNote);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _versions
            .WriteAsync(DocumentTypes.CreditNote, creditNote.Id, request.CompanyId, Snapshot(creditNote, calc, customer, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        // The general-ledger entry: a credit note reverses a sale — Dr Sales + Output VAT, Cr Receivable
        // (the exact opposite of the invoice's posting), so the receivable falls by the note's amount.
        await _gl.PostAsync(new GlPosting(
            request.CompanyId, creditNote.Date, GlSources.CreditNote, creditNote.Id, $"Credit note {number}",
            [
                GlChart.Sales(creditNote.NetTotal, 0m),
                GlChart.OutputVat(creditNote.TaxAmount, 0m),
                GlChart.Receivable(0m, creditNote.Total),
            ]), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CreditNoteCreated(creditNote.Id, number, calc.Totals.Total);
    }

    /// <summary>
    /// Writes the legacy varchar columns beside the typed ones. Two are NOT NULL — <c>invoiceno</c> (the
    /// parent invoice's number) and <c>stockposting</c> — so this is not optional: the insert fails without
    /// it. The rest keep any legacy reader whole.
    /// </summary>
    private void SetLegacyShadow(CreditNote creditNote, NewCreditNote request, string? preparedByName, Company company)
    {
        var entry = _db.Entry(creditNote);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        Set(CreditNoteLegacyShadow.InvoiceNo, request.InvoiceNumber);
        Set(CreditNoteLegacyShadow.CnDate, creditNote.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(CreditNoteLegacyShadow.TotAmount, Money(creditNote.Total));
        Set(CreditNoteLegacyShadow.PreparedBy, preparedByName);
        Set(CreditNoteLegacyShadow.CDateTime, _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(CreditNoteLegacyShadow.NoVatTotal, Money(creditNote.NetTotal));
        Set(CreditNoteLegacyShadow.VType, company.VatCode);
        Set(CreditNoteLegacyShadow.VPer, Money(creditNote.TaxRatePercentage));
        // The legacy stockposting flag is "1"/"0", as the old app wrote it.
        Set(CreditNoteLegacyShadow.StockPosting, creditNote.ReturnsStock ? "1" : "0");

        foreach (var line in creditNote.Lines)
        {
            var lineEntry = _db.Entry(line);
            lineEntry.Property(CreditNoteLineLegacyShadow.Cnno).CurrentValue = creditNote.Number;
            lineEntry.Property(CreditNoteLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
            lineEntry.Property(CreditNoteLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
            lineEntry.Property(CreditNoteLineLegacyShadow.Tot).CurrentValue = Money(line.Gross);
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

    /// <summary>
    /// Credits the receivable — the opposite sign to an invoice's charge (B3). The entry names the parent
    /// invoice, so that invoice's outstanding falls by the note's amount, and the customer's derived balance
    /// with it. Never <c>UPDATE invoice_h SET balance = balance - x</c>.
    /// </summary>
    private void PostLedgerCredit(CreditNote creditNote)
    {
        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = creditNote.CustomerId,
            Type = LedgerEntryType.Credit,
            Amount = -creditNote.Total, // negative: a credit reduces what the customer owes
            InvoiceId = creditNote.InvoiceId,
            OccurredAt = creditNote.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Note = $"Credit note {creditNote.Number}",
        });
    }

    /// <summary>
    /// Returns goods to stock for each item line — one positive append to the stock ledger — but only when
    /// the note returns goods (a pure price adjustment leaves stock untouched). The mirror of the invoice's
    /// issue.
    /// </summary>
    private void PostStockReceipts(CreditNote creditNote)
    {
        if (!creditNote.ReturnsStock)
        {
            return;
        }

        var occurredAt = creditNote.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (var line in creditNote.Lines.Where(l => l.ItemId is not null))
        {
            _db.StockMovements.Add(new StockMovement
            {
                ItemId = line.ItemId!.Value,
                Type = StockMovementType.Receipt,
                Quantity = line.Quantity, // signed: a receipt increases stock
                OccurredAt = occurredAt,
                Reason = $"Credit note {creditNote.Number}",
            });
        }
    }

    /// <summary>
    /// A self-contained snapshot — the credit note as issued, resolved not referenced, so a reprint
    /// reproduces it.
    /// </summary>
    private static object Snapshot(CreditNote creditNote, TaxCalculationResult calc, Customer customer, Company company) => new
    {
        creditNote = new
        {
            creditNote.Number,
            creditNote.Date,
            creditNote.ReturnsStock,
            creditNote.Subtotal,
            creditNote.DiscountAmount,
            creditNote.NetTotal,
            creditNote.TaxAmount,
            creditNote.Total,
            creditNote.Cost,
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
        lines = creditNote.Lines.Select(l => new
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

using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IQuotationConverter"/>
public sealed class QuotationConverter : IQuotationConverter
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly IInvoiceCreator _invoices;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public QuotationConverter(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        IInvoiceCreator invoices,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _invoices = invoices;
        _change = change;
        _time = time;
    }

    public async Task<InvoiceCreated> ConvertAsync(
        long quotationId,
        ConvertQuotation request,
        CancellationToken cancellationToken = default)
    {
        // One transaction over the whole conversion: the invoice (number, ledger, stock, snapshot) and
        // the marking of the quote are one atomic act. If either fails, neither happened — so a quote is
        // never left "converted" without an invoice, nor an invoice raised without the quote being spent.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Bypass the data_origin filter so a LEGACY quotation loads too — its conversion columns and its
        // (backfilled) company_id are the new columns the adoption added, so the entity can carry them and
        // record the conversion on the legacy row itself.
        var quotation = await _db.Quotations
            .IgnoreQueryFilters()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Quotation {quotationId} does not exist.");

        // Refuse a second conversion. The sequential case is caught here; a concurrent one is caught by the
        // row_version optimistic-concurrency check when the two updates of this quote collide, which rolls
        // back the loser's invoice with it (same transaction). Holds for legacy quotes too, since the new
        // conversion writes this column onto the legacy row.
        if (quotation.ConvertedToInvoiceId is { } existing)
        {
            throw new QuotationAlreadyConvertedException(quotation.Number, existing);
        }

        var isLegacy = !string.Equals(quotation.DataOrigin, "new", StringComparison.Ordinal);

        // The lines, customer, company and document discount come from the quote (a new quote from its
        // typed columns; a legacy one from its varchar columns, parsed). The caller supplies only what
        // makes it a sale — cash/credit, the invoice date, an optional PO and contact. Either way the
        // invoice is taxed at its OWN date through the same pipeline, so it gets a real number, a ledger
        // charge, a stock issue and a snapshot — none of which the legacy copy-paste conversion produced.
        var newInvoice = isLegacy
            ? await BuildFromLegacyAsync(quotation, request, cancellationToken).ConfigureAwait(false)
            : BuildFromNew(quotation, request);

        var created = await _invoices
            .CreateInCurrentTransactionAsync(newInvoice, sourceQuotationId: quotation.Id, cancellationToken)
            .ConfigureAwait(false);

        // Mark the quote spent, pointing at the invoice it became. The invoice already points back at the
        // quote (its SourceQuotationId), so the two documents reference each other — new or legacy.
        quotation.ConvertedToInvoiceId = created.Id;
        quotation.ConvertedAt = _time.GetUtcNow().UtcDateTime;
        quotation.ConvertedBy = _change.UserId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }

    private static NewInvoice BuildFromNew(Quotation quotation, ConvertQuotation request)
    {
        if (quotation.CompanyId is not { } companyId)
        {
            throw new InvalidOperationException($"Quotation {quotation.Id} has no company and cannot be converted.");
        }

        return new NewInvoice(
            companyId,
            quotation.CustomerId,
            request.Type,
            request.Date,
            request.PurchaseOrderNo,
            request.ContactPerson ?? quotation.ContactPerson,
            [.. quotation.Lines.Select(l => new NewInvoiceLine(
                l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
            quotation.DiscountPercent);
    }

    /// <summary>
    /// Builds the invoice draft from a legacy quotation's stored <c>varchar</c> data, parsed defensively.
    /// The customer is resolved from its code to the surrogate id the invoice needs; each line resolves
    /// its item by code where the code still exists in the item master (so it issues stock and carries a
    /// cost), otherwise it becomes a free-typed service line.
    /// </summary>
    private async Task<NewInvoice> BuildFromLegacyAsync(
        Quotation quotation,
        ConvertQuotation request,
        CancellationToken cancellationToken)
    {
        if (quotation.CompanyId is not { } companyId)
        {
            throw new InvalidOperationException(
                $"Quotation {quotation.Number} has no company and cannot be converted.");
        }

        var h = await _legacy.QuotationHs
            .FirstOrDefaultAsync(x => x.Id == quotation.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Legacy quotation {quotation.Id} could not be read.");

        var customer = h.Customer is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            throw new InvalidOperationException(
                $"The customer '{h.Customer}' on quotation {h.QNo} is not in the customer master, so it cannot be converted.");
        }

        var legacyLines = await _legacy.QuotationLs
            .Where(l => l.Qno == h.QNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (legacyLines.Count == 0)
        {
            throw new InvalidOperationException($"Quotation {h.QNo} has no lines to convert.");
        }

        // Resolve items by code once, so a line whose item still exists issues stock and carries its cost.
        var codes = legacyLines
            .Select(l => l.Itemcode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();
        var items = (await _db.Items
            .Where(i => i.Code != null && codes.Contains(i.Code))
            .Select(i => new { i.Id, i.Code, i.Cost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(i => i.Code!, i => (i.Id, i.Cost));

        var lines = legacyLines.Select(l =>
        {
            var matched = l.Itemcode is not null && items.TryGetValue(l.Itemcode, out var item);
            return new NewInvoiceLine(
                matched ? items[l.Itemcode!].Id : null,
                l.Itemcode,
                l.Desc,
                LegacyValue.Money(l.Qty),
                LegacyValue.Money(l.Rate),
                0m, // legacy lines carry no per-line discount
                matched ? items[l.Itemcode!].Cost : null);
        }).ToList();

        return new NewInvoice(
            companyId,
            customer.Id,
            request.Type,
            request.Date,
            request.PurchaseOrderNo,
            request.ContactPerson ?? h.Contactperson,
            lines,
            LegacyValue.Money(h.Discountper));
    }
}

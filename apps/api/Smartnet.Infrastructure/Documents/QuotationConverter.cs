using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;

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

        // Lock the quotation row for the transaction, so this and a concurrent "convert to proforma" of the
        // same quote are taken one after the other — the second then sees what the first did.
        await QuotationLock.TakeAsync(_db, quotationId, cancellationToken).ConfigureAwait(false);

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

        // A quotation with a live proforma goes through the proforma — converting it here as well would
        // turn one quotation into two invoices.
        var proforma = await _db.ProformaInvoices
            .Where(p => p.QuotationId == quotation.Id)
            .Select(p => p.Number)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (proforma is not null)
        {
            throw new QuotationHasProformaException(quotation.Number, proforma);
        }

        // The lines, customer, company and document discount come from the quote (a new quote from its
        // typed columns; a legacy one from its varchar columns, parsed — see QuotationContentReader). The
        // caller supplies only what makes it a sale — cash/credit, the invoice date, an optional PO and
        // contact. Either way the invoice is taxed at its OWN date through the same pipeline, so it gets a
        // real number, a ledger charge, a stock issue and a snapshot — none of which the legacy copy-paste
        // conversion produced.
        var content = await QuotationContentReader.ReadAsync(quotation, _db, _legacy, cancellationToken).ConfigureAwait(false);

        var newInvoice = new NewInvoice(
            content.CompanyId,
            content.CustomerId,
            request.Type,
            request.Date,
            request.PurchaseOrderNo,
            request.ContactPerson ?? content.ContactPerson,
            content.Lines,
            content.DocumentDiscountPercent,
            // An item quote derives its cost from the line costs carried off the item master. A service quote
            // has no such source, so the cost is entered at conversion and required there. "Service" means NO
            // line resolves to an item: a quote holding both (parts plus labour) derives its cost from the item
            // lines, and on such a mixed quote the labour side of the cost is not captured — the pre-existing
            // behaviour, which understates cost rather than inventing one.
            DocumentCost: content.HasItem ? null : ServiceCostRequired(quotation, request));

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

    /// <summary>
    /// The cost for a service conversion: what the caller entered, and it must be something.
    /// </summary>
    /// <remarks>
    /// Falling back to the quotation's stored <c>Cost</c> would defeat the requirement, because that column
    /// is <c>NOT NULL DEFAULT 0</c> — every service quote has a cost of zero until told otherwise, so a
    /// fallback would silently produce the 100%-margin invoice this exists to prevent. Zero is accepted when
    /// it is <i>typed</i>: a service genuinely costing nothing is a claim someone can make, but it has to be
    /// made rather than defaulted into.
    /// </remarks>
    private static decimal ServiceCostRequired(Quotation quotation, ConvertQuotation request) =>
        request.DocumentCost ?? throw new ServiceCostRequiredException(quotation.Number);
}

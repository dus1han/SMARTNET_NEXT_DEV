using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Documents;

/// <summary>What a quotation offers — company, customer, contact, discount and lines — whichever system raised it.</summary>
/// <param name="HasItem">Whether any line still resolves to an item — what decides if a conversion must be given a cost.</param>
internal sealed record QuotationContent(
    long CompanyId,
    long CustomerId,
    string? ContactPerson,
    decimal DocumentDiscountPercent,
    IReadOnlyList<NewInvoiceLine> Lines,
    bool HasItem);

/// <summary>
/// Reads a quotation's content for the two things built from it: an invoice (<see cref="QuotationConverter"/>)
/// and a proforma (<see cref="ProformaInvoiceService"/>). One reader, so the two cannot read the same quotation
/// differently.
/// </summary>
/// <remarks>
/// <para><b>New</b> quotations are read from their typed columns, <b>legacy</b> ones from the old system's
/// varchar columns, parsed defensively. The legacy path is not the edge case it looks like: every quotation
/// on live was raised by the old app, so for the foreseeable future it is the path nearly every conversion
/// takes.</para>
///
/// <para>"Item" means a line still resolves to an item — not the legacy <c>it</c> column. An item line issues
/// stock and carries a cost; a quotation with none is service work, whose cost has to be entered on
/// conversion because there is nowhere to derive it from.</para>
/// </remarks>
internal static class QuotationContentReader
{
    public static async Task<QuotationContent> ReadAsync(
        Quotation quotation,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        CancellationToken cancellationToken)
    {
        if (quotation.CompanyId is not { } companyId)
        {
            throw new InvalidOperationException($"Quotation {quotation.Number} has no company and cannot be converted.");
        }

        return string.Equals(quotation.DataOrigin, "new", StringComparison.Ordinal)
            ? FromNew(quotation, companyId)
            : await FromLegacyAsync(quotation, companyId, db, legacy, cancellationToken).ConfigureAwait(false);
    }

    private static QuotationContent FromNew(Quotation quotation, long companyId)
    {
        // Live lines only. The quote is loaded with IgnoreQueryFilters (so a legacy row loads), which drops
        // the line soft-delete filter with it — so a quote edited before conversion would otherwise carry the
        // lines that edit removed.
        var lines = quotation.Lines.Where(l => l.DeletedAt is null).ToList();

        return new QuotationContent(
            companyId,
            quotation.CustomerId,
            quotation.ContactPerson,
            quotation.DiscountPercent,
            [.. lines.Select(l => new NewInvoiceLine(
                l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
            lines.Any(l => l.ItemId is not null));
    }

    /// <summary>
    /// A legacy quotation's stored varchar data, parsed defensively. The customer resolves from its code to
    /// the id the new documents need; each line resolves its item by code where the code still exists in the
    /// item master (so it issues stock and carries a cost), and otherwise becomes a typed service line.
    /// </summary>
    private static async Task<QuotationContent> FromLegacyAsync(
        Quotation quotation,
        long companyId,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        CancellationToken cancellationToken)
    {
        var h = await legacy.QuotationHs
            .FirstOrDefaultAsync(x => x.Id == quotation.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Legacy quotation {quotation.Id} could not be read.");

        var customer = h.Customer is null
            ? null
            : await db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            throw new InvalidOperationException(
                $"The customer '{h.Customer}' on quotation {h.QNo} is not in the customer master, so it cannot be converted.");
        }

        var legacyLines = await legacy.QuotationLs
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
        var items = (await db.Items
            .Where(i => i.Code != null && codes.Contains(i.Code))
            .Select(i => new { i.Id, i.Code, i.Cost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(i => i.Code!, i => (i.Id, i.Cost));

        var lines = legacyLines.Select(l =>
        {
            var matched = l.Itemcode is not null && items.ContainsKey(l.Itemcode);
            return new NewInvoiceLine(
                matched ? items[l.Itemcode!].Id : null,
                l.Itemcode,
                l.Desc,
                LegacyValue.Money(l.Qty),
                LegacyValue.Money(l.Rate),
                0m, // legacy lines carry no per-line discount
                matched ? items[l.Itemcode!].Cost : null);
        }).ToList();

        return new QuotationContent(
            companyId,
            customer.Id,
            h.Contactperson,
            LegacyValue.Money(h.Discountper),
            lines,
            lines.Any(l => l.ItemId is not null));
    }
}

/// <summary>
/// Serialises the operations that consume a quotation — converting it to an invoice and raising a proforma
/// from it — so two of them cannot both pass their "is it still free?" check at the same moment.
/// </summary>
internal static class QuotationLock
{
    /// <summary>
    /// <c>SELECT … FOR UPDATE</c> on the quotation's row. Held until the caller's transaction ends, so it must
    /// be called inside one; outside a transaction the lock would be released immediately.
    /// </summary>
    public static async Task TakeAsync(SmartnetDbContext db, long quotationId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The quotation lock must be taken inside a transaction.");
        }

        await db.Database
            .ExecuteSqlAsync($"SELECT id FROM quotation_h WHERE id = {quotationId} FOR UPDATE", cancellationToken)
            .ConfigureAwait(false);
    }
}

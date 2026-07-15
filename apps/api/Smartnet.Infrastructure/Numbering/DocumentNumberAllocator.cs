using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Numbering;

/// <inheritdoc cref="IDocumentNumberAllocator"/>
public sealed class DocumentNumberAllocator : IDocumentNumberAllocator
{
    private readonly SmartnetDbContext _db;

    public DocumentNumberAllocator(SmartnetDbContext db) => _db = db;

    public async Task<string> AllocateAsync(
        long companyId,
        string docType,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        if (!DocumentTypes.IsKnown(docType))
        {
            throw new ArgumentException($"'{docType}' is not a document type.", nameof(docType));
        }

        // The lock is only held for the life of a transaction. Without one, FOR UPDATE locks the
        // row and releases it immediately, and two concurrent saves both read the same number —
        // which is the bug this class exists to prevent, reintroduced by its own caller.
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "AllocateAsync must be called inside the transaction that saves the document. "
                + "A number allocated outside one is not reserved: the row lock is released before "
                + "the document is written, and the next caller is handed the same number.");
        }

        // SELECT … FOR UPDATE. The second concurrent caller blocks here until the first commits,
        // and then reads the number the first has already taken — rather than reading the same one
        // and issuing a duplicate. This is the whole fix for B4.
        var series = await _db.DocumentSeries
            .FromSql($"""
                SELECT * FROM document_series
                WHERE company_id = {companyId} AND doc_type = {docType}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            // Loudly. A series that silently starts at 1 would reissue numbers already printed on
            // 2,485 invoices — and the unique index would then reject the save, at the till, in
            // front of a customer. Better to fail in staging, on the first attempt, with a message
            // that says exactly what to do.
            throw new InvalidOperationException(
                $"No numbering series is configured for {docType} in company {companyId}. "
                + "Run the numbering initialiser (Settings → Numbering → Initialise from legacy) "
                + "before issuing documents, so numbering continues from the last number used "
                + "rather than restarting at 1.");
        }

        var number = series.NextNumber;
        series.NextNumber++;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return series.Format(number, documentDate);
    }
}

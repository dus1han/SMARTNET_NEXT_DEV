namespace Smartnet.Domain.Settings;

/// <summary>
/// Issues the next document number.
/// </summary>
/// <remarks>
/// Closes ISSUES B4. The legacy allocator takes a number from a ticket table, and nothing checks
/// that the resulting number is unused — which is how two different quotations, for two different
/// customers, both came to be numbered <c>STQ-0</c>. The number is now allocated by locking the
/// series row (<c>SELECT … FOR UPDATE</c>) inside the caller's transaction, so two concurrent
/// saves queue instead of colliding.
/// </remarks>
public interface IDocumentNumberAllocator
{
    /// <summary>
    /// Reserves the next number in the series and returns it formatted.
    /// </summary>
    /// <remarks>
    /// <b>Must be called inside the transaction that saves the document.</b> The row lock is what
    /// makes this safe, and a lock is only held for the life of a transaction: allocating in one
    /// transaction and saving in another hands the same number to whoever asks next if the save
    /// then fails.
    /// </remarks>
    /// <param name="documentDate">The document's date. The prefix may encode it — see DocumentNumberFormat.</param>
    /// <exception cref="InvalidOperationException">
    /// No series is configured for this company and document type. Thrown deliberately and loudly:
    /// the alternative is to invent a series starting at 1 and quietly reissue numbers that are
    /// already on two and a half thousand printed invoices.
    /// </exception>
    Task<string> AllocateAsync(
        long companyId,
        string docType,
        DateOnly documentDate,
        CancellationToken cancellationToken = default);
}

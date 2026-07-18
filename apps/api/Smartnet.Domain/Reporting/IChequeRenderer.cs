namespace Smartnet.Domain.Reporting;

/// <summary>Renders a cheque as an overlay for its pre-printed bank stationery.</summary>
/// <remarks>
/// Unlike every other document this produces no design of its own — it places the payee, the amount in
/// figures, the amount in words and the date onto the blanks in paper the bank has already printed. The
/// page is cheque-sized and must be printed unscaled.
/// </remarks>
public interface IChequeRenderer
{
    /// <summary>The cheque for <paramref name="chequeId"/> as a PDF, or null if it does not exist.</summary>
    Task<byte[]?> RenderAsync(long chequeId, CancellationToken cancellationToken = default);
}

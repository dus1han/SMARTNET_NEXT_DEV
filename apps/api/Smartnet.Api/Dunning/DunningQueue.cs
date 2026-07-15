using System.Threading.Channels;

namespace Smartnet.Api.Dunning;

/// <summary>One dunning email to send — a customer's outstanding statement, off the request thread.</summary>
/// <param name="EmailLogId">The <c>email_log</c> row created (as "queued") when this was accepted; the
/// background worker updates it to sent / failed / blocked.</param>
public sealed record DunningJob(
    long EmailLogId,
    long CompanyId,
    string CustomerCode,
    string CustomerName,
    string Recipient,
    decimal Outstanding);

/// <summary>The <c>email_log.status</c> values a dunning message moves through.</summary>
public static class DunningStatus
{
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Failed = "failed";

    /// <summary>Not sent on purpose — the kill switch is off (the gate) or no mail server is configured.</summary>
    public const string Blocked = "blocked";
}

/// <summary>
/// The dunning queue — an in-process channel, matched to the existing DI, no broker.
/// </summary>
/// <remarks>
/// This is what replaces the legacy <c>emailOSBulk</c>: a synchronous <c>foreach</c> that built a
/// workbook and opened a blocking <c>SmtpClient.Send</c> (<c>Timeout = 1_000_000</c>) per customer, all
/// on one HTTP request, so N debtors meant N SMTP handshakes in series and one slow recipient hung the
/// lot. Here the endpoint writes the log rows, enqueues, and returns at once; the background worker
/// drains the channel.
/// </remarks>
public interface IDunningChannel
{
    ValueTask EnqueueAsync(DunningJob job, CancellationToken cancellationToken = default);

    IAsyncEnumerable<DunningJob> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class DunningChannel : IDunningChannel
{
    // Bounded so a runaway caller cannot exhaust memory; Wait back-pressures the (already-returned)
    // enqueue rather than dropping a customer's statement.
    private readonly Channel<DunningJob> _channel = Channel.CreateBounded<DunningJob>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(DunningJob job, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<DunningJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

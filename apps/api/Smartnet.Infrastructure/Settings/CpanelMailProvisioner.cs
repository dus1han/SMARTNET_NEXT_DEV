using System.Net.Http.Headers;
using System.Text.Json;

namespace Smartnet.Infrastructure.Settings;

/// <summary>The cPanel connection — where to reach it and the token to reach it with, already decrypted.</summary>
public sealed record CpanelCredentials(string Host, int Port, string Username, string ApiToken);

/// <summary>
/// Creates and updates real mailboxes on the host through cPanel's API, so an account added in this app
/// appears in cPanel and Roundcube rather than only in our database.
/// </summary>
/// <remarks>
/// Push only, and never a delete: the app creates a mailbox and can change its password, but removing an
/// account here leaves the real mailbox — and its stored mail — untouched on the host. Destroying a live
/// mailbox is only ever done deliberately in cPanel.
/// </remarks>
public interface ICpanelMailProvisioner
{
    /// <summary>Creates the mailbox on the host (UAPI <c>Email::add_pop</c>), unlimited quota.</summary>
    Task CreateMailboxAsync(CpanelCredentials credentials, string emailAddress, string password, CancellationToken cancellationToken = default);

    /// <summary>Changes the mailbox's password on the host (UAPI <c>Email::passwd_pop</c>).</summary>
    Task SetPasswordAsync(CpanelCredentials credentials, string emailAddress, string password, CancellationToken cancellationToken = default);

    /// <summary>Deletes the mailbox on the host (UAPI <c>Email::delete_pop</c>) — and its stored mail with it.</summary>
    Task DeleteMailboxAsync(CpanelCredentials credentials, string emailAddress, CancellationToken cancellationToken = default);
}

/// <summary>cPanel refused, or could not be reached. The message is the host's own, for the administrator.</summary>
public sealed class CpanelProvisioningException(string message) : Exception(message);

/// <inheritdoc cref="ICpanelMailProvisioner"/>
public sealed class CpanelMailProvisioner : ICpanelMailProvisioner
{
    private readonly HttpClient _http;

    public CpanelMailProvisioner(HttpClient http) => _http = http;

    public Task CreateMailboxAsync(CpanelCredentials credentials, string emailAddress, string password, CancellationToken cancellationToken = default)
    {
        var (local, domain) = Split(emailAddress);

        return CallAsync(credentials, "add_pop", new Dictionary<string, string>
        {
            ["email"] = local,
            ["domain"] = domain,
            ["password"] = password,
            ["quota"] = "0", // unlimited
        }, cancellationToken);
    }

    public Task SetPasswordAsync(CpanelCredentials credentials, string emailAddress, string password, CancellationToken cancellationToken = default)
    {
        var (local, domain) = Split(emailAddress);

        return CallAsync(credentials, "passwd_pop", new Dictionary<string, string>
        {
            ["email"] = local,
            ["domain"] = domain,
            ["password"] = password,
        }, cancellationToken);
    }

    public Task DeleteMailboxAsync(CpanelCredentials credentials, string emailAddress, CancellationToken cancellationToken = default)
    {
        var (local, domain) = Split(emailAddress);

        return CallAsync(credentials, "delete_pop", new Dictionary<string, string>
        {
            ["email"] = local,
            ["domain"] = domain,
        }, cancellationToken);
    }

    /// <summary>
    /// The bare hostname from whatever was entered — an administrator may paste a full cPanel URL
    /// (<c>https://host.example:2083/</c>) rather than just the host, and the request URL is built around this,
    /// so a scheme, a path or an embedded port would otherwise produce a nonsense address.
    /// </summary>
    private static string CleanHost(string raw)
    {
        var host = raw.Trim();

        var scheme = host.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            host = host[(scheme + 3)..];
        }

        var slash = host.IndexOf('/');
        if (slash >= 0)
        {
            host = host[..slash];
        }

        var colon = host.IndexOf(':');
        if (colon >= 0)
        {
            host = host[..colon];
        }

        return host;
    }

    private static (string Local, string Domain) Split(string emailAddress)
    {
        var at = emailAddress.LastIndexOf('@');

        if (at <= 0 || at == emailAddress.Length - 1)
        {
            throw new CpanelProvisioningException(
                $"'{emailAddress}' is not an address cPanel can create — it needs a mailbox and a domain.");
        }

        return (emailAddress[..at], emailAddress[(at + 1)..]);
    }

    /// <summary>
    /// Calls a UAPI Email function over POST — the password rides in the form body, never the query string,
    /// so it stays out of the host's request log. The token authenticates the call.
    /// </summary>
    private async Task CallAsync(
        CpanelCredentials credentials,
        string function,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var url = $"https://{CleanHost(credentials.Host)}:{credentials.Port}/execute/Email/{function}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form),
        };

        // cPanel's token scheme: "Authorization: cpanel <user>:<token>".
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "cpanel", $"{credentials.Username}:{credentials.ApiToken}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && ex is not OperationCanceledException || ex is HttpRequestException)
        {
            throw new CpanelProvisioningException(
                $"Could not reach cPanel at {credentials.Host}:{credentials.Port}. Check the host, the port, "
                + $"and that this server may connect to it. ({ex.Message})");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // A 401/403 here is almost always the token or username; say so rather than dumping HTML.
            throw new CpanelProvisioningException(
                $"cPanel returned {(int)response.StatusCode} for the mailbox operation — most often the API "
                + "token or cPanel username is wrong. Check them under the mail server settings.");
        }

        Report(body);
    }

    /// <summary>UAPI answers with <c>{ "status": 1|0, "errors": [...] }</c>. A zero status is a refusal.</summary>
    private static void Report(string body)
    {
        int status;
        var errors = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            status = root.TryGetProperty("status", out var s) ? s.GetInt32() : 0;

            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
            {
                errors.AddRange(e.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0));
            }
        }
        catch (JsonException)
        {
            throw new CpanelProvisioningException("cPanel returned a response this app could not read.");
        }

        if (status != 1)
        {
            throw new CpanelProvisioningException(errors.Count > 0
                ? string.Join(" ", errors)
                : "cPanel refused the mailbox operation without saying why.");
        }
    }
}

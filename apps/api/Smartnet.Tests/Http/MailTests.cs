using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Smartnet.Tests.Http;

/// <summary>
/// The user-facing mail screen, over HTTP. The IMAP/SMTP conversation needs a real server and is not
/// exercised here; what is, is the boundary that matters — every endpoint is scoped to the caller's own
/// assigned mailboxes, so a mailbox nobody assigned to them is invisible and unreachable.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class MailTests
{
    private readonly ApiFixture _api;

    public MailTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task The_mail_screen_only_reaches_mailboxes_assigned_to_the_caller()
    {
        var client = _api.SignedIn;

        // A mailbox exists, but is not assigned to the signed-in user.
        var mailboxId = await CreateMailboxAsync(client, "unassigned@smart-net.lk");

        // Reading it, reading a message in it, and sending as it are all "not one of yours".
        (await client.GetAsync($"/api/mail/{mailboxId}/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/mail/{mailboxId}/messages/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Send is multipart/form-data (files ride along); a well-formed one still 404s for a mailbox that
        // is not the caller's.
        using var send = new HttpRequestMessage(HttpMethod.Post, $"/api/mail/{mailboxId}/send")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["to"] = "someone@example.com",
                ["subject"] = "Hi",
                ["body"] = "Hi",
            }),
        };
        (await client.SendAsync(send)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The switcher is reachable, and does not list the mailbox the caller was never given.
        var response = await client.GetAsync("/api/mail/mailboxes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).Should().NotContain(mailboxId);
    }

    private static async Task<long> CreateMailboxAsync(HttpClient client, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mail-accounts")
        {
            Content = JsonContent.Create(new { displayName = "Unassigned", emailAddress = email, password = "x", enabled = true }),
        };
        request.Headers.Add("X-Change-Reason", "Adding a mailbox for the mail-scope test.");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Smartnet.Tests.Http;

/// <summary>
/// The mail-accounts CRUD, over HTTP — the shared server, and add/list/edit/disable/delete of the mailboxes
/// on it, with the write-only password.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class MailAccountsTests
{
    private readonly ApiFixture _api;

    public MailAccountsTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task The_shared_server_settings_round_trip()
    {
        var client = _api.SignedIn;

        using var save = new HttpRequestMessage(HttpMethod.Put, "/api/mail-accounts/server-settings")
        {
            Content = JsonContent.Create(new
            {
                mailDomain = "smart-net.lk",
                outgoingHost = "mail.smart-net.lk",
                outgoingPort = 587,
                outgoingUseSsl = true,
                incomingProtocol = "IMAP",
                incomingHost = "mail.smart-net.lk",
                incomingPort = 993,
                incomingUseSsl = true,
                cpanelHost = (string?)null,
                cpanelPort = 2083,
                cpanelUsername = (string?)null,
                cpanelApiToken = (string?)null,
            }),
        };
        save.Headers.Add("X-Change-Reason", "Configuring the shared mail server.");
        (await client.SendAsync(save)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var got = await client.GetFromJsonAsync<JsonElement>("/api/mail-accounts/server-settings");
        got.GetProperty("mailDomain").GetString().Should().Be("smart-net.lk");
        got.GetProperty("outgoingHost").GetString().Should().Be("mail.smart-net.lk");
        got.GetProperty("incomingProtocol").GetString().Should().Be("IMAP");
        got.GetProperty("incomingPort").GetInt32().Should().Be(993);

        // The domain is also readable from the lighter endpoint the add screen uses.
        var domain = await client.GetFromJsonAsync<JsonElement>("/api/mail-accounts/domain");
        domain.GetProperty("domain").GetString().Should().Be("smart-net.lk");
    }

    [Fact]
    public async Task Add_list_edit_disable_and_delete_a_mail_account()
    {
        var client = _api.SignedIn;

        // --- Add ---
        var created = await PostAsync(client, Body("Smart Net Sales", "sales@smart-net.lk", password: "s3cret!"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        // --- List: present, password redacted to a flag, no password field ---
        var listed = await GetOneAsync(client, id);
        listed.GetProperty("displayName").GetString().Should().Be("Smart Net Sales");
        listed.GetProperty("emailAddress").GetString().Should().Be("sales@smart-net.lk");
        listed.GetProperty("hasPassword").GetBoolean().Should().BeTrue();
        listed.GetProperty("enabled").GetBoolean().Should().BeTrue();
        listed.TryGetProperty("password", out _).Should().BeFalse();
        listed.TryGetProperty("passwordEncrypted", out _).Should().BeFalse();

        // --- Edit + disable, no password: the stored one must survive ---
        using var edit = new HttpRequestMessage(HttpMethod.Put, $"/api/mail-accounts/{id}")
        {
            Content = JsonContent.Create(Body("Sales (EU)", "sales@smart-net.lk", password: null, enabled: false)),
        };
        edit.Headers.Add("X-Change-Reason", "Renaming and disabling the sales mailbox.");
        (await client.SendAsync(edit)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterEdit = await GetOneAsync(client, id);
        afterEdit.GetProperty("displayName").GetString().Should().Be("Sales (EU)");
        afterEdit.GetProperty("enabled").GetBoolean().Should().BeFalse();
        afterEdit.GetProperty("hasPassword").GetBoolean().Should().BeTrue(); // kept, not wiped

        // --- Delete: gone from the list ---
        using var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/mail-accounts/{id}");
        del.Headers.Add("X-Change-Reason", "Removing the sales mailbox.");
        (await client.SendAsync(del)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync(client)).Should().NotContain(a => a.GetProperty("id").GetInt64() == id);
    }

    [Fact]
    public async Task A_change_without_a_reason_is_refused()
    {
        var response = await _api.SignedIn.PostAsJsonAsync(
            "/api/mail-accounts", Body("No Reason", "noreason@smart-net.lk", password: "x"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adding_an_address_that_already_exists_is_refused()
    {
        var client = _api.SignedIn;

        (await PostAsync(client, Body("First", "duplicate@smart-net.lk", password: "x")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Same address again — turned away, not added a second time.
        (await PostAsync(client, Body("Second", "duplicate@smart-net.lk", password: "x")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // And the address is matched case-insensitively — a mailbox is the same mailbox in any case.
        (await PostAsync(client, Body("Third", "Duplicate@Smart-Net.LK", password: "x")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Only the first one is there.
        (await ListAsync(client))
            .Count(a => string.Equals(
                a.GetProperty("emailAddress").GetString(), "duplicate@smart-net.lk",
                StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private static object Body(string displayName, string emailAddress, string? password, bool enabled = true) =>
        new { displayName, emailAddress, password, enabled };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mail-accounts")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Change-Reason", "Adding a mailbox for the tests.");
        return await client.SendAsync(request);
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client)
    {
        var array = await client.GetFromJsonAsync<JsonElement>("/api/mail-accounts");
        return array.EnumerateArray().ToList();
    }

    private static async Task<JsonElement> GetOneAsync(HttpClient client, long id) =>
        (await ListAsync(client)).Single(a => a.GetProperty("id").GetInt64() == id);
}

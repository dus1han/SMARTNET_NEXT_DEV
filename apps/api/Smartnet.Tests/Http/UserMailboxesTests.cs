using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Smartnet.Tests.Http;

/// <summary>
/// Assigning shared mailboxes to a user over HTTP — the set is replaced wholesale, unassigning drops a
/// mailbox from the list, and re-assigning one brings it back (the restore path behind the unique index).
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class UserMailboxesTests
{
    private static readonly string[] EmailAndDashboard = ["email", "dashboard"];

    private readonly ApiFixture _api;

    public UserMailboxesTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Assign_unassign_and_reassign_mailboxes_for_a_user()
    {
        var client = _api.SignedIn;

        var mailboxId = await CreateMailboxAsync(client, "assign-me@smart-net.lk");
        var userId = await CreateUserAsync(client, "mailbox.tester");
        await GrantEmailAsync(client, userId);

        // --- Assign ---
        (await SetMailboxesAsync(client, userId, mailboxId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MailboxIdsOf(client, userId)).Should().Contain(mailboxId);

        // The catalogue the assign dialog reads lists it too.
        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/users/mailboxes");
        catalogue.EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).Should().Contain(mailboxId);

        // --- Unassign (empty set) ---
        (await SetMailboxesAsync(client, userId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MailboxIdsOf(client, userId)).Should().NotContain(mailboxId);

        // --- Re-assign: the previously-unassigned row is restored, not duplicated ---
        (await SetMailboxesAsync(client, userId, mailboxId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MailboxIdsOf(client, userId)).Should().ContainSingle().Which.Should().Be(mailboxId);
    }

    [Fact]
    public async Task Assigning_a_mailbox_that_does_not_exist_is_refused()
    {
        var client = _api.SignedIn;
        var userId = await CreateUserAsync(client, "mailbox.badref");
        await GrantEmailAsync(client, userId);

        (await SetMailboxesAsync(client, userId, 987654321)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assigning_to_a_user_without_the_email_permission_is_refused()
    {
        var client = _api.SignedIn;
        var mailboxId = await CreateMailboxAsync(client, "gated@smart-net.lk");
        var userId = await CreateUserAsync(client, "mailbox.noemail"); // created with no permissions

        // Blocked until Email is granted...
        (await SetMailboxesAsync(client, userId, mailboxId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // ...but clearing is always allowed (so Email can be revoked, then the mailboxes cleared).
        (await SetMailboxesAsync(client, userId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Grant Email, and the same assignment now goes through.
        await GrantEmailAsync(client, userId);
        (await SetMailboxesAsync(client, userId, mailboxId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MailboxIdsOf(client, userId)).Should().Contain(mailboxId);
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private static async Task<long> CreateMailboxAsync(HttpClient client, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mail-accounts")
        {
            Content = JsonContent.Create(new { displayName = "Assignable", emailAddress = email, password = "x", enabled = true }),
        };
        request.Headers.Add("X-Change-Reason", "Adding a mailbox to assign in the tests.");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private static async Task<long> CreateUserAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync(
            "/api/users", new { username, name = "Mailbox Tester", roleIds = Array.Empty<long>() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private static async Task GrantEmailAsync(HttpClient client, long userId)
    {
        // SetPermissions refuses a blind write, so read the user's current version first. One dashboard is
        // mandatory in a permission set, so the request carries the management one alongside Email.
        var users = await client.GetFromJsonAsync<JsonElement>("/api/users");
        var rowVersion = users.EnumerateArray()
            .Single(u => u.GetProperty("id").GetInt64() == userId)
            .GetProperty("rowVersion").GetInt32();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{userId}/permissions")
        {
            Content = JsonContent.Create(new { permissions = EmailAndDashboard, expectedRowVersion = rowVersion }),
        };
        request.Headers.Add("X-Change-Reason", "Granting Email for the mailbox tests.");
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SetMailboxesAsync(HttpClient client, long userId, params long[] mailAccountIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{userId}/mailboxes")
        {
            Content = JsonContent.Create(new { mailAccountIds }),
        };
        return await client.SendAsync(request);
    }

    private static async Task<List<long>> MailboxIdsOf(HttpClient client, long userId)
    {
        var response = await client.GetAsync("/api/users");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var doc = JsonDocument.Parse(body);
        var user = doc.RootElement.EnumerateArray().Single(u => u.GetProperty("id").GetInt64() == userId);
        return user.GetProperty("mailboxes").EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).ToList();
    }
}

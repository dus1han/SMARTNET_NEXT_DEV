using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Smartnet.Tests.Http;

/// <summary>
/// The request pipeline, over HTTP — everything between the socket and the controller.
/// </summary>
/// <remarks>
/// These are the guarantees that live in <c>Program.cs</c> rather than in any service, so no
/// service-level test can hold them: deny-by-default authentication, the permission policies, the
/// change-reason filter, the correlation id, and an exception handler that says nothing useful to an
/// attacker. Until this file existed a middleware could have been deleted and the rest of the suite
/// would have stayed green.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class PipelineTests
{
    private readonly ApiFixture _api;

    public PipelineTests(ApiFixture api) => _api = api;

    // --- Authentication ---------------------------------------------------------------------------

    /// <summary>
    /// Deny by default: a business endpoint refuses an anonymous caller.
    /// </summary>
    /// <remarks>
    /// The legacy app's failure was the opposite — it hid the menu item and left the endpoint open, so
    /// any logged-in user could reach anything they could guess the URL of (ISSUES A5).
    /// </remarks>
    [Theory]
    [InlineData("/api/invoices")]
    [InlineData("/api/customers")]
    [InlineData("/api/users")]
    [InlineData("/api/notes")]
    [InlineData("/api/reports/data-exceptions")]
    public async Task An_anonymous_request_to_a_business_endpoint_is_refused(string path)
    {
        using var client = _api.NewClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_is_open_and_says_only_whether_it_is_up()
    {
        using var client = _api.NewClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("healthy");

        // It is unauthenticated, so it must not become a reconnaissance endpoint: no version, no
        // connection string, no row counts (the Phase 0 /_smoke endpoint was removed for exactly that).
        body.Should().NotContainAny("Server=", "Password", "smartnet_invsys_dev");
    }

    [Fact]
    public async Task A_signed_in_caller_reaches_the_same_endpoint()
    {
        var client = _api.SignedIn;

        var response = await client.GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_in_with_the_wrong_password_is_refused_and_says_nothing_useful()
    {
        using var client = _api.NewClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = ApiFixture.Username, password = "not-the-password" });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Never "no such user" vs "wrong password" — that difference enumerates accounts.
        body.Should().NotContainAny("no such user", "does not exist", "unknown user");
    }

    [Fact]
    public async Task The_auth_cookie_is_httponly_so_script_cannot_read_it()
    {
        using var client = _api.NewClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = ApiFixture.Username, password = ApiFixture.Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

        cookies.Should().NotBeEmpty("signing in must set the auth cookie");
        cookies.Should().Contain(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase),
            "a token readable by script is a token stealable by an XSS bug");
        cookies.Should().Contain(c => c.Contains("samesite", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Repeated sign-in attempts are eventually refused.
    /// </summary>
    /// <remarks>
    /// <b>This test exists because it happened by accident.</b> The first version of this fixture logged
    /// in per test, and around the tenth the suite started failing with 429s — the limiter doing exactly
    /// its job, because a test suite hammering <c>/api/auth/login</c> is indistinguishable from someone
    /// working through a password list. Rather than only work around it, it is pinned here: brute force
    /// against a login endpoint is the attack this app is most likely to actually see, and nothing else
    /// in the suite would notice if the limiter were removed.
    ///
    /// <para>It uses its own client and deliberately exhausts the limit, which is safe because the
    /// fixture established its shared session before any test ran.</para>
    /// </remarks>
    [Fact]
    public async Task Hammering_the_login_endpoint_is_eventually_refused()
    {
        using var client = _api.NewClient();

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { username = ApiFixture.Username, password = "wrong-on-purpose" });

            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "an unlimited login endpoint is a password-guessing endpoint");
    }

    // --- The change-reason filter -----------------------------------------------------------------

    /// <summary>
    /// A reason-gated endpoint refuses the request before it reaches the controller.
    /// </summary>
    /// <remarks>
    /// AUDIT.md makes a reason mandatory on destructive changes, and the enforcement is a filter on the
    /// pipeline rather than a hopeful frontend. A 404 here would mean the filter ran second and the
    /// controller decided first — the reason must be demanded whether or not the record exists.
    /// </remarks>
    [Fact]
    public async Task A_reason_gated_delete_without_a_reason_is_refused()
    {
        var client = _api.SignedIn;

        var response = await client.DeleteAsync("/api/notes/999999?expectedRowVersion=1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("X-Change-Reason");
    }

    [Fact]
    public async Task A_reason_that_is_too_short_to_explain_anything_is_refused()
    {
        var client = _api.SignedIn;

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/notes/999999?expectedRowVersion=1");
        request.Headers.Add("X-Change-Reason", "no");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task With_a_real_reason_the_request_reaches_the_controller()
    {
        var client = _api.SignedIn;

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/notes/999999?expectedRowVersion=1");
        request.Headers.Add("X-Change-Reason", "Removing a note that was filed against the wrong record.");

        var response = await client.SendAsync(request);

        // 404 is the *point*: the filter let it through and the controller answered on the merits.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Correlation id ---------------------------------------------------------------------------

    /// <summary>
    /// Every response carries the id that ties it to its log line.
    /// </summary>
    /// <remarks>
    /// This is what makes a generic error message acceptable: the user reads back a reference, and it
    /// finds the one request in the log. Without it, "something went wrong" is unanswerable.
    /// </remarks>
    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        var client = _api.SignedIn;

        var response = await client.GetAsync("/api/customers");

        response.Headers.Should().ContainSingle(h =>
            h.Key.Contains("correlation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_validation_failure_carries_the_correlation_id_in_the_body_too()
    {
        var client = _api.SignedIn;

        var response = await client.DeleteAsync("/api/notes/999999?expectedRowVersion=1");
        var body = await response.Content.ReadAsStringAsync();

        // The screen shows this to the user; it has to be in the payload, not only in a header the
        // browser never surfaces.
        body.Should().Contain("correlationId");
    }

    // --- What errors say --------------------------------------------------------------------------

    /// <summary>
    /// A bad request answers in RFC 9457 problem+json, not in a stack trace.
    /// </summary>
    [Fact]
    public async Task An_error_answers_as_problem_details_and_leaks_no_internals()
    {
        var client = _api.SignedIn;

        // A note with no title — rejected by the controller, so a real error path, not a 404.
        var response = await client.PostAsJsonAsync("/api/notes", new { title = "", body = "no title" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        body.Should().NotContainAny(
            "Microsoft.EntityFrameworkCore",
            "MySqlConnector",
            "at Smartnet.",
            "StackTrace",
            "ConnectionString");
    }

    [Fact]
    public async Task A_404_is_a_404_and_not_an_exception_page()
    {
        var client = _api.SignedIn;

        var response = await client.GetAsync("/api/does-not-exist");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().NotContain("Exception");
    }

    // --- CORS -------------------------------------------------------------------------------------

    /// <summary>
    /// The browser origin the app is served from is allowed; an arbitrary one is not.
    /// </summary>
    /// <remarks>
    /// Largely vestigial in production — the browser calls <c>/api</c> relatively through nginx, so
    /// requests are same-origin and never preflight. It is pinned anyway because the setting is the
    /// API's allow-list, and a permissive default is the kind of leftover that looks deliberate.
    /// </remarks>
    [Fact]
    public async Task The_configured_web_origin_is_allowed_and_a_stranger_is_not()
    {
        using var client = _api.NewClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/customers");
        allowed.Headers.Add("Origin", ApiFixture.CorsOrigin);
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        var allowedResponse = await client.SendAsync(allowed);

        using var stranger = new HttpRequestMessage(HttpMethod.Options, "/api/customers");
        stranger.Headers.Add("Origin", "https://not-our-app.example");
        stranger.Headers.Add("Access-Control-Request-Method", "GET");
        var strangerResponse = await client.SendAsync(stranger);

        allowedResponse.Headers.Should().Contain(h => h.Key == "Access-Control-Allow-Origin");
        strangerResponse.Headers.Should().NotContain(h => h.Key == "Access-Control-Allow-Origin");
    }

    // --- The shape of a real response -------------------------------------------------------------

    /// <summary>
    /// Model binding, serialisation and the JSON contract, end to end.
    /// </summary>
    /// <remarks>
    /// The generated TypeScript client is built from this shape. A service test asserts the object; only
    /// an HTTP test asserts what actually goes over the wire — camelCase, and a body the client can read.
    /// </remarks>
    [Fact]
    public async Task A_created_note_round_trips_over_the_wire_in_the_shape_the_client_expects()
    {
        var client = _api.SignedIn;

        var created = await client.PostAsJsonAsync("/api/notes",
            new { title = "Pipeline test", body = "Written over HTTP." });

        created.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // camelCase, as the generated client's types assume.
        root.TryGetProperty("title", out _).Should().BeTrue();
        root.TryGetProperty("rowVersion", out _).Should().BeTrue();
        root.GetProperty("title").GetString().Should().Be("Pipeline test");

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/notes");
        listed.EnumerateArray().Should().Contain(n => n.GetProperty("title").GetString() == "Pipeline test");
    }
}

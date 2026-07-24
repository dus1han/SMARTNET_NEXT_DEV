using System.Net;
using FluentAssertions;
using Smartnet.Infrastructure.Settings;

namespace Smartnet.Tests.Settings;

/// <summary>
/// The cPanel client — how it shapes a UAPI call and how it reads the answer. No network: the HTTP handler
/// is a stub, so success, a refusal, a rejected token and a bad address are all exercised in-process.
/// </summary>
public sealed class CpanelMailProvisionerTests
{
    private static readonly CpanelCredentials Creds = new("mail.example.com", 2083, "cpuser", "TOKEN123");

    [Fact]
    public async Task Create_posts_add_pop_with_the_split_address_and_the_token()
    {
        var (provisioner, handler) = Make(Ok("""{"status":1,"errors":null}"""));

        await provisioner.CreateMailboxAsync(Creds, "sales@example.com", "s3cret", default);

        handler.Request!.RequestUri!.ToString().Should().Be("https://mail.example.com:2083/execute/Email/add_pop");
        handler.Request.Headers.Authorization!.ToString().Should().Be("cpanel cpuser:TOKEN123");
        handler.Body.Should().Contain("email=sales").And.Contain("domain=example.com").And.Contain("quota=0");
    }

    [Fact]
    public async Task A_host_pasted_as_a_url_is_cleaned_to_the_bare_hostname()
    {
        var (provisioner, handler) = Make(Ok("""{"status":1}"""));
        var pasted = new CpanelCredentials("https://uniform.de.hostns.io/", 2083, "cpuser", "T");

        await provisioner.CreateMailboxAsync(pasted, "a@b.com", "pw", default);

        handler.Request!.RequestUri!.ToString().Should().Be("https://uniform.de.hostns.io:2083/execute/Email/add_pop");
    }

    [Fact]
    public async Task Set_password_posts_passwd_pop()
    {
        var (provisioner, handler) = Make(Ok("""{"status":1}"""));

        await provisioner.SetPasswordAsync(Creds, "sales@example.com", "n3w", default);

        handler.Request!.RequestUri!.AbsolutePath.Should().EndWith("/execute/Email/passwd_pop");
    }

    [Fact]
    public async Task Delete_posts_delete_pop_with_the_split_address()
    {
        var (provisioner, handler) = Make(Ok("""{"status":1}"""));

        await provisioner.DeleteMailboxAsync(Creds, "sales@example.com", default);

        handler.Request!.RequestUri!.AbsolutePath.Should().EndWith("/execute/Email/delete_pop");
        handler.Body.Should().Contain("email=sales").And.Contain("domain=example.com");
    }

    [Fact]
    public async Task A_refusal_throws_the_cpanel_message()
    {
        var (provisioner, _) = Make(Ok("""{"status":0,"errors":["The password you entered is too weak."]}"""));

        var act = () => provisioner.CreateMailboxAsync(Creds, "sales@example.com", "weak", default);

        (await act.Should().ThrowAsync<CpanelProvisioningException>()).WithMessage("*too weak*");
    }

    [Fact]
    public async Task An_http_401_points_at_the_token()
    {
        var (provisioner, _) = Make(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Access denied"),
        });

        var act = () => provisioner.SetPasswordAsync(Creds, "sales@example.com", "pw", default);

        (await act.Should().ThrowAsync<CpanelProvisioningException>()).WithMessage("*token*");
    }

    [Fact]
    public async Task An_address_without_a_domain_is_refused_before_any_call()
    {
        var (provisioner, handler) = Make(Ok("""{"status":1}"""));

        var act = () => provisioner.CreateMailboxAsync(Creds, "no-at-sign", "pw", default);

        await act.Should().ThrowAsync<CpanelProvisioningException>();
        handler.Request.Should().BeNull(); // never left the building
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private static (CpanelMailProvisioner, CapturingHandler) Make(HttpResponseMessage response)
    {
        var handler = new CapturingHandler(response);
        return (new CpanelMailProvisioner(new HttpClient(handler)), handler);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}

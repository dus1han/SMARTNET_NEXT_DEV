using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Storage;
using Xunit;

namespace Smartnet.Tests.Documents;

/// <summary>
/// The document store (Phase 7, slice 4).
/// </summary>
/// <remarks>
/// Real files in a temp directory rather than a mocked filesystem: the things worth testing here are
/// whether a path can escape the root and whether the bytes that come back are the bytes that went in,
/// and neither is answered by a mock that agrees with the code.
/// </remarks>
public sealed class DocumentStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "smartnet-doc-tests", Guid.NewGuid().ToString("N"));

    private LocalFileDocumentStorage NewStorage() => new(
        Options.Create(new DocumentStorageOptions { RootPath = _root }),
        NullLogger<LocalFileDocumentStorage>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task What_is_saved_is_what_comes_back()
    {
        var storage = NewStorage();
        var content = Encoding.UTF8.GetBytes("the quick brown fox");

        var saved = await storage.SaveAsync(new MemoryStream(content), ".pdf", CancellationToken.None);

        await using var read = await storage.OpenReadAsync(saved.StoredName, CancellationToken.None);
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer, CancellationToken.None);

        buffer.ToArray().Should().Equal(content);
        saved.ByteSize.Should().Be(content.Length);
    }

    [Fact]
    public async Task The_hash_describes_the_bytes_written()
    {
        var storage = NewStorage();
        var content = Encoding.UTF8.GetBytes("invoice scan");
        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var saved = await storage.SaveAsync(new MemoryStream(content), ".pdf", CancellationToken.None);

        // Computed as the file was written, so it is a statement about what landed on disk — which is what
        // lets the legacy migration verify a BLOB was materialised rather than merely attempted.
        saved.Sha256.Should().Be(expected);
    }

    [Fact]
    public async Task Two_uploads_of_the_same_filename_do_not_collide()
    {
        var storage = NewStorage();

        var first = await storage.SaveAsync(new MemoryStream([1, 2, 3]), ".pdf", CancellationToken.None);
        var second = await storage.SaveAsync(new MemoryStream([4, 5, 6]), ".pdf", CancellationToken.None);

        second.StoredName.Should().NotBe(first.StoredName);

        // And the first is still intact — a collision would have overwritten it.
        await using var read = await storage.OpenReadAsync(first.StoredName, CancellationToken.None);
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer, CancellationToken.None);
        buffer.ToArray().Should().Equal([1, 2, 3]);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config\\sam")]
    [InlineData("ab/../../secret.pdf")]
    [InlineData("")]
    [InlineData("a")]
    public async Task A_name_that_tries_to_leave_the_root_reads_nothing(string hostile)
    {
        var storage = NewStorage();

        var stream = await storage.OpenReadAsync(hostile, CancellationToken.None);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task A_deleted_file_is_gone()
    {
        var storage = NewStorage();
        var saved = await storage.SaveAsync(new MemoryStream([9]), ".pdf", CancellationToken.None);

        await storage.DeleteAsync(saved.StoredName, CancellationToken.None);

        (await storage.ExistsAsync(saved.StoredName, CancellationToken.None)).Should().BeFalse();
        (await storage.OpenReadAsync(saved.StoredName, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Deleting_something_already_gone_is_not_an_error()
    {
        var storage = NewStorage();

        // The delete endpoint soft-deletes the row first, so a retry after a partial failure lands here.
        var act = async () => await storage.DeleteAsync("ffffffffffffffffffffffffffffffff.pdf", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Nothing_is_written_outside_the_root()
    {
        var storage = NewStorage();

        await storage.SaveAsync(new MemoryStream([1]), ".pdf", CancellationToken.None);

        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Should().OnlyContain(f => f.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal));
    }
}

/// <summary>The upload whitelist (Phase 7, slice 4) — what the policy admits and what it refuses.</summary>
public sealed class DocumentPolicyTests
{
    [Theory]
    [InlineData("scan.pdf", ".pdf")]
    [InlineData("Report.PDF", ".pdf")]
    [InlineData("sheet.xlsx", ".xlsx")]
    [InlineData("photo.JPEG", ".jpeg")]
    public void An_allowed_extension_is_recognised_whatever_its_case(string name, string expected) =>
        DocumentPolicy.ExtensionOf(name).Should().Be(expected);

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("script.js")]
    [InlineData("page.html")]
    [InlineData("shell.sh")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_not_on_the_list_is_refused(string? name) =>
        DocumentPolicy.ExtensionOf(name).Should().BeNull();

    [Fact]
    public void A_double_extension_is_judged_on_the_last_one()
    {
        // invoice.pdf.exe is an executable wearing a PDF's name, and the last extension is the one the
        // operating system acts on.
        DocumentPolicy.ExtensionOf("invoice.pdf.exe").Should().BeNull();
        DocumentPolicy.ExtensionOf("invoice.exe.pdf").Should().Be(".pdf");
    }

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("C:\\Users\\me\\scan.pdf", "scan.pdf")]
    [InlineData("/var/www/thing.pdf", "thing.pdf")]
    public void Path_components_never_survive_the_display_name(string hostile, string expected) =>
        DocumentPolicy.SafeDisplayName(hostile).Should().Be(expected);

    [Fact]
    public void A_quote_cannot_break_out_of_the_content_disposition_header() =>
        DocumentPolicy.SafeDisplayName("in\"voice.pdf").Should().Be("invoice.pdf");

    [Fact]
    public void The_served_content_type_comes_from_the_extension_not_the_upload() =>
        DocumentPolicy.ContentTypeFor(".pdf").Should().Be("application/pdf");
}

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Smartnet.Api.Auth;
using Smartnet.Api.Controllers;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;

namespace Smartnet.Tests.Documents;

/// <summary>
/// A draft is reachable by exactly the people who could raise the document it will become.
/// </summary>
/// <remarks>
/// <para><b>Why this needs a test rather than a comment.</b> <see cref="DraftsController"/> cannot use
/// <c>[RequirePermission]</c> the way every other controller does: one controller serves four document
/// types with four different permissions, so it resolves the permission from the draft's own
/// <c>doc_type</c> at runtime. That moves the decision out of the attribute the endpoint-authorisation
/// test can see, and into a dictionary — where it can drift from the create endpoints without anything
/// noticing.</para>
///
/// <para>It would drift quietly, and in the dangerous direction. A draft holds the whole document: who
/// the customer is, what they are being charged, what it costs. If <c>InvoicesController.Create</c> were
/// re-gated one day and the map here were not, the draft of an invoice would be readable by people the
/// invoice itself refuses — and nothing about the invoice screen would look wrong.</para>
/// </remarks>
public sealed class DocumentDraftPermissionTests
{
    /// <summary>
    /// Every draft type's permission is the one its create endpoint requires — read off the endpoint,
    /// not restated here, so this cannot be "fixed" by editing the expectation.
    /// </summary>
    [Theory]
    [InlineData(DocumentTypes.Quotation, typeof(QuotationsController))]
    [InlineData(DocumentTypes.Invoice, typeof(InvoicesController))]
    [InlineData(DocumentTypes.PurchaseOrder, typeof(PurchaseOrdersController))]
    [InlineData(DocumentTypes.JobCard, typeof(JobCardsController))]
    public void A_drafts_permission_is_its_create_endpoints_permission(string docType, Type controller)
    {
        var onCreate = CreatePermissionOf(controller);

        DraftDocumentTypes.PermissionFor(docType).Should().Be(
            onCreate,
            because:
                $"a {docType} draft carries the same commercial detail as the document, so gating it "
                + $"any more loosely than {controller.Name}.Create leaks that detail to someone the "
                + "create endpoint refuses. Change both, or neither.");
    }

    /// <summary>The four screens that keep drafts, and no others.</summary>
    /// <remarks>
    /// Adding a fifth is a real decision — the create screen has to autosave and the list has to grow a
    /// Drafts tab — so it should fail here first rather than appear half-built.
    /// </remarks>
    [Fact]
    public void Only_the_four_create_screens_keep_drafts()
    {
        DraftDocumentTypes.All.Should().BeEquivalentTo([
            DocumentTypes.Quotation,
            DocumentTypes.Invoice,
            DocumentTypes.PurchaseOrder,
            DocumentTypes.JobCard,
        ]);
    }

    /// <summary>Every draft type is a document type the numbering series already knows.</summary>
    /// <remarks>
    /// The two vocabularies have to be the same one. A draft type invented here would be a string the
    /// document it becomes has never heard of.
    /// </remarks>
    [Fact]
    public void Every_draft_type_is_a_known_document_type()
    {
        foreach (var docType in DraftDocumentTypes.All)
        {
            DocumentTypes.IsKnown(docType).Should().BeTrue(
                $"'{docType}' is offered as a draft type but is not in DocumentTypes.All");
        }
    }

    [Fact]
    public void A_type_that_keeps_no_drafts_resolves_to_no_permission()
    {
        // The controller turns this into a 400, not a 403 — see DraftsController.Denied. It matters that
        // null means "no such draft type" and never "no permission needed".
        DraftDocumentTypes.PermissionFor(DocumentTypes.Cheque).Should().BeNull();
        DraftDocumentTypes.PermissionFor(null).Should().BeNull();
        DraftDocumentTypes.PermissionFor("NOT_A_TYPE").Should().BeNull();
    }

    /// <summary>The permission on the controller's <c>Create</c> action.</summary>
    private static string CreatePermissionOf(Type controller)
    {
        var create = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Create" && m.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToList();

        create.Should().ContainSingle(
            $"{controller.Name} should have exactly one POST Create action for this test to read");

        var permission = create[0].GetCustomAttribute<RequirePermissionAttribute>()?.Policy;

        permission.Should().NotBeNull($"{controller.Name}.Create should be gated by a permission");

        return permission!;
    }
}

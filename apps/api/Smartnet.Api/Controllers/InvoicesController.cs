using Microsoft.AspNetCore.Mvc;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Invoices — the documents engine's first writer (Phase 5, slice 1).
/// </summary>
/// <remarks>
/// One controller for what the legacy app split across four (item vs service, create vs edit): a line
/// either references an item or is free-typed, and that is the whole difference. The browser holds the
/// draft and posts it whole — the server-session cart is gone (D4). The save is one transaction behind
/// <see cref="IInvoiceCreator"/>: number, header, lines, ledger, stock and snapshot, all or none.
/// </remarks>
[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceCreator _creator;
    private readonly ICompanyContext _company;

    public InvoicesController(IInvoiceCreator creator, ICompanyContext company)
    {
        _creator = creator;
        _company = company;
    }

    /// <remarks>
    /// Creating is not a reason-gated action (AUDIT.md §5 — "the record is the reason"), so no
    /// <c>X-Change-Reason</c> here; editing an issued invoice, in slice 5, is.
    /// </remarks>
    [HttpPost]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceCreatedResponse>> Create(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        // Deny by default extends to the company: a caller may only raise a document in a company their
        // token grants, never one they named in the body but cannot see.
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise an invoice in that company.");
        }

        var type = request.Type == "Cash" ? InvoiceType.Cash : InvoiceType.Credit;

        var created = await _creator.CreateAsync(
            new NewInvoice(
                request.CompanyId,
                request.CustomerId,
                type,
                request.Date,
                request.PurchaseOrderNo,
                request.ContactPerson,
                [.. request.Lines.Select(l => new NewInvoiceLine(
                    l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))]),
            cancellationToken).ConfigureAwait(false);

        return Ok(new InvoiceCreatedResponse(created.Id, created.Number, created.Total, created.Outstanding));
    }
}

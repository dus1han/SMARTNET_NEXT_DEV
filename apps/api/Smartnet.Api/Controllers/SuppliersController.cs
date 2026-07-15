using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Exporting;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Suppliers — the second master-data screen, and the measurement Phase 3 slice 3 is actually for.
/// </summary>
/// <remarks>
/// Customers proved the Users pattern clones; Suppliers proves the clone is <i>cheap</i>. This is the
/// same controller as Customers with the customer-only fields removed — no type, no credit limit, no
/// margin band, no associated-company default — and nothing added. If it had needed something the
/// customer screen did not, the abstraction would have been the finding. It did not.
/// </remarks>
[ApiController]
[Route("api/suppliers")]
[RequirePermission(Permissions.SupplierM)]
public sealed class SuppliersController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly ISupplierCodeAllocator _codes;
    private readonly IExcelExporter _excel;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public SuppliersController(
        SmartnetDbContext db,
        ISupplierCodeAllocator codes,
        IExcelExporter excel,
        IAuditWriter audit,
        TimeProvider time)
    {
        _db = db;
        _codes = codes;
        _excel = excel;
        _audit = audit;
        _time = time;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierSummary>>> List(CancellationToken cancellationToken)
    {
        var suppliers = await _db.Suppliers
            .Select(Summarise)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ordered by the number inside the code — "S-2" before "S-10", which no plain ORDER BY
        // expresses. The client sorts too, but the export reads this order, so it lives here as well.
        return Ok(suppliers.OrderBy(s => CodeOrder(s.Code)).ThenBy(s => s.Code, StringComparer.Ordinal).ToList());
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var suppliers = (await _db.Suppliers
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(s => CodeOrder(s.Code))
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .ToList();

        var workbook = _excel.Export<Supplier>(
            "Suppliers",
            [
                new("Code", s => s.Code),
                new("Name", s => s.Name),
                new("Contact", s => s.ContactPerson),
                new("Address", s => s.Address),
                new("Phone", s => s.Phone),
                new("Email", s => s.Email),
                new("VAT number", s => s.VatNumber),
            ],
            suppliers);

        await _audit.RecordAsync(
            AuditAction.Export,
            nameof(Supplier),
            "*",
            details: new { list = "suppliers", rows = suppliers.Count },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"suppliers-{_time.GetUtcNow():yyyy-MM-dd}.xlsx");
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SupplierSummary>> Get(long id, CancellationToken cancellationToken)
    {
        var supplier = await _db.Suppliers
            .Where(s => s.Id == id)
            .Select(Summarise)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return supplier is null ? NotFound() : Ok(supplier);
    }

    [HttpPost]
    public async Task<ActionResult<CreateSupplierResponse>> Create(
        SaveSupplierRequest request,
        CancellationToken cancellationToken)
    {
        // The code allocation and the supplier insert are one transaction: a rolled-back create must
        // not burn a code out of the sequence the legacy app is also drawing from.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var code = await _codes.NextAsync(cancellationToken).ConfigureAwait(false);

        var supplier = new Supplier { Code = code };
        Apply(supplier, request);

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new CreateSupplierResponse(supplier.Id, code));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id,
        SaveSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var supplier = await Find(id, cancellationToken).ConfigureAwait(false);

        if (supplier is null)
        {
            return NotFound();
        }

        Apply(supplier, request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Removes a supplier — soft, and only when it carries no purchase history.
    /// </summary>
    /// <remarks>
    /// The legacy app has <b>no delete for suppliers at all</b> (<c>SupplierController</c> has no such
    /// action), so this guard has no legacy counterpart to mirror — it exists for the same reason the
    /// customer one does. Purchase orders and supplier invoices reference the supplier by its
    /// <b>code</b> string, not by a foreign key: removing the supplier would not orphan a row EF can
    /// see, it would orphan the <i>name</i> on every document it appears on. So a supplier with history
    /// stays, and the reason is stated back to the user.
    /// </remarks>
    [HttpDelete("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var supplier = await Find(id, cancellationToken).ConfigureAwait(false);

        if (supplier is null)
        {
            return NotFound();
        }

        var documents = await DocumentCountFor(supplier.Code, cancellationToken).ConfigureAwait(false);

        if (documents > 0)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{supplier.Name} appears on {documents} document(s) and cannot be removed. "
                       + "Its history would lose the name it was issued under.");
        }

        // The interceptor turns this into a soft delete and audits it with the reason attached.
        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<Supplier?> Find(long id, CancellationToken cancellationToken) => _db.Suppliers
        .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <summary>
    /// The number inside a supplier code, so "S-2" orders before "S-10". A code with no digits
    /// (should not occur) sorts to the end rather than throwing.
    /// </summary>
    private static long CodeOrder(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());

        return long.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : long.MaxValue;
    }

    private static void Apply(Supplier supplier, SaveSupplierRequest request)
    {
        supplier.Name = request.Name;
        supplier.ContactPerson = request.ContactPerson;
        supplier.Address = request.Address;
        supplier.Phone = request.Phone;
        supplier.Email = request.Email;
        supplier.VatNumber = request.VatNumber;
    }

    /// <summary>
    /// How many legacy documents name this supplier code — purchase orders and supplier invoices.
    /// </summary>
    /// <remarks>
    /// Raw SQL against the legacy tables: <c>po_h</c> and <c>supplier_invoice</c> are not in this
    /// context (they belong to the legacy app until Phase 6 adopts them), and they reference the
    /// supplier by its code string — <c>po_h.supplier</c> and <c>supplier_invoice.supcode</c>.
    /// </remarks>
    private async Task<int> DocumentCountFor(string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return 0;
        }

        return await _db.Database
            .SqlQuery<int>($"""
                SELECT
                    (SELECT COUNT(*) FROM po_h             WHERE supplier = {code})
                  + (SELECT COUNT(*) FROM supplier_invoice WHERE supcode  = {code}) AS Value
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Supplier, SupplierSummary>> Summarise =
        s => new SupplierSummary(
            s.Id,
            s.Code ?? string.Empty,
            s.Name ?? string.Empty,
            s.ContactPerson,
            s.Address,
            s.Phone,
            s.Email,
            s.VatNumber);
}

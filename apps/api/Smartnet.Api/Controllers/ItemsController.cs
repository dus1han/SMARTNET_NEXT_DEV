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
/// Items — the catalogue, and the stock ledger behind it.
/// </summary>
/// <remarks>
/// Two permissions live here, deliberately not one: the catalogue is <c>item_m</c>, the stock is
/// <c>itemstock</c> — the legacy split, kept, so a storeman can adjust stock without being able to
/// reprice the catalogue. There is no class-level requirement; every action states its own, which is
/// what lets the two coexist (two class+action policies would AND together and demand both).
/// <para>
/// The catalogue half is the third clone of the Users pattern. The stock half is the one new thing in
/// Phase 3: a balance is never stored, only derived from the movement ledger (B3).
/// </para>
/// </remarks>
[ApiController]
[Route("api/items")]
public sealed class ItemsController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly IItemCodeAllocator _codes;
    private readonly IExcelExporter _excel;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public ItemsController(
        SmartnetDbContext db,
        IItemCodeAllocator codes,
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

    // --- Catalogue (item_m) ------------------------------------------------------------------

    [HttpGet]
    [RequirePermission(Permissions.ItemM)]
    public async Task<ActionResult<IReadOnlyList<ItemSummary>>> List(CancellationToken cancellationToken)
    {
        var items = await _db.Items.ToListAsync(cancellationToken).ConfigureAwait(false);
        var balances = await BalancesByItem(cancellationToken).ConfigureAwait(false);

        return Ok(items
            .Select(i => Summarise(i, balances.GetValueOrDefault(i.Id)))
            .OrderBy(s => CodeOrder(s.Code))
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .ToList());
    }

    [HttpGet("export")]
    [RequirePermission(Permissions.ItemM)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var items = await _db.Items.ToListAsync(cancellationToken).ConfigureAwait(false);
        var balances = await BalancesByItem(cancellationToken).ConfigureAwait(false);

        var rows = items
            .Select(i => Summarise(i, balances.GetValueOrDefault(i.Id)))
            .OrderBy(s => CodeOrder(s.Code))
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .ToList();

        var workbook = _excel.Export<ItemSummary>(
            "Items",
            [
                new("Code", i => i.Code),
                new("Name", i => i.Name),
                new("Selling price", i => i.SellingPrice, ExcelFormat.Money),
                new("Cost", i => i.Cost, ExcelFormat.Money),
                new("Unit", i => i.Unit),
                new("Reorder level", i => i.ReorderLevel),
                new("In stock", i => i.StockBalance),
            ],
            rows);

        await _audit.RecordAsync(
            AuditAction.Export,
            nameof(Item),
            "*",
            details: new { list = "items", rows = rows.Count },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"items-{_time.GetUtcNow():yyyy-MM-dd}.xlsx");
    }

    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.ItemM)]
    public async Task<ActionResult<ItemSummary>> Get(long id, CancellationToken cancellationToken)
    {
        var item = await Find(id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return NotFound();
        }

        var balance = await BalanceFor(id, cancellationToken).ConfigureAwait(false);
        return Ok(Summarise(item, balance));
    }

    [HttpPost]
    [RequirePermission(Permissions.ItemM)]
    public async Task<ActionResult<CreateItemResponse>> Create(
        SaveItemRequest request,
        CancellationToken cancellationToken)
    {
        // One transaction: a rolled-back create must not burn a code out of the sequence the legacy
        // app is also drawing from. This endpoint is also called from inside the invoice screen, so
        // it must be self-contained and cheap.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var code = await _codes.NextAsync(cancellationToken).ConfigureAwait(false);

        var item = new Item { Code = code };
        Apply(item, request);

        _db.Items.Add(item);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new CreateItemResponse(item.Id, code));
    }

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.ItemM)]
    public async Task<IActionResult> Update(
        long id,
        SaveItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await Find(id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return NotFound();
        }

        Apply(item, request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Removes an item — soft, and only when it has no stock ledger to strand.
    /// </summary>
    /// <remarks>
    /// An item with movements stays: its ledger references it by id, and a removed item would leave a
    /// stock history pointing at a row the list no longer shows. Corrections are movements, not
    /// deletions — so if the intent is "we hold none of this any more", that is a stock adjustment to
    /// zero, not a delete.
    /// </remarks>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.ItemM)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var item = await Find(id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return NotFound();
        }

        var movements = await _db.StockMovements
            .CountAsync(m => m.ItemId == id, cancellationToken)
            .ConfigureAwait(false);

        if (movements > 0)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{item.Name} has {movements} stock movement(s) and cannot be removed. "
                       + "Adjust its stock to zero instead — its history stays that way.");
        }

        _db.Items.Remove(item);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- Stock (itemstock) -------------------------------------------------------------------

    /// <summary>The item's balance, the ledger that produced it, and the legacy receipt batches.</summary>
    [HttpGet("{id:long}/stock")]
    [RequirePermission(Permissions.ItemStock)]
    public async Task<ActionResult<ItemStockResponse>> Stock(long id, CancellationToken cancellationToken)
    {
        var item = await Find(id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return NotFound();
        }

        // Ordered oldest-first so the running balance accumulates in the order the movements happened;
        // the client reverses it for display if it wants newest-first.
        var movements = await _db.StockMovements
            .Where(m => m.ItemId == id)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        decimal running = 0;
        var ledger = movements
            .Select(m =>
            {
                running += m.Quantity;
                return new StockMovementDto(
                    m.Id, m.Type.ToString(), m.Quantity, running, m.Reason, m.OccurredAt, m.CreatedBy, m.CreatedAt);
            })
            .ToList();

        var batches = await _db.StockBatches
            .Where(b => b.ItemCode == item.Code)
            .OrderByDescending(b => b.InDate)
            .Select(b => new StockBatchDto(b.Id, b.Quantity, b.Balance, b.UnitCost, b.InDate, b.EnteredBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(new ItemStockResponse(
            item.Id, item.Code ?? string.Empty, item.Name ?? string.Empty, item.ReorderLevel, running, ledger, batches));
    }

    /// <summary>
    /// Records a stock adjustment — the only way a human moves a quantity, and always a new ledger
    /// entry, never an edit of a balance.
    /// </summary>
    [HttpPost("{id:long}/stock/adjustments")]
    [RequirePermission(Permissions.ItemStock)]
    public async Task<IActionResult> Adjust(
        long id,
        CreateStockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await Find(id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return NotFound();
        }

        var movement = new StockMovement
        {
            ItemId = id,
            Type = StockMovementType.Adjustment,
            Quantity = request.Quantity,
            Reason = request.Reason,
            // Back-dateable to the count, but never into the future — a movement cannot happen later
            // than the moment it is recorded.
            OccurredAt = request.OccurredAt is { } when && when <= _time.GetUtcNow().UtcDateTime
                ? when
                : _time.GetUtcNow().UtcDateTime,
        };

        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<Item?> Find(long id, CancellationToken cancellationToken) => _db.Items
        .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    /// <summary>Every item's balance in one grouped query, for the list and the export.</summary>
    private async Task<Dictionary<long, decimal>> BalancesByItem(CancellationToken cancellationToken) =>
        await _db.StockMovements
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, Balance = g.Sum(m => m.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Balance, cancellationToken)
            .ConfigureAwait(false);

    private async Task<decimal> BalanceFor(long id, CancellationToken cancellationToken) =>
        await _db.StockMovements
            .Where(m => m.ItemId == id)
            .SumAsync(m => m.Quantity, cancellationToken)
            .ConfigureAwait(false);

    private static ItemSummary Summarise(Item i, decimal balance) => new(
        i.Id,
        i.Code ?? string.Empty,
        i.Name ?? string.Empty,
        i.SellingPrice,
        i.Cost,
        i.ReorderLevel,
        i.Unit,
        balance,
        i.ReorderLevel.HasValue && balance <= i.ReorderLevel.Value);

    private static void Apply(Item item, SaveItemRequest request)
    {
        item.Name = request.Name;
        item.SellingPrice = request.SellingPrice;
        item.Cost = request.Cost;
        item.ReorderLevel = request.ReorderLevel;
        item.Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim();
    }

    /// <summary>The number inside an item code, so "I-2" orders before "I-10".</summary>
    private static long CodeOrder(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());

        return long.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : long.MaxValue;
    }
}

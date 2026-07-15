using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.MasterData;

/// <summary>
/// The slice-4 exit, and the answer to ISSUES B3: a stock balance is never stored, only derived from
/// an immutable movement ledger.
/// </summary>
/// <remarks>
/// The legacy app keeps a mutable <c>item_stock.balance</c> and edits it in place — the same shape as
/// <c>invoice_h.balance</c>, which is how Rs. 1.55M of duplicate payments happened with nothing left
/// to reconstruct them from. These tests assert the new shape: the balance is Σ of the movements, and
/// the legacy column is never written by anything here.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class StockLedgerTests
{
    private static readonly DateTime When = new(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly AuditFixture _fixture;

    public StockLedgerTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_items_balance_is_the_sum_of_its_movements()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var item = new Item { Code = "I-8001", Name = "166A Toner" };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // A receipt-style top-up, a write-off, and a small upward correction: 100 − 30 + 5 = 75.
        foreach (var quantity in new[] { 100m, -30m, 5m })
        {
            db.StockMovements.Add(new StockMovement
            {
                ItemId = item.Id,
                Type = StockMovementType.Adjustment,
                Quantity = quantity,
                Reason = "count",
                OccurredAt = When,
            });
        }

        await db.SaveChangesAsync();

        var balance = await db.StockMovements
            .Where(m => m.ItemId == item.Id)
            .SumAsync(m => m.Quantity);

        balance.Should().Be(75m);

        // Three movements, all still there — a correction added a row, it did not rewrite one.
        (await db.StockMovements.CountAsync(m => m.ItemId == item.Id)).Should().Be(3);
    }

    [Fact]
    public async Task The_derived_balance_is_independent_of_the_legacy_item_stock_column()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var item = new Item { Code = "I-8002", Name = "HDMI Cable" };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // A legacy batch, written the legacy way, carrying its own mutable balance of 50. This is the
        // column the new app must never touch.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO item_stock (item_code, quantity, balance) VALUES ('I-8002', '50', '50')");

        db.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            Type = StockMovementType.Adjustment,
            Quantity = 10m,
            Reason = "opening count",
            OccurredAt = When,
        });
        await db.SaveChangesAsync();

        // The new balance comes only from the ledger: 10, not the legacy 50, and not 60. The two are
        // deliberately unrelated — the new app derives, and never reads or writes the legacy cache.
        (await db.StockMovements.Where(m => m.ItemId == item.Id).SumAsync(m => m.Quantity))
            .Should().Be(10m);

        // And the legacy column is exactly as the legacy app left it. Nothing here wrote a balance.
        (await db.StockBatches.Where(b => b.ItemCode == "I-8002").Select(b => b.Balance).SingleAsync())
            .Should().Be(50m);
    }

    [Fact]
    public async Task A_movement_records_who_entered_it_without_any_audit_code()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 7 });

        var item = new Item { Code = "I-8003", Name = "Mouse" };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var movement = new StockMovement
        {
            ItemId = item.Id,
            Type = StockMovementType.Adjustment,
            Quantity = -2m,
            Reason = "broken in transit",
            OccurredAt = When,
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync();

        // The ledger's attribution is the audit spine's, for free: who and when, as an id not a name.
        movement.CreatedBy.Should().Be(7);
        movement.CreatedAt.Should().NotBe(default);
        movement.RowVersion.Should().Be(1);
    }

    [Fact]
    public async Task A_legacy_item_insert_still_succeeds_and_the_new_columns_default_null()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        // The legacy app names only the two columns it knows. Slice 4 added five more, all nullable,
        // so this must still succeed — additive only (DEVELOPMENT.md §8).
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO item_m (itemcode, itemname) VALUES ('I-8004', 'Legacy part')");

        var item = await db.Items.SingleAsync(i => i.Code == "I-8004");

        item.Id.Should().BeGreaterThan(0);
        item.SellingPrice.Should().BeNull();
        item.Cost.Should().BeNull();
        item.ReorderLevel.Should().BeNull();
        item.Unit.Should().BeNull();
    }
}

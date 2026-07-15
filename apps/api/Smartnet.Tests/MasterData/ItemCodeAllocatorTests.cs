using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.MasterData;

/// <summary>
/// The item code comes from <c>item_seq</c>, the sequence the legacy app also uses — the same
/// coexistence guarantee made for customers and suppliers, and it matters most here because the item
/// "add" is reachable from inside the invoice screen, where two people adding a part at once is not a
/// rare event.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class ItemCodeAllocatorTests
{
    private readonly AuditFixture _fixture;

    public ItemCodeAllocatorTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Allocates_sequential_codes_from_item_seq()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new ItemCodeAllocator(db, TimeProvider.System);

        var first = await allocator.NextAsync();
        var second = await allocator.NextAsync();

        first.Should().MatchRegex(@"^I-\d+$");

        var firstNumber = int.Parse(first[2..], System.Globalization.CultureInfo.InvariantCulture);
        var secondNumber = int.Parse(second[2..], System.Globalization.CultureInfo.InvariantCulture);

        secondNumber.Should().Be(firstNumber + 1);
    }

    [Fact]
    public async Task Shares_the_sequence_with_a_legacy_style_allocation()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new ItemCodeAllocator(db, TimeProvider.System);

        var mine = await allocator.NextAsync();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO item_seq (dt) VALUES ('2026-07-15')");
        var next = await allocator.NextAsync();

        var mineNumber = long.Parse(mine[2..], System.Globalization.CultureInfo.InvariantCulture);
        var nextNumber = long.Parse(next[2..], System.Globalization.CultureInfo.InvariantCulture);

        nextNumber.Should().Be(mineNumber + 2);
    }
}

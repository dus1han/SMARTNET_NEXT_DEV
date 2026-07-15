using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.MasterData;

/// <summary>
/// The supplier code comes from the sequence the legacy app also uses — the same coexistence
/// guarantee <see cref="CustomerCodeAllocatorTests"/> makes for customers, and it matters here for the
/// identical reason: two apps allocating "S-87" from separate counters would collide on the unique
/// index this phase added. They share <c>sup_seq</c>.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class SupplierCodeAllocatorTests
{
    private readonly AuditFixture _fixture;

    public SupplierCodeAllocatorTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Allocates_sequential_codes_from_sup_seq()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new SupplierCodeAllocator(db, TimeProvider.System);

        var first = await allocator.NextAsync();
        var second = await allocator.NextAsync();

        first.Should().MatchRegex(@"^S-\d+$");

        var firstNumber = int.Parse(first[2..], System.Globalization.CultureInfo.InvariantCulture);
        var secondNumber = int.Parse(second[2..], System.Globalization.CultureInfo.InvariantCulture);

        secondNumber.Should().Be(firstNumber + 1);
    }

    [Fact]
    public async Task Shares_the_sequence_with_a_legacy_style_allocation()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new SupplierCodeAllocator(db, TimeProvider.System);

        var mine = await allocator.NextAsync();

        // A legacy allocation happens in between, on the same table — the old app inserting its own
        // supplier (SupplierController.savesupplier).
        await db.Database.ExecuteSqlRawAsync("INSERT INTO sup_seq (dt) VALUES ('2026-07-15')");

        var next = await allocator.NextAsync();

        var mineNumber = long.Parse(mine[2..], System.Globalization.CultureInfo.InvariantCulture);
        var nextNumber = long.Parse(next[2..], System.Globalization.CultureInfo.InvariantCulture);

        // The gap is exactly the legacy row: mine, then theirs, then the next of mine.
        nextNumber.Should().Be(mineNumber + 2);
    }
}

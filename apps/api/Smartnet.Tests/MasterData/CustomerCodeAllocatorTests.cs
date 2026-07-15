using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.MasterData;

/// <summary>
/// The customer code comes from the sequence the legacy app also uses.
/// </summary>
/// <remarks>
/// This is the coexistence guarantee, tested. If the new app allocated codes from its own counter
/// while the legacy app kept drawing from <c>cus_seq</c>, the two would hand "C-232" to two different
/// customers, and the unique index this phase added would reject the second save. The only way they
/// stay out of each other's way is to share the one sequence.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class CustomerCodeAllocatorTests
{
    private readonly AuditFixture _fixture;

    public CustomerCodeAllocatorTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Allocates_sequential_codes_from_cus_seq()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new CustomerCodeAllocator(db, TimeProvider.System);

        var first = await allocator.NextAsync();
        var second = await allocator.NextAsync();

        first.Should().MatchRegex(@"^C-\d+$");

        // Consecutive, so the codes a human reads run in the order the customers were created.
        var firstNumber = int.Parse(first[2..], System.Globalization.CultureInfo.InvariantCulture);
        var secondNumber = int.Parse(second[2..], System.Globalization.CultureInfo.InvariantCulture);

        secondNumber.Should().Be(firstNumber + 1);
    }

    [Fact]
    public async Task Shares_the_sequence_with_a_legacy_style_allocation()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new CustomerCodeAllocator(db, TimeProvider.System);

        var mine = await allocator.NextAsync();

        // A legacy allocation happens in between, on the same table — the old app inserting its own
        // customer.
        await db.Database.ExecuteSqlRawAsync("INSERT INTO cus_seq (dt) VALUES ('2026-07-15')");

        var next = await allocator.NextAsync();

        var mineNumber = long.Parse(mine[2..], System.Globalization.CultureInfo.InvariantCulture);
        var nextNumber = long.Parse(next[2..], System.Globalization.CultureInfo.InvariantCulture);

        // The gap is exactly the legacy row: mine, then theirs, then the next of mine. Two apps, one
        // sequence, and the new app's second code has skipped the number the old app took.
        nextNumber.Should().Be(mineNumber + 2);
    }
}

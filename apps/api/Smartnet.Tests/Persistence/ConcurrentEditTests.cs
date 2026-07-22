using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Persistence;

/// <summary>
/// Two people editing one master-data record: the second save is refused, not silently applied.
/// </summary>
/// <remarks>
/// <para><b>The bug these close.</b> <c>row_version</c> has been a concurrency token on every audited
/// entity since Phase 1, but the master-data controllers were load-modify-save in a single request — so
/// the "original" value EF compared was the one it had read a millisecond earlier, and it always
/// matched. Two people editing one customer meant the second write won and the first person's change
/// was gone with no error and no trace: exactly the legacy behaviour <c>AuditColumns</c> says the token
/// exists to prevent, and it names the customer as its example.</para>
///
/// <para>The fix is that the client sends the version it loaded and the controller checks it. What is
/// pinned here is the layer underneath that: that the token really does reach the UPDATE, so a check
/// against a stale version cannot be defeated by two requests racing past the controller's own
/// comparison together.</para>
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class ConcurrentEditTests
{
    private readonly AuditFixture _fixture;

    public ConcurrentEditTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_second_of_two_concurrent_customer_edits_is_refused()
    {
        var change = new FakeChangeContext { UserId = 7, CompanyId = 1 };

        long id;
        await using (var seed = _fixture.CreateContext(change))
        {
            var customer = new Customer { Code = "C-CONC-1", Name = "Acme Trading" };
            seed.Customers.Add(customer);
            await seed.SaveChangesAsync();
            id = customer.Id;
        }

        // Two requests, each having loaded the customer at the same version.
        await using var first = _fixture.CreateContext(change);
        await using var second = _fixture.CreateContext(change);

        var mine = await first.Customers.SingleAsync(c => c.Id == id);
        var theirs = await second.Customers.SingleAsync(c => c.Id == id);

        mine.Name = "Acme Trading Ltd";
        await first.SaveChangesAsync();

        theirs.Phone = "0112 555 000";

        await FluentActions
            .Awaiting(() => second.SaveChangesAsync())
            .Should()
            .ThrowAsync<DbUpdateConcurrencyException>(
                because:
                    "without this the second write lands, the rename is gone, and nobody is told. "
                    + "The controller's own version check closes the window it can see; this is what "
                    + "closes the one where both requests pass that check together.");

        await using var check = _fixture.CreateContext(change);
        var saved = await check.Customers.SingleAsync(c => c.Id == id);

        // The first writer's change survived intact, and the second's was not partially applied.
        saved.Name.Should().Be("Acme Trading Ltd");
        saved.Phone.Should().BeNull();
    }

    [Fact]
    public async Task An_edit_carrying_the_current_version_still_saves()
    {
        // The guard has to refuse a stale edit without refusing an ordinary one — a check that blocked
        // everything would "pass" the test above and make the screen unusable.
        var change = new FakeChangeContext { UserId = 7, CompanyId = 1 };

        long id;
        int version;

        await using (var seed = _fixture.CreateContext(change))
        {
            var item = new Item { Code = "I-CONC-1", Name = "Router", SellingPrice = 100m };
            seed.Items.Add(item);
            await seed.SaveChangesAsync();
            id = item.Id;
            version = item.RowVersion;
        }

        await using (var edit = _fixture.CreateContext(change))
        {
            var item = await edit.Items.SingleAsync(i => i.Id == id);
            item.RowVersion.Should().Be(version, "the reader sees the version the writer wrote");

            item.SellingPrice = 125m;
            await edit.SaveChangesAsync();
        }

        await using (var check = _fixture.CreateContext(change))
        {
            var item = await check.Items.SingleAsync(i => i.Id == id);
            item.SellingPrice.Should().Be(125m);
            item.RowVersion.Should().Be(version + 1, "a save moves the version on, so the next stale edit is caught");
        }
    }
}

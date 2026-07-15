using FluentAssertions;
using Smartnet.Domain.Auditing;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Tests.Auditing;

/// <summary>
/// The read side the admin audit viewer is built on: browsing the whole log with filters, and the
/// facets that populate those filters.
/// </summary>
/// <remarks>
/// The container is shared across the collection, so every test here scopes to its own company id and
/// asserts against rows it seeded — never an unqualified "total in the table", which another test's
/// rows would make non-deterministic. Company-less rows are the one thing visible across any scope
/// (that is the property under test in <see cref="Company_less_rows_are_visible_under_any_scope"/>),
/// so no other test seeds one.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class AuditLogBrowseTests
{
    private readonly AuditFixture _fixture;

    private static readonly FakeChangeContext Seeder = new() { UserId = 1, CompanyId = 1 };

    public AuditLogBrowseTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Browse_narrows_by_action_user_entity_and_date()
    {
        const long company = 8801;

        await Seed(
            Row("A_Login1", AuditAction.Login, user: 1, company, Day(10)),
            Row("A_Login2", AuditAction.Login, user: 2, company, Day(11)),
            Row("A_Cust", AuditAction.Update, user: 1, company, Day(12)),
            Row("A_Rept", AuditAction.Export, user: 1, company, Day(13)),
            Row("A_Item", AuditAction.Create, user: 2, company, Day(14)));

        var scope = HistoryScope.Of([company]);

        // By action — bounded to this test's window so the (deliberately company-less, and thus
        // always-visible) login seeded by the null-visibility test cannot drift in. Within the window
        // the Update/Export/Create rows are excluded by action, the two logins remain.
        var logins = await Browse(scope, new(Day(10), Day(15), null, AuditAction.Login, null, 500));
        logins.Events.Select(e => e.EntityType).Should().BeEquivalentTo("A_Login1", "A_Login2");

        // By user — and, filtered to one user in this test's own company, the result is exactly this
        // test's rows, so order and total are deterministic despite the shared table.
        var byUser = await Browse(scope, new(null, null, UserId: 1, null, null, 500));
        byUser.Events.Select(e => e.EntityType).Should().Equal("A_Rept", "A_Cust", "A_Login1");

        // By entity type.
        var byType = await Browse(scope, new(null, null, null, null, EntityType: "A_Rept", 500));
        byType.Events.Should().ContainSingle().Which.EntityType.Should().Be("A_Rept");

        // By date — half-open, so "to the 13th" is < the 14th and A_Item (the 14th) is out while the
        // A_Rept row late on the 13th is in.
        var window = await Browse(scope, new(Day(12), Day(14), null, null, null, 500));
        window.Events.Select(e => e.EntityType).Should().BeEquivalentTo("A_Cust", "A_Rept");

        // The page is capped but the total tells the truth about the rest.
        var capped = await Browse(scope, new(null, null, UserId: 1, null, null, Limit: 2));
        capped.Events.Should().HaveCount(2);
        capped.Total.Should().Be(3);
    }

    [Fact]
    public async Task Facets_list_only_the_visible_entity_types_and_actors()
    {
        await Seed(
            Row("B_Cust", AuditAction.Update, user: 1, company: 8811, Day(12)),
            Row("B_Rept", AuditAction.Export, user: 3, company: 8811, Day(13)),
            // A different company the scope will not include — its type and actor must not leak in.
            Row("B_Hidden", AuditAction.Create, user: 4, company: 8812, Day(14)));

        var facets = await Facets(HistoryScope.Of([8811]));

        facets.EntityTypes.Should().BeEquivalentTo("B_Cust", "B_Rept");
        facets.EntityTypes.Should().NotContain("B_Hidden");

        facets.Actors.Select(a => a.Id).Should().BeEquivalentTo(new long[] { 1, 3 });
        facets.Actors.Select(a => a.Id).Should().NotContain(4);

        // No user_m row backs these ids, so the actor is labelled by id rather than vanishing — the
        // log stores the id precisely so a removed user stays filterable.
        facets.Actors.Should().Contain(a => a.Id == 1 && a.Name == "User 1");
    }

    [Fact]
    public async Task Company_less_rows_are_visible_under_any_scope()
    {
        const long tag = 987654; // A user id used nowhere else, so these rows are addressable.

        await Seed(
            Row("C_Login", AuditAction.Login, user: tag, company: null, Day(9)),
            Row("C_Scoped", AuditAction.Update, user: tag, company: 8831, Day(9)));

        // A scope that includes neither company. The company-less login is still visible — it happened
        // before authentication, outside any set of books — while the company-8831 row is hidden.
        var page = await Browse(HistoryScope.Of([123]), new(null, null, UserId: tag, null, null, 500));

        page.Events.Should().ContainSingle().Which.EntityType.Should().Be("C_Login");
    }

    // --- Harness ---------------------------------------------------------------------------------

    private static DateTime Day(int day) => new(2026, 7, day, 9, 0, 0, DateTimeKind.Utc);

    private static AuditLogEntry Row(
        string entityType,
        AuditAction action,
        long? user,
        long? company,
        DateTime at) =>
        new()
        {
            EntityType = entityType,
            EntityId = "1",
            Action = action,
            ChangedBy = user,
            CompanyId = company,
            ChangedAt = at,
        };

    private async Task Seed(params AuditLogEntry[] rows)
    {
        // A context with the interceptor attached, but audit_log is not itself auditable, so these
        // inserts write exactly the rows given and nothing else.
        await using var db = _fixture.CreateContext(Seeder);
        db.AuditLog.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task<RecordHistory> Browse(HistoryScope scope, AuditLogFilter filter)
    {
        await using var db = _fixture.CreateContext(Seeder);
        return await new AuditHistoryReader(db).BrowseAsync(filter, scope);
    }

    private async Task<AuditLogFacets> Facets(HistoryScope scope)
    {
        await using var db = _fixture.CreateContext(Seeder);
        return await new AuditHistoryReader(db).FacetsAsync(scope);
    }
}

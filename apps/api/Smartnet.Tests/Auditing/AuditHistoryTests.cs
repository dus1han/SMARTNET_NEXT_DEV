using System.Globalization;
using FluentAssertions;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;

namespace Smartnet.Tests.Auditing;

/// <summary>
/// The read side of the trail — what the History tab is made of.
/// </summary>
/// <remarks>
/// The promises tested here are the ones a history screen is worthless without: that it shows the
/// changes in the order they happened, attributed to a person by name; that it says how much it is
/// <i>not</i> showing; and that a Company_Admin of one company cannot read another company's
/// history through it. The last is the one worth having a test for, because it is the one whose
/// failure is silent.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class AuditHistoryTests
{
    private readonly AuditFixture _fixture;

    private const long Acme = 7;
    private const long Rival = 9;

    public AuditHistoryTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_records_history_is_its_changes_newest_first_attributed_by_name()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var actor = await CreateUser(db, "chanaka", "Chanaka Perera");

        await using var acting = _fixture.CreateContext(Editing(actor, Acme, "Customer sent the corrected part number."));

        var widget = new Widget { Name = "Switch, 8-port" };
        acting.Widgets.Add(widget);
        await acting.SaveChangesAsync();

        widget.Name = "Switch, 16-port";
        await acting.SaveChangesAsync();

        var history = await Read(nameof(Widget), Key(widget.Id), Scope(Acme));

        history.Total.Should().Be(2);
        history.Events.Select(e => e.Action).Should().Equal(
            [AuditAction.Update, AuditAction.Create],
            "the newest change is the one you came to read");

        var latest = history.Events[0];

        // The log stores the id; the name is resolved at read time. Rename the user tomorrow and
        // this history says the new name — rather than the legacy app's frozen "Saboor A. : …".
        latest.ChangedBy.Should().Be(actor);
        latest.ChangedByName.Should().Be("Chanaka Perera");
        latest.Reason.Should().Be("Customer sent the corrected part number.");
        latest.Changes.Should().Contain("Switch, 16-port");
    }

    [Fact]
    public async Task The_total_counts_every_event_not_just_the_page_returned()
    {
        await using var db = _fixture.CreateContext(Editing(null, Acme));

        var widget = new Widget { Name = "Rev 1" };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        foreach (var revision in new[] { "Rev 2", "Rev 3", "Rev 4" })
        {
            widget.Name = revision;
            await db.SaveChangesAsync();
        }

        var page = await Read(nameof(Widget), Key(widget.Id), Scope(Acme), limit: 2);

        page.Events.Should().HaveCount(2);

        // "Showing 2 of 4". A list that silently stops at its limit reads as a complete history,
        // which on the one screen whose job is to be believed is worse than showing nothing.
        page.Total.Should().Be(4);
    }

    /// <summary>
    /// The scoping mechanism, tested with a scope narrower than anyone actually has.
    /// </summary>
    /// <remarks>
    /// <b>No user in this business is scoped to one company today, and none is meant to be</b> —
    /// Smart Net and Smart Technologies are two trading entities worked by the same staff, and every
    /// user's token carries both (see <c>ICompanyAccessService</c>). So this test does not describe
    /// a boundary anyone is standing behind. It proves the read path <i>honours</i> the scope it is
    /// given, which is what makes that boundary a configuration change rather than a rewrite on the
    /// day an entity is acquired whose books the existing staff should not see.
    /// </remarks>
    [Fact]
    public async Task History_is_readable_only_within_the_scope_the_caller_was_given()
    {
        await using var mine = _fixture.CreateContext(Editing(null, Acme));
        var ours = new Widget { Name = "Our margin" };
        mine.Widgets.Add(ours);
        await mine.SaveChangesAsync();

        await using var theirs = _fixture.CreateContext(Editing(null, Rival));
        var yours = new Widget { Name = "Their margin" };
        theirs.Widgets.Add(yours);
        await theirs.SaveChangesAsync();

        // A caller whose token carries only Acme. The permission is not what scopes them — audit.view
        // is all-or-nothing — the company set in their token is.
        var visible = await Read(nameof(Widget), Key(yours.Id), Scope(Acme));

        visible.Events.Should().BeEmpty("the read path honours the scope it was given");
        visible.Total.Should().Be(0);

        (await Read(nameof(Widget), Key(ours.Id), Scope(Acme))).Events.Should().HaveCount(1);

        // And a caller carrying both — which today is everybody — sees both. No bypass flag: one
        // predicate serves every caller, and the code path that is never exercised is the one that
        // leaks.
        (await Read(nameof(Widget), Key(yours.Id), Scope(Acme, Rival))).Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_event_belonging_to_no_company_is_visible_to_anyone_who_may_read_history()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { CompanyId = null });

        var actor = await CreateUser(db, "nobody", "Signed Out");

        var audit = new AuditWriter(db, new FakeChangeContext { CompanyId = null }, TimeProvider.System);

        // A login is written before the user is authenticated, so there is no active company to
        // attribute it to. Scoping those rows away would empty a user's history of exactly the
        // events it exists to show.
        await audit.RecordAsync(AuditAction.Login, nameof(User), Key(actor));

        var history = await Read(nameof(User), Key(actor), Scope(Acme));

        history.Events.Should().Contain(e => e.Action == AuditAction.Login);
    }

    [Fact]
    public void A_type_the_log_cannot_contain_is_rejected_rather_than_answered_with_nothing()
    {
        using var db = _fixture.CreateContext(new FakeChangeContext());
        var reader = new AuditHistoryReader(db);

        reader.IsAuditableEntity(nameof(Widget)).Should().BeTrue();
        reader.IsAuditableEntity(nameof(User)).Should().BeTrue();

        // "Never changed" and "no such thing" both return an empty list. The endpoint 404s on the
        // second so that the screen does not present a typo as a clean bill of health.
        reader.IsAuditableEntity("Xyzzy").Should().BeFalse();
        reader.IsAuditableEntity("widget").Should().BeFalse("the entity type is the CLR name, exactly");
    }

    [Fact]
    public async Task A_documents_versions_list_carries_no_snapshots_and_a_single_version_does()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var actor = await CreateUser(db, "priya", "Priya N.");

        db.DocumentVersions.AddRange(
            Version(1, "{\"total\":100}", actor),
            Version(2, "{\"total\":250}", actor));

        await db.SaveChangesAsync();

        var reader = new AuditHistoryReader(db);

        var versions = await reader.VersionsAsync(DocumentTypes.Invoice, 4242, Scope(Acme));

        versions.Select(v => v.VersionNo).Should()
            .Equal([2, 1], "the current version is the one you land on");
        versions.Should().OnlyContain(v => v.Snapshot == null, "the list draws dates, not documents");
        versions[0].ChangedByName.Should().Be("Priya N.");

        var version1 = await reader.VersionAsync(DocumentTypes.Invoice, 4242, 1, Scope(Acme));

        // Version 1 is the document as first created — the one version you cannot recover if it is
        // only written on edit. Reprinting it must reproduce that document, so it carries its own
        // resolved totals rather than today's.
        version1!.Snapshot.Should().Be("{\"total\":100}");

        (await reader.VersionAsync(DocumentTypes.Invoice, 4242, 1, Scope(Rival)))
            .Should().BeNull("a version in a company you cannot see does not exist to you");
    }

    // --- helpers -----------------------------------------------------------------------------

    private async Task<RecordHistory> Read(
        string entityType,
        string entityId,
        HistoryScope scope,
        int limit = HistoryLimits.Default)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        return await new AuditHistoryReader(db).ForRecordAsync(entityType, entityId, scope, limit);
    }

    private static DocumentVersion Version(int versionNo, string snapshot, long changedBy) => new()
    {
        CompanyId = Acme,
        DocType = DocumentTypes.Invoice,
        DocId = 4242,
        VersionNo = versionNo,
        Snapshot = snapshot,
        ChangedBy = changedBy,
        ChangedAt = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc),
        Reason = "Customer added two lines after approval.",
    };

    private static async Task<long> CreateUser(TestDbContext db, string username, string name)
    {
        var user = new User { Username = username, Name = name, Ustat = "Active" };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private static FakeChangeContext Editing(long? userId, long companyId, string? reason = null) =>
        new() { UserId = userId, CompanyId = companyId, Reason = reason };

    private static HistoryScope Scope(params long[] companies) => HistoryScope.Of(companies);

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
}

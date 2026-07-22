using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Autosaved drafts of the four create screens — the persistence claims the feature rests on.
/// </summary>
/// <remarks>
/// Against the real database rather than an in-memory provider, because both claims here are things
/// only a real one can settle: whether the audit interceptor writes rows for this entity, and whether
/// the <c>row_version</c> condition actually reaches the UPDATE statement.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class DocumentDraftTests
{
    private readonly AuditFixture _fixture;

    public DocumentDraftTests(AuditFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The one that matters: a draft is not audited, so autosave cannot bury the audit trail.
    /// </summary>
    /// <remarks>
    /// <para>Autosave writes every couple of seconds while somebody types. If <see cref="DocumentDraft"/>
    /// were <c>IAuditable</c>, a single forty-line invoice would leave hundreds of <c>audit_log</c> rows,
    /// each holding a before-and-after diff of a JSON blob — and AUDIT.md's entire claim is that the log
    /// is worth reading. What deserves auditing is the document that gets raised, and that already is.</para>
    ///
    /// <para>This would be an easy thing to undo by accident: adding <c>: IAuditable</c> to the entity
    /// looks like tidying up, since every other table has those columns. It fails here instead.</para>
    /// </remarks>
    [Fact]
    public async Task Autosaving_a_draft_writes_no_audit_rows()
    {
        var change = new FakeChangeContext { UserId = 7, CompanyId = 1 };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            var before = await db.AuditLog.CountAsync();

            var draft = Draft(companyId: 1, payload: """{"v":1,"state":{"lines":[]}}""");
            db.DocumentDrafts.Add(draft);
            await db.SaveChangesAsync();
            id = draft.Id;

            // Thirty saves — a minute of typing, and nowhere near the worst case.
            for (var i = 0; i < 30; i++)
            {
                draft.Payload = $$$"""{"v":1,"state":{"lines":[{{{i}}}]}}""";
                draft.RowVersion++;
                await db.SaveChangesAsync();
            }

            (await db.AuditLog.CountAsync()).Should().Be(
                before,
                because:
                    "a draft is a scratchpad, not a business record. If it were IAuditable, this loop "
                    + "alone would add 31 rows to audit_log — each a diff of a JSON blob nobody will "
                    + "ever read — and the log would stop being worth opening.");
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // Still saved, though. The point is that it is not *audited*, not that it does not persist.
            var draft = await db.DocumentDrafts.SingleAsync(d => d.Id == id);
            draft.Payload.Should().Be("""{"v":1,"state":{"lines":[29]}}""");
            draft.RowVersion.Should().Be(31);
        }
    }

    /// <summary>
    /// Two people in one shared draft: the second save is refused rather than silently winning.
    /// </summary>
    /// <remarks>
    /// Drafts are visible to everyone in the company who could raise the document, which is what makes
    /// this reachable — one person books the work, another prices it, and both have the screen open.
    /// Last-write-wins would discard whichever of them stopped typing first, with nothing said. That is
    /// the legacy behaviour the rebuild exists to remove, and the controller turns this into a 409.
    /// </remarks>
    [Fact]
    public async Task A_second_writer_holding_a_stale_version_is_refused()
    {
        var change = new FakeChangeContext { UserId = 7, CompanyId = 1 };

        long id;
        await using (var seed = _fixture.CreateContext(change))
        {
            var draft = Draft(companyId: 1, payload: """{"v":1,"state":{}}""");
            seed.DocumentDrafts.Add(draft);
            await seed.SaveChangesAsync();
            id = draft.Id;
        }

        // Two requests, each having read the draft at version 1.
        await using var first = _fixture.CreateContext(change);
        await using var second = _fixture.CreateContext(change);

        var mine = await first.DocumentDrafts.SingleAsync(d => d.Id == id);
        var theirs = await second.DocumentDrafts.SingleAsync(d => d.Id == id);

        mine.Payload = """{"v":1,"state":{"who":"first"}}""";
        mine.RowVersion++;
        await first.SaveChangesAsync();

        theirs.Payload = """{"v":1,"state":{"who":"second"}}""";
        theirs.RowVersion++;

        // The UPDATE carries `WHERE row_version = 1`, which no longer matches. Without the concurrency
        // token this would succeed and the first writer's work would be gone with no error anywhere.
        await FluentActions
            .Awaiting(() => second.SaveChangesAsync())
            .Should()
            .ThrowAsync<DbUpdateConcurrencyException>();

        await using var check = _fixture.CreateContext(change);
        (await check.DocumentDrafts.SingleAsync(d => d.Id == id)).Payload
            .Should().Be("""{"v":1,"state":{"who":"first"}}""");
    }

    /// <summary>A draft is hard-deleted — there is no soft-delete filter hiding it from its own list.</summary>
    /// <remarks>
    /// Every other table in this schema is soft-deleted, so "delete" not meaning what it says would be
    /// the reasonable assumption here. It genuinely goes: either the draft became a real document, which
    /// is audited and permanent, or somebody decided not to raise it.
    /// </remarks>
    [Fact]
    public async Task A_discarded_draft_is_gone_rather_than_tombstoned()
    {
        var change = new FakeChangeContext { UserId = 7, CompanyId = 1 };

        await using var db = _fixture.CreateContext(change);

        var draft = Draft(companyId: 1, payload: """{"v":1,"state":{}}""");
        db.DocumentDrafts.Add(draft);
        await db.SaveChangesAsync();

        await db.DocumentDrafts.Where(d => d.Id == draft.Id).ExecuteDeleteAsync();

        // IgnoreQueryFilters, so a soft delete hiding behind a filter would still be found here.
        (await db.DocumentDrafts.IgnoreQueryFilters().AnyAsync(d => d.Id == draft.Id))
            .Should().BeFalse();
    }

    private static DocumentDraft Draft(long companyId, string payload) => new()
    {
        DocType = DocumentTypes.Invoice,
        CompanyId = companyId,
        Payload = payload,
        PartyName = "Acme Trading",
        Total = 1_250.00m,
        LineCount = 3,
        CreatedBy = 7,
        CreatedAt = DateTime.UtcNow,
        UpdatedBy = 7,
        UpdatedAt = DateTime.UtcNow,
        RowVersion = 1,
    };
}

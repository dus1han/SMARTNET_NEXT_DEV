using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Notes;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Notes;

/// <summary>
/// Personal notes (Phase 7, slice 5): a title and a body, private to their author, replacing the
/// legacy shared textarea.
/// </summary>
/// <remarks>
/// There is no note service — the controller is the whole of the logic — so what is tested here is
/// what outlives it: that the audit spine attributes and versions a note without the module writing
/// any audit code, that <c>created_by</c> is a usable ownership boundary, and that the history panel
/// can address a note at all. The controller's own 404-not-403 behaviour is an HTTP concern and is
/// exercised against the running API rather than here.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class NoteTests
{
    private readonly AuditFixture _fixture;

    public NoteTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_note_is_attributed_and_timestamped_without_the_module_doing_it()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 7, CompanyId = companyId };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            var note = new UserNote
            {
                CompanyId = companyId,
                Title = "Supplier bank details",
                Body = "Confirm the account number before the next transfer.",
            };
            db.UserNotes.Add(note);
            await db.SaveChangesAsync();
            id = note.Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var note = await db.UserNotes.SingleAsync(n => n.Id == id);

            // Nothing in the notes module sets these. The interceptor does — and CreatedBy is not
            // decoration here, it is the ownership rule the controller filters on.
            note.CreatedBy.Should().Be(7);
            note.CreatedAt.Should().NotBe(default);
            note.UpdatedAt.Should().BeNull();
            note.RowVersion.Should().Be(1);
        }
    }

    [Fact]
    public async Task One_persons_notes_are_not_another_persons()
    {
        var companyId = await SeedCompany();

        await using (var db = _fixture.CreateContext(new FakeChangeContext { UserId = 11, CompanyId = companyId }))
        {
            db.UserNotes.Add(new UserNote { CompanyId = companyId, Title = "Mine", Body = "Private to 11." });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(new FakeChangeContext { UserId = 12, CompanyId = companyId }))
        {
            db.UserNotes.Add(new UserNote { CompanyId = companyId, Title = "Theirs", Body = "Private to 12." });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(new FakeChangeContext { UserId = 11, CompanyId = companyId }))
        {
            // The controller's list query, in the shape it applies it.
            var mine = await db.UserNotes.Where(n => n.CreatedBy == 11).ToListAsync();

            mine.Should().ContainSingle();
            mine[0].Title.Should().Be("Mine");
        }
    }

    [Fact]
    public async Task A_note_is_not_scoped_to_the_company_it_was_written_in()
    {
        // Notes are personal, so switching company must not hide them. The column is recorded for
        // context; it is deliberately not part of the visibility rule.
        var first = await SeedCompany();
        var second = await SeedCompany();

        await using (var db = _fixture.CreateContext(new FakeChangeContext { UserId = 21, CompanyId = first }))
        {
            db.UserNotes.Add(new UserNote { CompanyId = first, Title = "Written under company one", Body = "..." });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(new FakeChangeContext { UserId = 21, CompanyId = second }))
        {
            var visible = await db.UserNotes.Where(n => n.CreatedBy == 21).ToListAsync();

            visible.Should().ContainSingle();
            visible[0].CompanyId.Should().Be(first);
        }
    }

    [Fact]
    public async Task Editing_a_note_bumps_the_row_version_and_records_both_fields()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 7, CompanyId = companyId };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            var note = new UserNote { CompanyId = companyId, Title = "Draft", Body = "First thoughts." };
            db.UserNotes.Add(note);
            await db.SaveChangesAsync();
            id = note.Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var note = await db.UserNotes.SingleAsync(n => n.Id == id);
            note.Title = "Final";
            note.Body = "Settled on this.";
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var note = await db.UserNotes.SingleAsync(n => n.Id == id);

            // The concurrency token moved, so a stale editor's expectedRowVersion now 409s.
            note.RowVersion.Should().Be(2);
            note.UpdatedAt.Should().NotBeNull();

            // Both fields, before and after, are in the log — which is why editing needs no typed
            // reason: the question a reason would answer is already recorded.
            var entry = await db.AuditLog
                .Where(e => e.EntityType == nameof(UserNote)
                    && e.EntityId == id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    && e.Action == AuditAction.Update)
                .SingleAsync();

            entry.Changes.Should().Contain("Draft").And.Contain("Final");
            entry.Changes.Should().Contain("First thoughts.").And.Contain("Settled on this.");
        }
    }

    [Fact]
    public async Task Removing_a_note_hides_it_but_keeps_it()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 7, CompanyId = companyId, Reason = "No longer relevant." };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            var note = new UserNote { CompanyId = companyId, Title = "Temporary", Body = "Delete me later." };
            db.UserNotes.Add(note);
            await db.SaveChangesAsync();
            id = note.Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var note = await db.UserNotes.SingleAsync(n => n.Id == id);
            note.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.UserNotes.CountAsync(n => n.Id == id)).Should().Be(0);

            var removed = await db.UserNotes.IgnoreQueryFilters().SingleAsync(n => n.Id == id);
            removed.DeletedAt.Should().NotBeNull();
            removed.CreatedBy.Should().Be(7);
            removed.Title.Should().Be("Temporary");
        }
    }

    /// <summary>
    /// The History dialog addresses a note as <c>UserNote</c>; the server must agree that is a record.
    /// </summary>
    [Fact]
    public async Task The_history_panel_can_address_a_note()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        new AuditHistoryReader(db).IsAuditableEntity(nameof(UserNote)).Should().BeTrue();
    }

    private async Task<long> SeedCompany()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });
        var company = new Company { Name = $"Notes co {Guid.NewGuid():N}"[..20], VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }
}

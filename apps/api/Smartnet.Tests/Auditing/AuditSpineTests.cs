using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;

namespace Smartnet.Tests.Auditing;

/// <summary>
/// Each test here is one promise AUDIT.md makes, held to the fire.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class AuditSpineTests
{
    private readonly AuditFixture _fixture;

    private static readonly FakeChangeContext Chanaka = new()
    {
        UserId = 1,
        CompanyId = 7,
        Reason = "Corrected the customer's VAT number after they sent the certificate.",
        IpAddress = "10.0.0.9",
        UserAgent = "Mozilla/5.0",
        CorrelationId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
    };

    public AuditSpineTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Creating_a_row_audits_it_without_any_audit_code()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        var widget = new Widget { Name = "Cable, 3m" };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        var entry = await LastEntryFor(widget.Id);

        entry.Action.Should().Be(AuditAction.Create);
        entry.EntityType.Should().Be(nameof(Widget));
        entry.EntityId.Should().Be(Key(widget.Id));

        // The user id, never a display name — rename the user and history must stay unambiguous.
        entry.ChangedBy.Should().Be(1);
        entry.CompanyId.Should().Be(7);
        entry.CorrelationId.Should().Be(Chanaka.CorrelationId);
        entry.IpAddress.Should().Be("10.0.0.9");
    }

    [Fact]
    public async Task Creating_a_row_stamps_the_audit_columns()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        var widget = new Widget { Name = "Router" };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        widget.CreatedBy.Should().Be(1);
        widget.CreatedAt.Should().NotBe(default);
        widget.CreatedAt.Kind.Should().Be(DateTimeKind.Utc, "timestamps are stored in UTC");
        widget.RowVersion.Should().Be(1);
    }

    [Fact]
    public async Task An_update_logs_only_the_fields_that_actually_changed()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        var widget = new Widget { Name = "Switch, 8-port" };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        widget.Name = "Switch, 16-port";
        await db.SaveChangesAsync();

        var entry = await LastEntryFor(widget.Id);
        entry.Action.Should().Be(AuditAction.Update);

        var changes = Parse(entry.Changes);

        changes.Should().ContainKey("Name");
        changes["Name"].GetProperty("from").GetString().Should().Be("Switch, 8-port");
        changes["Name"].GetProperty("to").GetString().Should().Be("Switch, 16-port");

        // Only changed fields — not the whole row, and not the audit columns themselves, which
        // change on every save and would drown the diff in noise.
        changes.Should().NotContainKey("UpdatedAt");
        changes.Should().NotContainKey("RowVersion");
        changes.Should().NotContainKey("Id");
    }

    [Fact]
    public async Task Assigning_a_field_its_existing_value_is_not_a_change()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        var widget = new Widget { Name = "Patch panel" };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        var createdAudits = await CountEntriesFor(widget.Id);

        // EF marks a property Modified when it is assigned, not when it differs. An audit log
        // full of no-op "changes" is an audit log nobody reads.
        widget.Name = "Patch panel";
        await db.SaveChangesAsync();

        (await CountEntriesFor(widget.Id)).Should().Be(createdAudits);
    }

    [Fact]
    public async Task A_redacted_field_is_recorded_as_changed_but_never_with_its_value()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        const string secret = "$argon2id$v=19$m=65536,t=3,p=1$c29tZXNhbHQ$hash";

        var widget = new Widget { Name = "User row", PasswordHash = secret };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        widget.PasswordHash = secret + "-rotated";
        await db.SaveChangesAsync();

        var all = await _fixture.CreateContext(Chanaka).AuditLog
            .Where(a => a.EntityType == nameof(Widget) && a.EntityId == Key(widget.Id))
            .ToListAsync();

        var serialised = string.Join("\n", all.Select(a => a.Changes));

        // The fact of the change is evidence. The value is a credential.
        serialised.Should().Contain("PasswordHash");
        serialised.Should().Contain(AuditRedaction.Placeholder);
        serialised.Should().NotContain(secret, "an audit log that leaks the secrets it audits is worse than none");
    }

    [Fact]
    public async Task A_delete_is_soft_recoverable_and_attributable()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        var widget = new Widget { Name = "Deleted invoice stand-in" };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        db.Widgets.Remove(widget);
        await db.SaveChangesAsync();

        var entry = await LastEntryFor(widget.Id);
        entry.Action.Should().Be(AuditAction.Delete);
        entry.Reason.Should().Be(Chanaka.Reason);

        // Nothing is hard-deleted: the row is still there, and it says who removed it.
        await using var fresh = _fixture.CreateContext(Chanaka);
        var stored = await fresh.Widgets.SingleAsync(w => w.Id == widget.Id);

        stored.DeletedAt.Should().NotBeNull();
        stored.DeletedBy.Should().Be(1);
    }

    [Fact]
    public async Task Two_users_editing_the_same_row_conflict_loudly()
    {
        await using var setup = _fixture.CreateContext(Chanaka);
        var widget = new Widget { Name = "Contested" };
        setup.Widgets.Add(widget);
        await setup.SaveChangesAsync();

        await using var first = _fixture.CreateContext(Chanaka);
        await using var second = _fixture.CreateContext(Chanaka);

        var mine = await first.Widgets.SingleAsync(w => w.Id == widget.Id);
        var theirs = await second.Widgets.SingleAsync(w => w.Id == widget.Id);

        mine.Name = "Mine";
        await first.SaveChangesAsync();

        theirs.Name = "Theirs";

        // The legacy app has no protection against this at all: the second write simply wins,
        // silently. Here it fails, and the user is told to reload.
        await second.Invoking(s => s.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task A_failed_business_write_leaves_no_audit_row_behind()
    {
        await using var db = _fixture.CreateContext(Chanaka);

        // 100 chars is the column limit; this violates it, so the INSERT fails at the database.
        var doomed = new Widget { Name = new string('x', 500) };
        db.Widgets.Add(doomed);

        await db.Invoking(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

        // Audit and data cannot diverge: either both commit, or neither does. If the audit row
        // had been written in its own transaction, this count would be 1 — an audit trail
        // describing a change that never happened.
        await using var fresh = _fixture.CreateContext(Chanaka);
        var orphans = await fresh.AuditLog
            .CountAsync(a => a.EntityType == nameof(Widget) && a.Changes!.Contains("xxxxx"));

        orphans.Should().Be(0);
    }

    private async Task<AuditLogEntry> LastEntryFor(long widgetId)
    {
        await using var db = _fixture.CreateContext(Chanaka);

        return await db.AuditLog
            .Where(a => a.EntityType == nameof(Widget) && a.EntityId == Key(widgetId))
            .OrderByDescending(a => a.Id)
            .FirstAsync();
    }

    private async Task<int> CountEntriesFor(long widgetId)
    {
        await using var db = _fixture.CreateContext(Chanaka);

        return await db.AuditLog
            .CountAsync(a => a.EntityType == nameof(Widget) && a.EntityId == Key(widgetId));
    }

    /// <summary>
    /// Invariant, matching how the interceptor writes the key. What a row is called in the audit
    /// log must not depend on the server's locale.
    /// </summary>
    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<string, JsonElement> Parse(string? changes) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(changes ?? "{}")!;
}

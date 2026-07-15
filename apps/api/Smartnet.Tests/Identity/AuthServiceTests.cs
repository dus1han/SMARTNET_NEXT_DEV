using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Identity;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Identity;

/// <summary>
/// Slice 2's promises: nobody's plaintext password survives their next login, nobody can guess
/// their way in, and everything that happens at the door is written down.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class AuthServiceTests
{
    private readonly AuditFixture _fixture;
    private readonly MutableClock _clock = new(new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc));

    public AuthServiceTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_legacy_plaintext_password_logs_in_and_is_upgraded_to_a_hash()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "legacy-user", legacyPassword: "1234");

        var result = await auth.LoginAsync("legacy-user", "1234");

        result.Outcome.Should().Be(LoginOutcome.Success);

        // The whole point of the cutover strategy: the user typed the same password they always
        // have, and walked away with an Argon2id hash they never noticed being created.
        var stored = await Reload(user.Id);
        stored.PasswordHash.Should().NotBeNullOrEmpty();
        stored.PasswordHash.Should().StartWith("$argon2id$");

        // And the plaintext column is untouched — the legacy app is still live and still logs
        // this same person in with it. It goes in Phase 9, not before.
        stored.LegacyPassword.Should().Be("1234");
    }

    [Fact]
    public async Task Once_hashed_the_plaintext_column_is_no_longer_what_authenticates()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "hashed-user", legacyPassword: "1234");

        await auth.LoginAsync("hashed-user", "1234");

        // Someone edits the legacy password column directly — as the legacy app would, and as
        // anyone holding those published credentials could. It must no longer be a way in,
        // because the hash now takes precedence over it.
        user.LegacyPassword = "letmein";
        await db.SaveChangesAsync();

        var result = await auth.LoginAsync("hashed-user", "letmein");

        result.Outcome.Should().Be(LoginOutcome.InvalidCredentials);

        // And the real password still works, so the hash is genuinely what is being consulted.
        (await auth.LoginAsync("hashed-user", "1234")).Outcome.Should().Be(LoginOutcome.Success);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_even_against_the_correct_password()
    {
        var (db, auth) = Subject();
        await GivenUser(db, "brute-target", legacyPassword: "1234");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await auth.LoginAsync("brute-target", "guess");
        }

        // The correct password, at the wrong moment. The legacy app would have let them in.
        var result = await auth.LoginAsync("brute-target", "1234");

        result.Outcome.Should().Be(LoginOutcome.LockedOut);
    }

    [Fact]
    public async Task The_lock_expires_by_itself()
    {
        var (db, auth) = Subject();
        await GivenUser(db, "patient-user", legacyPassword: "1234");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await auth.LoginAsync("patient-user", "guess");
        }

        (await auth.LoginAsync("patient-user", "1234")).Outcome.Should().Be(LoginOutcome.LockedOut);

        // Nobody should have to phone the developer to be let back in.
        _clock.Advance(TimeSpan.FromMinutes(16));

        (await auth.LoginAsync("patient-user", "1234")).Outcome.Should().Be(LoginOutcome.Success);
    }

    [Fact]
    public async Task A_successful_login_clears_the_failure_count()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "fat-fingers", legacyPassword: "1234");

        await auth.LoginAsync("fat-fingers", "typo");
        await auth.LoginAsync("fat-fingers", "typo");
        await auth.LoginAsync("fat-fingers", "1234");

        // Otherwise a user who mistypes twice a day locks themselves out by Thursday.
        (await Reload(user.Id)).FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public async Task A_disabled_user_cannot_log_in_with_the_right_password()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "ex-employee", legacyPassword: "1234");

        // The legacy app's own notion of "disabled" — it has no soft-delete column.
        user.Ustat = "Inactive";
        await db.SaveChangesAsync();

        var result = await auth.LoginAsync("ex-employee", "1234");

        result.Outcome.Should().Be(LoginOutcome.Disabled);
    }

    [Fact]
    public async Task A_failed_login_is_audited_even_for_a_username_that_does_not_exist()
    {
        var (_, auth) = Subject();

        await auth.LoginAsync("mallory", "hunter2");

        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var entry = await db.AuditLog
            .Where(a => a.Action == AuditAction.Login && a.EntityId == "mallory")
            .OrderByDescending(a => a.Id)
            .FirstAsync();

        // "Who has been trying to log in, and as whom?" is unanswerable in the legacy app: it
        // records neither successes nor failures.
        entry.Changes.Should().Contain("failure");
        entry.Changes.Should().Contain("no such user");
        entry.ChangedBy.Should().BeNull("nobody was authenticated");
    }

    [Fact]
    public async Task Changing_a_password_requires_the_current_one()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "cautious", legacyPassword: "1234");

        var result = await auth.ChangePasswordAsync(
            user.Id, currentPassword: "not-it", newPassword: "a-much-longer-password");

        // A hijacked session must not be able to set a password the real owner does not know,
        // which would lock them out of their own account.
        result.Should().Be(ChangePasswordResult.InvalidCurrentPassword);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "weak-choice", legacyPassword: "1234");

        var result = await auth.ChangePasswordAsync(user.Id, "1234", "password");

        result.Should().Be(ChangePasswordResult.NewPasswordTooWeak);
    }

    [Fact]
    public async Task Changing_a_password_clears_the_forced_change_and_writes_through_to_legacy()
    {
        var (db, auth) = Subject();
        var user = await GivenUser(db, "upgrader", legacyPassword: "1234", mustChange: true);

        var result = await auth.ChangePasswordAsync(user.Id, "1234", "a-properly-long-password");

        result.Should().Be(ChangePasswordResult.Success);

        var stored = await Reload(user.Id);
        stored.MustChangePassword.Should().BeFalse();
        stored.PasswordHash.Should().StartWith("$argon2id$");

        // The dual-write window: the legacy app authenticates against the plaintext column, so it
        // has to keep matching or the user cannot log into the old app after changing it in the
        // new one. This assertion is here to make the ugliness visible, and to fail loudly in
        // Phase 9 when the column is finally dropped.
        stored.LegacyPassword.Should().Be("a-properly-long-password");
    }

    // --- plumbing -----------------------------------------------------------------------

    private (Auditing.TestDbContext Db, AuthService Auth) Subject()
    {
        var db = _fixture.CreateContext(new FakeChangeContext());

        var auth = new AuthService(
            db,
            new Argon2PasswordHasher(),
            new AuditWriter(db, new FakeChangeContext(), _clock),
            _clock);

        return (db, auth);
    }

    private static async Task<User> GivenUser(
        Auditing.TestDbContext db,
        string username,
        string legacyPassword,
        bool mustChange = false)
    {
        var user = new User
        {
            Username = username,
            Name = username,
            LegacyPassword = legacyPassword,
            MustChangePassword = mustChange,
            Ustat = "Active",
            Addedby = "test",
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    /// <summary>
    /// Reads the row back through a <i>fresh</i> context, so the assertion sees what is actually
    /// in the database rather than the instance the service still has in memory.
    /// </summary>
    private async Task<User> Reload(long id)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        return await db.Users.SingleAsync(u => u.Id == id);
    }
}

/// <summary>A clock the test drives, so "wait 15 minutes" does not mean waiting 15 minutes.</summary>
internal sealed class MutableClock : TimeProvider
{
    private DateTimeOffset _now;

    public MutableClock(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

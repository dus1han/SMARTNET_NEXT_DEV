using System.Globalization;
using FluentAssertions;
using Smartnet.Domain.Backups;

namespace Smartnet.Tests.Backups;

/// <summary>
/// The rotation — the code whose job is to delete backups.
/// </summary>
/// <remarks>
/// Tested harder than its size suggests, because the cost of it being wrong is asymmetric: keeping one
/// backup too many wastes a few megabytes on somebody else's disk, and deleting one too many is
/// unrecoverable. It is a pure function precisely so that this can be tested without an FTP server.
/// </remarks>
public sealed class BackupRetentionTests
{
    private static BackupFile File(string name, int day = 1) =>
        new(name, 1024, new DateTime(2026, 7, day, 0, 0, 0, DateTimeKind.Utc));

    private static BackupFile Auto(int hour) =>
        File($"smartnet-auto-20260721-{hour:D2}0000.sql.gz");

    [Fact]
    public void Nothing_is_pruned_while_the_rotation_is_not_full()
    {
        var files = Enumerable.Range(1, 10).Select(Auto).ToList();

        BackupRetention.ToPrune(files, keep: 15).Should().BeEmpty();
    }

    [Fact]
    public void Exactly_the_oldest_go_once_it_is_full()
    {
        // Sixteen hourly backups, keeping fifteen: the 00:00 one is the only casualty.
        var files = Enumerable.Range(0, 16).Select(Auto).ToList();

        var pruned = BackupRetention.ToPrune(files, keep: 15);

        pruned.Should().ContainSingle();
        pruned[0].Name.Should().Be("smartnet-auto-20260721-000000.sql.gz");
    }

    [Fact]
    public void The_newest_fifteen_survive_whatever_order_the_server_lists_them_in()
    {
        // FTP servers list in whatever order they please. The rule must not depend on it.
        var files = Enumerable.Range(0, 20).Select(Auto).Reverse().ToList();

        var kept = files.Except(BackupRetention.ToPrune(files, keep: 15)).ToList();

        kept.Should().HaveCount(15);
        kept.Select(f => f.Name).Should().Contain("smartnet-auto-20260721-190000.sql.gz");
        kept.Select(f => f.Name).Should().NotContain("smartnet-auto-20260721-040000.sql.gz");
    }

    [Fact]
    public void Manual_backups_share_the_rotation_with_scheduled_ones()
    {
        // One rotation, whatever took the backup — the name sorts by time, not by kind.
        var files = new List<BackupFile>
        {
            File("smartnet-auto-20260721-010000.sql.gz"),
            File("smartnet-manual-20260721-020000.sql.gz"),
            File("smartnet-auto-20260721-030000.sql.gz"),
        };

        var pruned = BackupRetention.ToPrune(files, keep: 2);

        pruned.Should().ContainSingle();
        pruned[0].Name.Should().Be("smartnet-auto-20260721-010000.sql.gz");
    }

    [Fact]
    public void Files_that_are_not_ours_are_never_touched()
    {
        // The destination may be a shared folder. This deletes only what it can prove it wrote — a
        // rotation that tidied up other people's files would be a very expensive kind of helpful.
        var files = new List<BackupFile>
        {
            File("important-client-data.zip"),
            File("smartnet-auto-20260721-010000.sql.gz"),
            File("notes.txt"),
            File("smartnet-auto-20260721-020000.sql.gz"),
        };

        var pruned = BackupRetention.ToPrune(files, keep: 1);

        pruned.Should().ContainSingle();
        pruned[0].Name.Should().Be("smartnet-auto-20260721-010000.sql.gz");
    }

    [Fact]
    public void Keeping_none_prunes_every_backup_but_still_only_ours()
    {
        var files = new List<BackupFile>
        {
            File("smartnet-auto-20260721-010000.sql.gz"),
            File("someone-elses-file.gz"),
        };

        var pruned = BackupRetention.ToPrune(files, keep: 0);

        pruned.Should().ContainSingle();
        pruned[0].Name.Should().Be("smartnet-auto-20260721-010000.sql.gz");
    }

    [Fact]
    public void An_older_manual_backup_does_not_outrank_a_newer_hourly_one()
    {
        // The reported bug, at the size that makes it dangerous. Fifteen manual backups from the morning
        // and one hourly backup from the afternoon: ordering by name put every manual above every auto,
        // filling the rotation, so the hourly backup was deleted moments after it was uploaded.
        var manuals = Enumerable.Range(0, 15)
            .Select(hour => File($"smartnet-manual-20260721-{hour:D2}0000.sql.gz"))
            .ToList();

        var newest = Auto(23);

        var pruned = BackupRetention.ToPrune([.. manuals, newest], keep: 15);

        pruned.Should().ContainSingle().Which.Name.Should().Be("smartnet-manual-20260721-000000.sql.gz");
        pruned.Should().NotContain(f => f.Name == newest.Name, "the newest backup is never the one deleted");
    }

    [Fact]
    public void A_name_without_a_readable_stamp_is_never_deleted()
    {
        // It passes IsBackupName, so it is not obviously foreign — but nothing here can say how old it is,
        // and "unknown age" must not sort as "oldest" in the code that deletes things.
        var undateable = File("smartnet-auto-something-else.sql.gz");
        var files = Enumerable.Range(0, 20).Select(Auto).Append(undateable).ToList();

        BackupRetention.ToPrune(files, keep: 5).Should().NotContain(f => f.Name == undateable.Name);
    }

    [Fact]
    public void A_negative_retention_is_a_bug_and_says_so()
    {
        var act = () => BackupRetention.ToPrune([], keep: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

/// <summary>The filename convention, which is the only metadata that survives a round trip through FTP.</summary>
public sealed class BackupNamingTests
{
    [Fact]
    public void A_name_carries_the_kind_and_a_sortable_utc_stamp()
    {
        var name = BackupNaming.For(BackupKind.Scheduled, new DateTime(2026, 7, 21, 14, 5, 9, DateTimeKind.Utc));

        name.Should().Be("smartnet-auto-20260721-140509.sql.gz");
    }

    [Fact]
    public void Sorting_names_does_NOT_order_them_by_time()
    {
        // This used to assert the opposite, and passed, because both names it compared were the same
        // kind. Across kinds the property is simply false — the kind sits in front of the stamp — and
        // the rotation was built on it. Pinned as a falsehood so nobody reintroduces the assumption.
        var later = BackupNaming.For(BackupKind.Scheduled, new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc));
        var earlier = BackupNaming.For(BackupKind.Manual, new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc));

        string.CompareOrdinal(earlier, later).Should().BePositive("'manual' sorts above 'auto'");

        BackupNaming.TakenAtUtc(earlier).Should().BeBefore(BackupNaming.TakenAtUtc(later)!.Value);
    }

    [Theory]
    [InlineData("smartnet-auto-20260721-140509.sql.gz", "2026-07-21T14:05:09Z")]
    [InlineData("smartnet-prerestore-20261231-235959.sql.gz", "2026-12-31T23:59:59Z")]
    public void The_stamp_is_read_back_as_the_utc_it_was_written_in(string name, string expected)
    {
        var taken = BackupNaming.TakenAtUtc(name);

        taken.Should().Be(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        taken!.Value.Kind.Should().Be(DateTimeKind.Utc, "a stamp read as local time drifts by the offset");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("smartnet-auto.sql.gz")]
    [InlineData("smartnet-auto-20261332-140509.sql.gz")] // month 13, day 32
    public void An_unreadable_stamp_is_null_rather_than_a_guess(string? name) =>
        BackupNaming.TakenAtUtc(name).Should().BeNull();

    [Theory]
    [InlineData("smartnet-auto-20260721-140509.sql.gz")]
    [InlineData("smartnet-manual-20260721-140509.sql.gz")]
    [InlineData("smartnet-prerestore-20260721-140509.sql.gz")]
    public void Our_own_names_are_recognised(string name) =>
        BackupNaming.IsBackupName(name).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("backup.sql.gz")]                          // not ours
    [InlineData("smartnet-auto-20260721-140509.sql")]      // not gzipped
    [InlineData("../smartnet-auto-20260721-140509.sql.gz")] // traversal
    [InlineData("smartnet-auto/../../etc/passwd.sql.gz")]   // traversal, embedded
    [InlineData("smartnet-auto\\20260721.sql.gz")]          // separator
    public void Anything_else_is_refused(string? name) =>
        // Refused rather than sanitised: a name that needs cleaning is not a name we wrote. Download,
        // restore and delete all take a name from the caller, so this is the guard on all three.
        BackupNaming.IsBackupName(name).Should().BeFalse();
}

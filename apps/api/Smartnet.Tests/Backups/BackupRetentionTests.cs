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
    public void Sorting_names_orders_them_by_time()
    {
        // The property the rotation depends on: newest-first by ordinal name is newest-first by clock.
        var earlier = BackupNaming.For(BackupKind.Scheduled, new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc));
        var later = BackupNaming.For(BackupKind.Scheduled, new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc));

        string.CompareOrdinal(earlier, later).Should().BeNegative();
    }

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

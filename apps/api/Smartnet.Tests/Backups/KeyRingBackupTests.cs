using System.Formats.Tar;
using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smartnet.Domain.Backups;
using Smartnet.Infrastructure.Backups;

namespace Smartnet.Tests.Backups;

/// <summary>
/// The key ring backup — the file that stops a database restore coming up with unreadable passwords.
/// </summary>
/// <remarks>
/// No FTP server and no database: the snapshot is pure filesystem, and the orchestration is proven with
/// in-memory fakes for the store, the dump and the ring. The one guarantee worth the most here is the last
/// test — that a ring failure never costs the database backup that already succeeded.
/// </remarks>
public sealed class KeyRingBackupTests
{
    [Fact]
    public async Task Snapshot_archives_every_key_file_and_is_valid_gzip()
    {
        var dir = Directory.CreateTempSubdirectory("keyring-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "key-a.xml"), "<key>a</key>");
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "key-b.xml"), "<key>b</key>");

            using var archive = new MemoryStream();
            await new KeyRingBackup(dir.FullName).SnapshotToAsync(archive);

            archive.Position = 0;
            var entries = await ReadTarGzEntryNamesAsync(archive);

            entries.Should().Contain(n => n.EndsWith("key-a.xml", StringComparison.Ordinal));
            entries.Should().Contain(n => n.EndsWith("key-b.xml", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_scheduled_backup_also_uploads_the_key_ring()
    {
        var storage = new FakeStorage();
        var keyRing = new FakeKeyRing();

        await NewService(storage, keyRing).BackupAsync(BackupKind.Scheduled);

        keyRing.Called.Should().BeTrue();
        storage.Uploaded.Should().Contain(BackupNaming.KeyRingName);
        storage.Uploaded.Should().Contain(n => n.EndsWith(".sql.gz", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pre_restore_backup_leaves_the_key_ring_alone()
    {
        var storage = new FakeStorage();
        var keyRing = new FakeKeyRing();

        // The safety copy taken before a restore is the database's own undo — the ring has no part in it.
        await NewService(storage, keyRing).BackupAsync(BackupKind.PreRestore);

        keyRing.Called.Should().BeFalse();
        storage.Uploaded.Should().NotContain(BackupNaming.KeyRingName);
    }

    [Fact]
    public async Task A_key_ring_failure_does_not_fail_the_database_backup()
    {
        var storage = new FakeStorage();

        var act = () => NewService(storage, new FakeKeyRing(throws: true)).BackupAsync(BackupKind.Scheduled);

        await act.Should().NotThrowAsync();
        storage.Uploaded.Should().Contain(n => n.EndsWith(".sql.gz", StringComparison.Ordinal)); // db backup stood
        storage.Uploaded.Should().NotContain(BackupNaming.KeyRingName);                          // ring didn't land
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private static BackupService NewService(FakeStorage storage, FakeKeyRing keyRing) =>
        new(storage, new FakeDatabaseBackup(), keyRing, new FakeDestinations(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new BackupOptions()), TimeProvider.System,
            NullLogger<BackupService>.Instance);

    private static async Task<List<string>> ReadTarGzEntryNamesAsync(Stream archive)
    {
        var names = new List<string>();
        await using var gunzip = new GZipStream(archive, CompressionMode.Decompress);
        await using var reader = new TarReader(gunzip);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile)
            {
                names.Add(entry.Name);
            }
        }

        return names;
    }

    private sealed class FakeStorage : IBackupStorage
    {
        public List<string> Uploaded { get; } = [];

        public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackupFile>>([]);

        public Task UploadAsync(string name, Stream content, BackupKind kind, CancellationToken cancellationToken = default)
        {
            Uploaded.Add(name);
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDatabaseBackup : IDatabaseBackup
    {
        public Task DumpAsync(Stream destination, CancellationToken cancellationToken = default) =>
            destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken).AsTask();

        public Task RestoreAsync(Stream gzippedDump, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeKeyRing(bool throws = false) : IKeyRingBackup
    {
        public bool Called { get; private set; }

        public Task SnapshotToAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            Called = true;

            if (throws)
            {
                throw new IOException("key ring could not be read");
            }

            return destination.WriteAsync(new byte[] { 9 }, cancellationToken).AsTask();
        }
    }

    private sealed class FakeDestinations : IBackupDestinationProvider
    {
        public Task<BackupDestination?> CurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<BackupDestination?>(new BackupDestination(
                Enabled: true, Host: "ftp.example", Port: 21, Username: "u", Password: "p",
                UseTls: true, AcceptAnyCertificate: false, RemotePath: "/backups", SafetyPath: "/safety",
                Retention: 15));
    }
}

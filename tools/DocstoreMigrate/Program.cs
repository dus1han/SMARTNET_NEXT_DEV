// Materialises the legacy docstore BLOBs onto the document store's filesystem (Phase 7, slice 4).
//
// Reads each docstore row, writes its pdfdoc bytes to a file under the storage root using the same
// naming the running app uses, and inserts a `documents` row pointing at it.
//
// WHAT THIS DOES NOT DO: drop docstore.pdfdoc. The bytes stay in the database after this runs, so the
// migration is reversible — delete the documents rows and the files, and nothing is lost. Dropping the
// column is a separate, deliberate step at cutover (docs/MIGRATION-DATA-CHECKS.md).
//
// Idempotent: documents.legacy_docstore_id is uniquely indexed, so a row already materialised is
// skipped, and a concurrent second run is refused by the database rather than by this tool's own check.
//
//   dotnet run -- --company 1                     # dry run: says what it would do, writes nothing
//   dotnet run -- --company 1 --apply             # does it
//   dotnet run -- --company 1 --apply --root PATH # explicit storage root
//   dotnet run -- --verify                        # re-checks every migrated file against its hash

using System.Globalization;
using System.Security.Cryptography;
using MySqlConnector;

var apply = args.Contains("--apply", StringComparer.Ordinal);
var verifyOnly = args.Contains("--verify", StringComparer.Ordinal);
var companyId = ArgValue("--company") is { } c && long.TryParse(c, CultureInfo.InvariantCulture, out var parsed)
    ? parsed
    : (long?)null;

// Defaults to the API's own development path so a local run lands where the local API reads from.
var root = Path.GetFullPath(ArgValue("--root") ?? Path.Combine(
    RepoRoot(), "apps", "api", "Smartnet.Api", "App_Data", "documents"));

var connectionString = ConnectionString();

if (connectionString is null)
{
    Console.Error.WriteLine("No connection string. Set ConnectionStrings__Smartnet in .env at the repo root.");
    return 1;
}

// The production guard the API carries, repeated here: this tool writes rows and files, and pointing it
// at production by accident is exactly the mistake worth making impossible rather than unlikely.
if (connectionString.Contains("Database=smartnet_invsys;", StringComparison.OrdinalIgnoreCase)
    || connectionString.TrimEnd().EndsWith("Database=smartnet_invsys", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Refusing to run against the PRODUCTION database (smartnet_invsys).");
    return 1;
}

await using var conn = new MySqlConnection(connectionString);
await conn.OpenAsync();

if (verifyOnly)
{
    return await Verify(conn, root);
}

if (companyId is null)
{
    Console.Error.WriteLine(
        """
        --company is required.

        docstore has no company column, and the 18 documents in it span both trading entities
        (there is a "VAT CERTIFICATE SMART NET" and a "SMART BRC" in the same table). Which company
        they belong to is a business decision, not something to infer from a title.

          1 = Smart Technologies
          2 = Smart Net
        """);
    return 1;
}

Console.WriteLine($"Storage root : {root}");
Console.WriteLine($"Company      : {companyId}");
Console.WriteLine($"Mode         : {(apply ? "APPLY - writes files and rows" : "DRY RUN - writes nothing")}");
Console.WriteLine();

// Already-materialised ids, so a re-run reports them as skipped rather than attempting and failing on
// the unique index.
var already = new HashSet<int>();
await using (var cmd = new MySqlCommand(
    "SELECT legacy_docstore_id FROM documents WHERE legacy_docstore_id IS NOT NULL", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync()) already.Add(reader.GetInt32(0));
}

var rows = new List<(int Id, string Title, string Ext, byte[] Bytes, string? AddedBy, string? AddedDate)>();
await using (var cmd = new MySqlCommand(
    "SELECT id, title, docext, pdfdoc, addedby, addeddate FROM docstore ORDER BY id", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        rows.Add((
            reader.GetInt32(0),
            reader.IsDBNull(1) ? "Untitled" : reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            reader.IsDBNull(3) ? [] : (byte[])reader.GetValue(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5)));
    }
}

// The same whitelist the upload endpoint enforces. A docstore row with an extension nobody may upload
// today does not get to walk onto disk just because it is old.
var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".pdf"] = "application/pdf",
    [".doc"] = "application/msword",
    [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    [".xls"] = "application/vnd.ms-excel",
    [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    [".csv"] = "text/csv",
    [".txt"] = "text/plain",
    [".png"] = "image/png",
    [".jpg"] = "image/jpeg",
    [".jpeg"] = "image/jpeg",
    [".gif"] = "image/gif",
    [".webp"] = "image/webp",
};

int migrated = 0, skipped = 0, refused = 0, empty = 0;

foreach (var row in rows)
{
    if (already.Contains(row.Id))
    {
        Console.WriteLine($"  skip  {row.Id,3}  {row.Title,-30}  already materialised");
        skipped++;
        continue;
    }

    if (row.Bytes.Length == 0)
    {
        Console.WriteLine($"  EMPTY {row.Id,3}  {row.Title,-30}  no bytes - nothing to write");
        empty++;
        continue;
    }

    var ext = row.Ext.StartsWith('.') ? row.Ext : "." + row.Ext;

    if (!allowed.TryGetValue(ext, out var contentType))
    {
        Console.WriteLine($"  REFUSE{row.Id,3}  {row.Title,-30}  '{ext}' is not on the whitelist");
        refused++;
        continue;
    }

    var storedName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
    var hash = Convert.ToHexString(SHA256.HashData(row.Bytes)).ToLowerInvariant();
    var size = row.Bytes.Length;

    Console.WriteLine(
        $"  {(apply ? "write" : "would"),-5} {row.Id,3}  {row.Title,-30}  {size / 1024,6} KB  -> {storedName}");

    if (!apply)
    {
        migrated++;
        continue;
    }

    // The file first. A file with no row is an orphan nobody sees and the next run rewrites; a row with
    // no file is a document that appears in the list and 410s when opened.
    var path = Path.Combine(root, storedName[..2], storedName);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllBytesAsync(path, row.Bytes);

    // Read it back and re-hash: this is the whole point of the exercise, so "I wrote it" is not good
    // enough. If the disk lied, the row is not written and the BLOB is still there to try again.
    var written = await File.ReadAllBytesAsync(path);
    var writtenHash = Convert.ToHexString(SHA256.HashData(written)).ToLowerInvariant();

    if (writtenHash != hash)
    {
        Console.Error.WriteLine($"  FAIL  {row.Id,3}  hash mismatch after write - leaving the BLOB in place");
        File.Delete(path);
        refused++;
        continue;
    }

    await using var insert = new MySqlCommand(
        """
        INSERT INTO documents
            (company_id, title, original_filename, stored_name, content_type, byte_size, sha256,
             legacy_docstore_id, created_at, row_version)
        VALUES
            (@company, @title, @filename, @stored, @type, @size, @hash, @legacy, UTC_TIMESTAMP(), 0)
        """, conn);

    insert.Parameters.AddWithValue("@company", companyId);
    insert.Parameters.AddWithValue("@title", Truncate(row.Title, 255));
    insert.Parameters.AddWithValue("@filename", Truncate(SafeName(row.Title) + ext.ToLowerInvariant(), 255));
    insert.Parameters.AddWithValue("@stored", storedName);
    insert.Parameters.AddWithValue("@type", contentType);
    insert.Parameters.AddWithValue("@size", size);
    insert.Parameters.AddWithValue("@hash", hash);
    insert.Parameters.AddWithValue("@legacy", row.Id);

    await insert.ExecuteNonQueryAsync();
    migrated++;
}

Console.WriteLine();
Console.WriteLine($"{(apply ? "Migrated" : "Would migrate")}: {migrated}   skipped: {skipped}   refused: {refused}   empty: {empty}   of {rows.Count} docstore rows");

if (!apply)
{
    Console.WriteLine("\nDry run. Nothing was written. Re-run with --apply to do it.");
}
else
{
    Console.WriteLine("\nThe BLOBs are still in docstore.pdfdoc - this migration is reversible.");
    Console.WriteLine("Dropping that column is a separate cutover step: docs/MIGRATION-DATA-CHECKS.md");
}

return 0;

// --- verification ------------------------------------------------------------------------------

static async Task<int> Verify(MySqlConnection conn, string root)
{
    Console.WriteLine($"Verifying every migrated document against its recorded hash.\nRoot: {root}\n");

    var bad = 0;
    var checkedCount = 0;

    await using var cmd = new MySqlCommand(
        """
        SELECT legacy_docstore_id, title, stored_name, sha256, byte_size
        FROM documents
        WHERE legacy_docstore_id IS NOT NULL AND deleted_at IS NULL
        ORDER BY legacy_docstore_id
        """, conn);

    await using var reader = await cmd.ExecuteReaderAsync();
    var rows = new List<(int Legacy, string Title, string Stored, string Hash, long Size)>();

    while (await reader.ReadAsync())
    {
        rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));
    }

    await reader.CloseAsync();

    foreach (var row in rows)
    {
        checkedCount++;
        var path = Path.Combine(root, row.Stored[..2], row.Stored);

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"  MISSING  {row.Legacy,3}  {row.Title,-30}  {row.Stored}");
            bad++;
            continue;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (hash != row.Hash || bytes.LongLength != row.Size)
        {
            Console.Error.WriteLine($"  CORRUPT  {row.Legacy,3}  {row.Title,-30}  hash or size differs");
            bad++;
            continue;
        }

        Console.WriteLine($"  ok       {row.Legacy,3}  {row.Title,-30}  {bytes.Length / 1024,6} KB");
    }

    Console.WriteLine($"\n{checkedCount - bad} of {checkedCount} verified.");

    if (bad > 0)
    {
        Console.Error.WriteLine("NOT SAFE to drop docstore.pdfdoc - the database is still the only copy of the above.");
        return 1;
    }

    Console.WriteLine("Every migrated document is on disk and matches its hash.");
    return 0;
}

// --- helpers -----------------------------------------------------------------------------------

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string RepoRoot()
{
    var dir = AppContext.BaseDirectory;

    while (dir is not null && !File.Exists(Path.Combine(dir, ".env")) && !Directory.Exists(Path.Combine(dir, ".git")))
    {
        dir = Path.GetDirectoryName(dir);
    }

    return dir ?? Directory.GetCurrentDirectory();
}

static string? ConnectionString()
{
    var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Smartnet");
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

    var envFile = Path.Combine(RepoRoot(), ".env");
    if (!File.Exists(envFile)) return null;

    foreach (var line in File.ReadAllLines(envFile))
    {
        if (line.StartsWith("ConnectionStrings__Smartnet=", StringComparison.Ordinal))
        {
            return line["ConnectionStrings__Smartnet=".Length..].Trim();
        }
    }

    return null;
}

static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

static string SafeName(string title)
{
    var cleaned = new string(title.Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.' ? ch : '_').ToArray());
    return string.IsNullOrWhiteSpace(cleaned) ? "document" : cleaned.Trim();
}

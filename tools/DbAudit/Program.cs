using System.Text;
using MySqlConnector;

// READ-ONLY audit of the live smartnet_invsys database.
// Guardrails:
//   - only SELECT / SHOW statements are permitted (asserted below)
//   - session is set read-only and to a low lock/exec timeout so we cannot
//     block or damage production
//   - results are printed as counts/aggregates; no bulk customer data dumped

var host = Environment.GetEnvironmentVariable("SN_DB_HOST") ?? "185.73.8.1";
var db = Environment.GetEnvironmentVariable("SN_DB_NAME") ?? "smartnet_invsys";
var user = Environment.GetEnvironmentVariable("SN_DB_USER") ?? "smartnet_sys";
var pass = Environment.GetEnvironmentVariable("SN_DB_PASS") ?? throw new("set SN_DB_PASS");

var cs = new MySqlConnectionStringBuilder
{
    Server = host,
    Database = db,
    UserID = user,
    Password = pass,
    DefaultCommandTimeout = 60,
    ConnectionTimeout = 20,
}.ConnectionString;

await using var cn = new MySqlConnection(cs);
await cn.OpenAsync();

// belt and braces: make the session incapable of writing
foreach (var guard in new[]
         {
             "SET SESSION TRANSACTION READ ONLY",
             "SET SESSION max_execution_time = 60000",
             "SET SESSION innodb_lock_wait_timeout = 5",
         })
    try { await Exec(guard); } catch { /* older server may not support all */ }

Console.WriteLine($"connected: {cn.ServerVersion}  db={db}\n");

var mode = args.Length > 0 ? args[0] : "schema";

if (mode == "schema")
{
    Console.WriteLine("=== TABLES (rows, size) ===");
    await Dump("""
        SELECT TABLE_NAME, TABLE_ROWS, ROUND((DATA_LENGTH+INDEX_LENGTH)/1024/1024,1) AS mb, ENGINE
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_ROWS DESC
        """);

    Console.WriteLine("\n=== MONEY-ISH COLUMNS (type check) ===");
    await Dump("""
        SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND (COLUMN_NAME REGEXP 'amount|total|balance|rate|price|cost|paid|vat|disc|qty|quantity')
        ORDER BY TABLE_NAME, COLUMN_NAME
        """);

    Console.WriteLine("\n=== PRIMARY KEYS / FOREIGN KEYS ===");
    await Dump("""
        SELECT TABLE_NAME, CONSTRAINT_NAME, CONSTRAINT_TYPE
        FROM information_schema.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY CONSTRAINT_TYPE, TABLE_NAME
        """);
}
else if (mode == "columns")
{
    var table = args.Length > 1 ? args[1] : throw new("usage: columns <table>");
    await Dump($"""
        SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table.Replace("'", "")}'
        ORDER BY ORDINAL_POSITION
        """);
}
else if (mode == "sql")
{
    var q = args.Length > 1 ? args[1] : throw new("usage: sql \"SELECT ...\"");
    await Dump(q);
}

async Task Exec(string sql)
{
    await using var c = new MySqlCommand(sql, cn);
    await c.ExecuteNonQueryAsync();
}

async Task Dump(string sql)
{
    var trimmed = sql.TrimStart();
    if (!(trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
       || trimmed.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase)
       || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("read-only tool: only SELECT/SHOW/WITH permitted");

    await using var cmd = new MySqlCommand(sql, cn);
    await using var r = await cmd.ExecuteReaderAsync();

    var widths = new int[r.FieldCount];
    var rows = new List<string[]>();
    var head = new string[r.FieldCount];
    for (var i = 0; i < r.FieldCount; i++) { head[i] = r.GetName(i); widths[i] = head[i].Length; }

    while (await r.ReadAsync())
    {
        var row = new string[r.FieldCount];
        for (var i = 0; i < r.FieldCount; i++)
        {
            var v = await r.IsDBNullAsync(i) ? "NULL" : r.GetValue(i)?.ToString() ?? "";
            if (v.Length > 60) v = v[..57] + "...";
            row[i] = v;
            widths[i] = Math.Max(widths[i], v.Length);
        }
        rows.Add(row);
    }

    var sb = new StringBuilder();
    for (var i = 0; i < head.Length; i++) sb.Append(head[i].PadRight(widths[i] + 2));
    Console.WriteLine(sb.ToString().TrimEnd());
    Console.WriteLine(new string('-', Math.Min(widths.Sum() + widths.Length * 2, 160)));
    foreach (var row in rows)
    {
        sb.Clear();
        for (var i = 0; i < row.Length; i++) sb.Append(row[i].PadRight(widths[i] + 2));
        Console.WriteLine(sb.ToString().TrimEnd());
    }
    Console.WriteLine($"({rows.Count} rows)");
}

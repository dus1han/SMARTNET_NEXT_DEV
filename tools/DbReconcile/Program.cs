using System.Globalization;
using System.Text;
using MySqlConnector;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;

// -----------------------------------------------------------------------------------------------
// Money-correctness reconciliation (PHASE-5-PLAN slice 6).
//
// Recomputes a sample of EXISTING legacy invoices through the NEW tax engine (Smartnet.Domain,
// referenced verbatim — not re-implemented) and diffs the result against the figure the legacy app
// stored. The parent plan names this under "Money correctness": the legacy app did its arithmetic in
// binary `double`, the new engine in `decimal`, so small differences are expected. This tool measures
// how small, so the business can sign a policy off with numbers in front of it rather than a hunch.
//
// It writes NOTHING to the database and reads NOTHING but invoice figures (no customer PII). The
// session is set read-only and refuses to run against production, the same guard the API starts with.
//
// Usage:
//   dotnet run --project tools/DbReconcile [-- <sampleSize>]
//   ConnectionStrings__Smartnet=... dotnet run --project tools/DbReconcile
// The connection string is read from ConnectionStrings__Smartnet, or from the repo-root .env.
// -----------------------------------------------------------------------------------------------

const string ProductionDb = "smartnet_invsys";
var sampleSize = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 500;

var conn = ResolveConnectionString()
    ?? throw new InvalidOperationException(
        "ConnectionStrings__Smartnet is not set and no repo-root .env was found. " +
        "Copy .env.example to .env (dev copy) or set the env var.");

// The same guard the API starts with — never reconcile against the live database, whatever the config
// says. The dev copy is smartnet_invsys_dev; production is smartnet_invsys.
var csb = new MySqlConnectionStringBuilder(conn);
if (string.Equals(csb.Database, ProductionDb, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Refusing to run against the PRODUCTION database ({ProductionDb}). " +
        "Reconciliation runs against the dev copy (smartnet_invsys_dev).");
}

csb.DefaultCommandTimeout = 120;
csb.ConnectionTimeout = 20;

await using var cn = new MySqlConnection(csb.ConnectionString);
await cn.OpenAsync();

// Belt and braces: make the session incapable of writing, and unable to block anything for long.
foreach (var guard in new[]
         {
             "SET SESSION TRANSACTION READ ONLY",
             "SET SESSION max_execution_time = 120000",
             "SET SESSION innodb_lock_wait_timeout = 5",
         })
    try { await Exec(guard); } catch { /* older server may not support all */ }

Console.WriteLine($"connected: {cn.ServerVersion}  db={csb.Database}");
Console.WriteLine($"sampling up to {sampleSize} legacy invoices (most recent first)\n");

var engine = new TaxEngine();

// The most recent legacy invoices that carry a total — the ones closest to cutover, so the sample is
// weighted to invoices staff still recognise. data_origin defaults to 'legacy' at the DB, so a new
// invoice (the only 'new' rows) is excluded; every pre-adoption row and every row the legacy app still
// inserts is in scope.
var headers = new List<Header>();
await using (var cmd = cn.CreateCommand())
{
    cmd.CommandText = """
        SELECT id, invoiceno, totamount, novattotal, beforedisctot, discountper, vper
        FROM invoice_h
        WHERE COALESCE(NULLIF(TRIM(data_origin), ''), 'legacy') <> 'new'
          AND totamount IS NOT NULL AND TRIM(totamount) <> ''
        ORDER BY id DESC
        LIMIT @n
        """;
    cmd.Parameters.AddWithValue("@n", sampleSize);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        headers.Add(new Header(
            Id: r.GetInt64(0),
            InvoiceNo: r.IsDBNull(1) ? "" : r.GetString(1),
            TotAmount: Str(r, 2),
            NoVatTotal: Str(r, 3),
            BeforeDiscTot: Str(r, 4),
            DiscountPer: Str(r, 5),
            VPer: Str(r, 6)));
    }
}

// Load every sampled invoice's lines in ONE query, not one per invoice — the difference between a
// handful of round trips and thousands over a remote link. Grouped in memory by invoice number.
var linesByInvoice = new Dictionary<string, List<TaxLineInput>>(StringComparer.Ordinal);
var invoiceNos = headers.Select(h => h.InvoiceNo).Where(no => no.Length > 0).Distinct().ToList();
if (invoiceNos.Count > 0)
{
    await using var cmd = cn.CreateCommand();
    var names = invoiceNos.Select((_, i) => $"@i{i}").ToList();
    cmd.CommandText = $"SELECT inno, qty, rate FROM invoice_l WHERE inno IN ({string.Join(",", names)})";
    for (var i = 0; i < invoiceNos.Count; i++)
        cmd.Parameters.AddWithValue(names[i], invoiceNos[i]);

    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var inno = Str(r, 0);
        if (inno.Length == 0) continue;
        if (!linesByInvoice.TryGetValue(inno, out var list))
            linesByInvoice[inno] = list = new List<TaxLineInput>();
        list.Add(new TaxLineInput(ParseMoney(Str(r, 1)), ParseMoney(Str(r, 2)), DiscountPercent: 0m));
    }
}

var results = new List<Row>();
var skippedNoLines = 0;
var skippedUnparseable = 0;

foreach (var h in headers)
{
    if (!TryParseMoney(h.TotAmount, out var legacyTotal))
    {
        skippedUnparseable++;
        continue;
    }

    if (!linesByInvoice.TryGetValue(h.InvoiceNo, out var lines) || lines.Count == 0)
    {
        skippedNoLines++;
        continue;
    }

    // Feed the legacy stored rate and document discount straight in via a rate override — so we test
    // "does the new engine's decimal arithmetic reproduce the legacy figure given the same inputs?",
    // not "does the current rate table still cover this invoice's date?". Legacy invoice_l has no
    // per-line discount (the discount is document-level, discountper), so each line's discount is 0.
    var vper = ParseMoney(h.VPer);
    var request = new TaxCalculationRequest(
        DocumentDate: new DateOnly(2000, 1, 1), // unused: RateOverride short-circuits date resolution
        IsVatRegistered: true,                  // unused for the same reason
        Rounding: TaxRounding.PerLine,
        Lines: lines,
        AvailableRates: Array.Empty<TaxRate>(),
        DocumentDiscountPercent: ParseMoney(h.DiscountPer),
        RateOverride: new TaxRateOverride(null, $"legacy {vper}%", vper));

    var recomputed = engine.Calculate(request).Totals.Total;
    results.Add(new Row(h.InvoiceNo, legacyTotal, recomputed, lines.Count));
}

// --- Report -------------------------------------------------------------------------------------

if (results.Count == 0)
{
    Console.WriteLine("No reconcilable invoices found in the sample. Nothing to report.");
    return;
}

var diffs = results.Select(r => Math.Abs(r.Diff)).ToList();
var buckets = new (string Label, Func<decimal, bool> In)[]
{
    ("exact (0.00)",      d => d == 0m),
    ("<= 0.01",           d => d > 0m && d <= 0.01m),
    ("<= 0.05",           d => d > 0.01m && d <= 0.05m),
    ("<= 0.50",           d => d > 0.05m && d <= 0.50m),
    ("<= 5.00",           d => d > 0.50m && d <= 5.00m),
    ("> 5.00",            d => d > 5.00m),
};

Console.WriteLine($"reconciled {results.Count} invoices "
    + $"(skipped {skippedNoLines} with no lines, {skippedUnparseable} with an unparseable total)\n");
Console.WriteLine("distribution of |recomputed - legacy total|:");
foreach (var (label, inBucket) in buckets)
{
    var count = diffs.Count(inBucket);
    var pct = 100.0 * count / results.Count;
    Console.WriteLine($"  {label,-14} {count,6}  ({pct,5:0.0}%)");
}

var worst = results.OrderByDescending(r => Math.Abs(r.Diff)).Take(15).ToList();
Console.WriteLine("\nlargest differences:");
Console.WriteLine($"  {"invoice",-20} {"legacy",14} {"recomputed",14} {"diff",12} {"lines",6}");
foreach (var r in worst)
{
    Console.WriteLine($"  {r.InvoiceNo,-20} {r.Legacy,14:0.00} {r.Recomputed,14:0.00} {r.Diff,12:0.00} {r.Lines,6}");
}

var maxDiff = diffs.Max();
var withinAPenny = diffs.Count(d => d <= 0.01m);
Console.WriteLine($"\nmax |diff| = {maxDiff:0.00}; within one penny: {withinAPenny}/{results.Count} "
    + $"({100.0 * withinAPenny / results.Count:0.0}%)");

// The findings doc — written next to the other legacy analysis, so the sign-off has numbers in it.
var docPath = ResolveRepoPath("docs", "legacy-analysis", "RECONCILIATION.md");
await File.WriteAllTextAsync(docPath, BuildDoc(results, buckets, worst, skippedNoLines, skippedUnparseable));
Console.WriteLine($"\nwrote {docPath}");

// --- Helpers ------------------------------------------------------------------------------------

async Task Exec(string sql)
{
    await using var cmd = cn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static string Str(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString() ?? "";

// Legacy money is a varchar (Finding 5): it may carry thousands separators, stray spaces or be empty.
// Parse invariantly; treat anything unrecognisable as zero for a line, but fail loudly for a header
// total (a document with no readable total cannot be reconciled and is reported as skipped, not zero).
static decimal ParseMoney(string s) => TryParseMoney(s, out var v) ? v : 0m;

static bool TryParseMoney(string s, out decimal value)
{
    value = 0m;
    if (string.IsNullOrWhiteSpace(s)) return false;
    var cleaned = s.Replace(",", "").Replace(" ", "").Trim();
    return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
}

static string? ResolveConnectionString()
{
    var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Smartnet");
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

    // Fall back to the repo-root .env, the same file the API reads. We only pull the one key out; the
    // value never leaves this process (it is not printed).
    try
    {
        var envPath = ResolveRepoPath(".env");
        if (!File.Exists(envPath)) return null;
        foreach (var raw in File.ReadAllLines(envPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') || !line.Contains('=')) continue;
            var eq = line.IndexOf('=');
            var key = line[..eq].Trim();
            if (key == "ConnectionStrings__Smartnet")
                return line[(eq + 1)..].Trim().Trim('"');
        }
    }
    catch { /* best effort — env var is the supported path */ }

    return null;
}

// Walk up from the executable to the repo root (the directory that has a .git), so the tool can be run
// from anywhere (`dotnet run` sets the cwd to the project). Falls back to the current directory.
static string ResolveRepoPath(params string[] parts)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        dir = dir.Parent;
    var root = dir?.FullName ?? Directory.GetCurrentDirectory();
    return Path.Combine(new[] { root }.Concat(parts).ToArray());
}

static string BuildDoc(
    List<Row> results,
    (string Label, Func<decimal, bool> In)[] buckets,
    List<Row> worst,
    int skippedNoLines,
    int skippedUnparseable)
{
    var diffs = results.Select(r => Math.Abs(r.Diff)).ToList();
    var withinAPenny = diffs.Count(d => d <= 0.01m);
    var maxDiff = diffs.Max();

    var sb = new StringBuilder();
    sb.AppendLine("# Money reconciliation — legacy invoices vs the new tax engine");
    sb.AppendLine();
    sb.AppendLine("_Generated by `tools/DbReconcile` (PHASE-5-PLAN slice 6). Re-run it to refresh._");
    sb.AppendLine();
    sb.AppendLine("## Method");
    sb.AppendLine();
    sb.AppendLine("A sample of existing legacy invoices (`data_origin <> 'new'`, most recent first) is");
    sb.AppendLine("recomputed through the **same** `Smartnet.Domain.TaxEngine` the new app issues documents");
    sb.AppendLine("with, and the result is diffed against the total the legacy app stored in");
    sb.AppendLine("`invoice_h.totamount`. Each invoice is fed its own stored VAT rate (`vper`) and");
    sb.AppendLine("document discount (`discountper`) via a rate override, so the comparison isolates the one");
    sb.AppendLine("thing under test — **binary `double` (legacy) vs `decimal` (new) arithmetic** — rather than");
    sb.AppendLine("whether the current rate table still covers each invoice's date.");
    sb.AppendLine();
    sb.AppendLine("This is read-only; it writes nothing to the database and dumps no customer data.");
    sb.AppendLine();
    sb.AppendLine("## Result");
    sb.AppendLine();
    sb.AppendLine($"- **Reconciled:** {results.Count} invoices");
    sb.AppendLine($"- **Skipped:** {skippedNoLines} with no lines, {skippedUnparseable} with an unparseable total");
    sb.AppendLine($"- **Within one penny (|diff| ≤ 0.01):** {withinAPenny}/{results.Count} "
        + $"({100.0 * withinAPenny / results.Count:0.0}%)");
    sb.AppendLine($"- **Largest single difference:** {maxDiff:0.00}");
    sb.AppendLine();
    sb.AppendLine("### Distribution of `|recomputed − legacy total|`");
    sb.AppendLine();
    sb.AppendLine("| Band | Count | Share |");
    sb.AppendLine("|---|---:|---:|");
    foreach (var (label, inBucket) in buckets)
    {
        var count = diffs.Count(inBucket);
        sb.AppendLine($"| {label} | {count} | {100.0 * count / results.Count:0.0}% |");
    }
    sb.AppendLine();
    sb.AppendLine("### Largest differences");
    sb.AppendLine();
    sb.AppendLine("| Invoice | Legacy total | Recomputed | Diff | Lines |");
    sb.AppendLine("|---|---:|---:|---:|---:|");
    foreach (var r in worst)
        sb.AppendLine($"| {r.InvoiceNo} | {r.Legacy:0.00} | {r.Recomputed:0.00} | {r.Diff:0.00} | {r.Lines} |");
    sb.AppendLine();
    sb.AppendLine("## Proposed policy (for sign-off)");
    sb.AppendLine();
    sb.AppendLine("Per [LEGACY-DATA-POLICY.md](../LEGACY-DATA-POLICY.md) (decision 7), legacy figures are");
    sb.AppendLine("**left as-is** and imported as opening balances — this reconciliation does not rewrite");
    sb.AppendLine("them. The proposed reading of the numbers above:");
    sb.AppendLine();
    sb.AppendLine("- **Sub-penny differences are expected and accepted.** They are the residue of the legacy");
    sb.AppendLine("  app's `double` math; the new `decimal` figure is the correct one, and new documents");
    sb.AppendLine("  carry it. No remediation.");
    sb.AppendLine("- **Any difference above a materiality threshold** (proposed: > 1.00 in document currency)");
    sb.AppendLine("  is a legacy data defect, not a rounding artefact — it surfaces in the **Data Exceptions**");
    sb.AppendLine("  screen for the business to correct when it chooses, exactly as the duplicate payments and");
    sb.AppendLine("  negative balances do. It is **not** fixed silently by this migration.");
    sb.AppendLine();
    sb.AppendLine("_Sign-off: pending business review of the figures above._");
    return sb.ToString();
}

internal readonly record struct Header(
    long Id,
    string InvoiceNo,
    string TotAmount,
    string NoVatTotal,
    string BeforeDiscTot,
    string DiscountPer,
    string VPer);

internal readonly record struct Row(string InvoiceNo, decimal Legacy, decimal Recomputed, int Lines)
{
    public decimal Diff => Recomputed - Legacy;
}

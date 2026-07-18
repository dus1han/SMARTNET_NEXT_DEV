using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using QuestPDF.Companion;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Pdf;

// QuestPDF Community licence — free for this scale. Set once at startup.
QuestPDF.Settings.License = LicenseType.Community;

// Which company (1 = Smart Technologies default, 2 = Smart Net) and which job to preview — everything is
// pulled live from the dev DB so the layout shows real data, not placeholders.
var companyId = ArgValue("--company") is { } cv && long.TryParse(cv, out var cid) ? cid : 1;
var jobNo = ArgValue("--job") ?? (companyId == 2 ? "SNJ-1" : "STJ-5");

// `--doc quotation --id <n>` previews a quotation instead of a job sheet. It goes through the app's own
// QuotationRenderer rather than a second copy of the resolution logic, so what the Companion shows is
// what the endpoint would produce.
if (string.Equals(ArgValue("--doc"), "quotation", StringComparison.OrdinalIgnoreCase))
{
    await PreviewQuotationAsync();
    return;
}

if (string.Equals(ArgValue("--doc"), "po", StringComparison.OrdinalIgnoreCase))
{
    await PreviewPurchaseOrderAsync();
    return;
}

if (string.Equals(ArgValue("--doc"), "cn", StringComparison.OrdinalIgnoreCase))
{
    await PreviewCreditNoteAsync();
    return;
}

var company = await LoadCompany(companyId);
var job = await LoadJob(companyId, jobNo);

var model = new JobSheetModel(
    Logo: company.Logo,
    CompanyName: company.Name.ToUpperInvariant(),
    CompanyTagline: "Computer Sales & Service",
    CompanyContact: company.Contact,
    JobNo: job.JobNo,
    Date: job.Date,
    Status: job.Status,
    ClientName: job.ClientName,
    ClientAddress: job.ClientAddress,
    ClientPhone: job.ClientPhone,
    ContactPerson: job.ContactPerson,
    PreparedBy: job.PreparedBy,
    FaultDescription: job.Fault,
    Remarks: job.Remarks,
    Items: job.Items);

// Same selection as the app's JobSheetRenderer: company 2 → the Smart Net layout, else the default.
IDocument document = companyId == 2 ? new SmartNetJobSheetDocument(model) : new JobSheetDocument(model);

if (args.Contains("--companion"))
{
    Console.WriteLine($"Streaming {company.Name} · {job.JobNo} to QuestPDF Companion… (logo: {(company.Logo is null ? "none" : $"{company.Logo.Length} bytes")})");
    document.ShowInCompanion();
}
else
{
    Directory.CreateDirectory("out");
    var path = Path.GetFullPath(Path.Combine("out", "jobsheet-st.pdf"));
    document.GeneratePdf(path);
    document.GenerateImages(
        i => Path.GetFullPath(Path.Combine("out", $"jobsheet-st-{i}.png")),
        new ImageGenerationSettings { RasterDpi = 140 });
    Console.WriteLine($"Wrote {path} (+ PNG) — {company.Name} · {job.JobNo}");
}

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>Previews a quotation through the application's own renderer.</summary>
async Task PreviewQuotationAsync()
{
    var connection = ResolveConnectionString();

    var version = ServerVersion.AutoDetect(connection);

    await using var db = new SmartnetDbContext(
        new DbContextOptionsBuilder<SmartnetDbContext>().UseMySql(connection, version).Options);

    await using var legacyDb = new SmartnetLegacyDbContext(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>().UseMySql(connection, version).Options);

    // A specific quotation by id, or simply the newest one for the company — whichever exists, so the
    // tool is useful without first hunting for an id. Selected off the legacy columns for the same
    // reason the renderer reads them: an unadopted quotation has nothing in the typed ones.
    var company = companyId.ToString(CultureInfo.InvariantCulture);

    var id = ArgValue("--id") is { } raw && long.TryParse(raw, out var parsed)
        ? parsed
        : await legacyDb.QuotationHs
            .Where(q => q.Company == company)
            .OrderByDescending(q => q.Id)
            .Select(q => q.Id)
            .FirstOrDefaultAsync();

    if (id == 0)
    {
        Console.WriteLine($"No quotation found for company {companyId}. Pass --id <n> to name one.");
        return;
    }

    var document = await new QuotationRenderer(db, legacyDb).BuildAsync(id);

    if (document is null)
    {
        Console.WriteLine($"Quotation {id} does not exist.");
        return;
    }

    if (args.Contains("--companion"))
    {
        Console.WriteLine($"Streaming quotation {id} to QuestPDF Companion…");
        await document.ShowInCompanionAsync();
    }
    else
    {
        Directory.CreateDirectory("out");
        var path = Path.GetFullPath(Path.Combine("out", "quotation.pdf"));
        document.GeneratePdf(path);
        document.GenerateImages(
            i => Path.GetFullPath(Path.Combine("out", $"quotation-{i}.png")),
            new ImageGenerationSettings { RasterDpi = 140 });
        Console.WriteLine($"Wrote {path} (+ PNG) — quotation {id}");
    }
}

static async Task<(string Name, string Contact, byte[]? Logo)> LoadCompany(long companyId)
{
    await using var conn = await Open();

    string name = "Company";
    var address = new List<string>();
    var channels = new List<string>();
    var registration = new List<string>();
    await using (var cmd = new MySqlCommand(
        "SELECT name, address_line1, address_line2, city, country, phone, email, website, business_registration_no, vat_number, is_vat_registered FROM companies_m WHERE id=@id", conn))
    {
        cmd.Parameters.AddWithValue("@id", companyId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            name = r.IsDBNull(0) ? name : r.GetString(0).Trim();
            void Add(List<string> into, int i, string? prefix = null) { if (!r.IsDBNull(i) && r.GetString(i).Trim().Length > 0) into.Add((prefix ?? "") + r.GetString(i).Trim()); }
            Add(address, 1); Add(address, 2); Add(address, 3); Add(address, 4);
            if (!r.IsDBNull(5) && r.GetString(5).Trim().Length > 0) channels.Add("Tel: " + FormatPhone(r.GetString(5).Trim()));
            Add(channels, 6); Add(channels, 7);
            Add(registration, 8, "Reg. No: ");
            if (!r.IsDBNull(10) && r.GetBoolean(10)) Add(registration, 9, "VAT No: ");
        }
    }

    byte[]? logo = null;
    await using (var cmd = new MySqlCommand("SELECT data FROM company_logo WHERE company_id=@id", conn))
    {
        cmd.Parameters.AddWithValue("@id", companyId);
        if (await cmd.ExecuteScalarAsync() is byte[] bytes && bytes.Length > 0) logo = bytes;
    }

    // Address / contact channels / registration each on their own line so nothing crams.
    var lines = new[] { address, channels, registration }.Select(g => string.Join(" · ", g)).Where(l => l.Length > 0).ToList();
    var contact = lines.Count > 0 ? string.Join("\n", lines) : "[Set the address / phone / email in Settings → Company details]";
    return (name, contact, logo);
}

static async Task<JobData> LoadJob(long companyId, string jobNo)
{
    await using var conn = await Open();

    await using var cmd = new MySqlCommand(
        """
        SELECT j.jobno, j.jdate, j.contactperson, j.faultd, j.remarks, j.enteredby, j.jstat, j.items,
               c.cusname, c.cusadd, c.contactno
        FROM jobs_m j LEFT JOIN cus_m c ON c.cuscode = j.customer
        WHERE j.company=@company AND j.jobno=@job
        """, conn);
    cmd.Parameters.AddWithValue("@company", companyId.ToString(CultureInfo.InvariantCulture));
    cmd.Parameters.AddWithValue("@job", jobNo);

    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync())
    {
        return new JobData(jobNo, "—", "—", $"[Job {jobNo} not found]", "—", "—", "—", "—", "—", "", []);
    }

    string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i).Trim();

    var date = DateTime.TryParse(S(1), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
        : S(1);

    var clientAddress = S(9);
    var contactNo = S(10);

    return new JobData(
        JobNo: S(0),
        Date: date,
        Status: Title(S(6)),
        ClientName: S(8).Length > 0 ? S(8) : "—",
        ClientAddress: clientAddress.Length > 0 ? clientAddress : "—",
        ClientPhone: contactNo.Length > 0 ? FormatPhone(contactNo) : "—",
        ContactPerson: S(2).Length > 0 ? S(2) : "—",
        PreparedBy: S(5).Length > 0 ? S(5) : "—",
        Fault: S(3).Length > 0 ? S(3) : "—",
        Remarks: S(4),
        Items: ParseItems(S(7)));
}

// The legacy `items` column is free text: "Item : <desc> | Qty : <n> | Serial No : <s>", one item per line.
static List<JobItem> ParseItems(string raw)
{
    var items = new List<JobItem>();
    foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        string desc = "", qty = "", serial = "";
        foreach (var part in line.Split('|'))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var value = kv[1].Trim();
            if (key.StartsWith("item")) desc = value.StartsWith("Item :", StringComparison.OrdinalIgnoreCase) ? value[6..].Trim() : value;
            else if (key.StartsWith("qty")) qty = value;
            else if (key.StartsWith("serial")) serial = value;
        }
        if (desc.Length > 0 || qty.Length > 0 || serial.Length > 0)
            items.Add(new JobItem(desc, qty, serial));
    }
    return items;
}

static string Title(string s) =>
    s.Length == 0 ? s : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

static async Task<MySqlConnection> Open()
{
    var conn = new MySqlConnection(ResolveConnectionString());
    await conn.OpenAsync();
    return conn;
}

// Format a stored phone into a readable telephone number. Sri Lankan grouping: a mobile is 07X XXX XXXX
// nationally, or +94 7X XXX XXXX internationally. Anything unrecognised is returned unchanged.
static string FormatPhone(string raw)
{
    var digits = new string(raw.Where(char.IsDigit).ToArray());

    if (digits.StartsWith("94", StringComparison.Ordinal) && digits.Length == 11)
    {
        var n = digits[2..];
        return $"+94 {n[..2]} {n.Substring(2, 3)} {n.Substring(5, 4)}";
    }
    if (digits.Length == 10 && digits.StartsWith('0'))
    {
        return $"{digits[..3]} {digits.Substring(3, 3)} {digits.Substring(6, 4)}";
    }
    if (digits.Length == 9)
    {
        return $"0{digits[..2]} {digits.Substring(2, 3)} {digits.Substring(5, 4)}";
    }

    return raw;
}

static string ResolveConnectionString()
{
    var env = Environment.GetEnvironmentVariable("ConnectionStrings__Smartnet");
    if (!string.IsNullOrWhiteSpace(env)) return env;

    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var envFile = Path.Combine(root, ".env");
    foreach (var line in File.Exists(envFile) ? File.ReadAllLines(envFile) : [])
    {
        if (line.StartsWith("ConnectionStrings__Smartnet=", StringComparison.Ordinal))
            return line["ConnectionStrings__Smartnet=".Length..];
    }
    throw new InvalidOperationException("Could not find ConnectionStrings__Smartnet (env var or repo-root .env).");
}


/// <summary>Previews a purchase order through the application's own renderer.</summary>
async Task PreviewPurchaseOrderAsync()
{
    var connection = ResolveConnectionString();
    var version = ServerVersion.AutoDetect(connection);

    await using var db = new SmartnetDbContext(
        new DbContextOptionsBuilder<SmartnetDbContext>().UseMySql(connection, version).Options);

    await using var legacyDb = new SmartnetLegacyDbContext(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>().UseMySql(connection, version).Options);

    var company = companyId.ToString(CultureInfo.InvariantCulture);

    var id = ArgValue("--id") is { } raw && long.TryParse(raw, out var parsed)
        ? parsed
        : await legacyDb.PoHs
            .Where(p => p.Company == company)
            .OrderByDescending(p => p.Id)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();

    if (id == 0)
    {
        Console.WriteLine($"No purchase order found for company {companyId}. Pass --id <n> to name one.");
        return;
    }

    var document = await new PurchaseOrderRenderer(db, legacyDb).BuildAsync(id);

    if (document is null)
    {
        Console.WriteLine($"Purchase order {id} does not exist.");
        return;
    }

    if (args.Contains("--companion"))
    {
        Console.WriteLine($"Streaming purchase order {id} to QuestPDF Companion…");
        await document.ShowInCompanionAsync();
    }
    else
    {
        Directory.CreateDirectory("out");
        var path = Path.GetFullPath(Path.Combine("out", "purchase-order.pdf"));
        document.GeneratePdf(path);
        document.GenerateImages(
            i => Path.GetFullPath(Path.Combine("out", $"purchase-order-{i}.png")),
            new ImageGenerationSettings { RasterDpi = 140 });
        Console.WriteLine($"Wrote {path} (+ PNG) — purchase order {id}");
    }
}
/// <summary>Previews a credit note through the application's own renderer.</summary>
async Task PreviewCreditNoteAsync()
{
    var connection = ResolveConnectionString();
    var version = ServerVersion.AutoDetect(connection);

    await using var db = new SmartnetDbContext(
        new DbContextOptionsBuilder<SmartnetDbContext>().UseMySql(connection, version).Options);

    await using var legacyDb = new SmartnetLegacyDbContext(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>().UseMySql(connection, version).Options);

    var id = ArgValue("--id") is { } raw && long.TryParse(raw, out var parsed)
        ? parsed
        : await legacyDb.CnHs
            .Where(c => c.CompanyId == companyId)
            .OrderByDescending(c => c.Id)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

    if (id == 0)
    {
        Console.WriteLine($"No credit note found for company {companyId}. Pass --id <n> to name one.");
        return;
    }

    var document = await new CreditNoteRenderer(db, legacyDb).BuildAsync(id);

    if (document is null)
    {
        Console.WriteLine($"Credit note {id} does not exist.");
        return;
    }

    if (args.Contains("--companion"))
    {
        Console.WriteLine($"Streaming credit note {id} to QuestPDF Companion…");
        await document.ShowInCompanionAsync();
    }
    else
    {
        Directory.CreateDirectory("out");
        var path = Path.GetFullPath(Path.Combine("out", "credit-note.pdf"));
        document.GeneratePdf(path);
        document.GenerateImages(
            i => Path.GetFullPath(Path.Combine("out", $"credit-note-{i}.png")),
            new ImageGenerationSettings { RasterDpi = 140 });
        Console.WriteLine($"Wrote {path} (+ PNG) — credit note {id}");
    }
}

internal sealed record JobData(
    string JobNo, string Date, string Status, string ClientName, string ClientAddress, string ClientPhone,
    string ContactPerson, string PreparedBy, string Fault, string Remarks, List<JobItem> Items);

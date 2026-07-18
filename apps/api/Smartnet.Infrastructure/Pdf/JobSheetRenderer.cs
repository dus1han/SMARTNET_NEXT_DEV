using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// Renders a job card to a job-sheet PDF, resolving the job, its customer and its company (with logo) and
/// choosing that company's layout — the two companies print as visibly distinct documents (see
/// <see cref="SmartNetJobSheetDocument"/>). Every value is cleaned/formatted here so the templates just draw.
/// </summary>
public sealed class JobSheetRenderer : IJobSheetRenderer
{
    /// <summary>The seeded company that gets the Smart Net layout; every other company gets the default one.</summary>
    private const long SmartNetCompanyId = 2;

    static JobSheetRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public JobSheetRenderer(SmartnetDbContext db, SmartnetLegacyDbContext legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<byte[]?> RenderAsync(long jobId, CancellationToken cancellationToken = default)
    {
        var job = await _legacy.JobsMs
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            .ConfigureAwait(false);
        if (job is null)
        {
            return null;
        }

        // Project the three fields the sheet prints rather than materialising the whole row: cus_m carries
        // columns this document has no use for, and reading them is a needless dependency on their types.
        var customer = string.IsNullOrEmpty(job.Customer)
            ? null
            : await _legacy.CusMs
                .Where(c => c.Cuscode == job.Customer)
                .Select(c => new CustomerContact(c.Cusname, c.Cusadd, c.Contactno))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        long.TryParse(job.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId);
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        var logo = await _db.CompanyLogos
            .Where(l => l.CompanyId == companyId)
            .Select(l => l.Data)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var model = BuildModel(job, customer, company, logo);

        IDocument document = companyId == SmartNetCompanyId
            ? new SmartNetJobSheetDocument(model)
            : new JobSheetDocument(model);

        return document.GeneratePdf();
    }

    /// <summary>The customer fields the job sheet prints — all it reads from <c>cus_m</c>.</summary>
    private sealed record CustomerContact(string? Cusname, string? Cusadd, string? Contactno);

    private static JobSheetModel BuildModel(JobsM job, CustomerContact? customer, Company? company, byte[]? logo) => new(
        Logo: logo is { Length: > 0 } ? logo : null,
        CompanyName: Trim(company?.Name),
        CompanyTagline: "Computer Sales & Service",
        CompanyContact: CompanyHeader.Build(company),
        JobNo: Trim(job.Jobno),
        Date: FormatDate(job.Jdate),
        Status: Title(Trim(job.Jstat)),
        ClientName: Trim(customer?.Cusname),
        ClientAddress: Trim(customer?.Cusadd),
        ClientPhone: CompanyHeader.FormatPhone(Trim(customer?.Contactno)),
        ContactPerson: Trim(job.Contactperson),
        PreparedBy: Trim(job.Enteredby),
        FaultDescription: Trim(job.FaultD),
        Remarks: Trim(job.Remarks),
        Items: ParseItems(job.Items ?? string.Empty));


    // The legacy `items` column is free text: "Item : <desc> | Qty : <n> | Serial No : <s>", one item per line.
    private static List<JobItem> ParseItems(string raw)
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
                if (key.StartsWith("item", StringComparison.Ordinal))
                    desc = value.StartsWith("Item :", StringComparison.OrdinalIgnoreCase) ? value[6..].Trim() : value;
                else if (key.StartsWith("qty", StringComparison.Ordinal)) qty = value;
                else if (key.StartsWith("serial", StringComparison.Ordinal)) serial = value;
            }
            if (desc.Length > 0 || qty.Length > 0 || serial.Length > 0)
                items.Add(new JobItem(desc, qty, serial));
        }
        return items;
    }


    private static string FormatDate(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : Trim(raw);

    private static string Title(string s) =>
        s.Length == 0 ? s : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;
}

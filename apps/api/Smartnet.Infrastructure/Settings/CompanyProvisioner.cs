using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Settings;

/// <summary>What a new trading entity needs before it can raise its first document.</summary>
public interface ICompanyProvisioner
{
    Task<ProvisionedCompany> CreateAsync(NewCompany request, CancellationToken cancellationToken = default);
}

/// <param name="NumberPrefix">Applied to all nine document types; each editable afterwards.</param>
public sealed record NewCompany(
    string Name,
    bool IsVatRegistered,
    string? BusinessRegistrationNo,
    string NumberPrefix);

public sealed record ProvisionedCompany(
    long Id,
    string Name,
    int TaxRates,
    int NumberSeries,
    int EmailTemplates);

/// <summary>
/// Creates a company <b>and everything it cannot work without</b>, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// A row in <c>companies_m</c> on its own is a company that looks fine and cannot do anything. The two
/// existing companies were set up by <c>SettingsAndMultiCompany</c>, whose seeding runs
/// <c>FROM companies_m</c> — a one-shot cross join over the companies that existed on the day it ran.
/// Nothing re-runs it, so a company added later inherits none of it.
/// </para>
/// <para>What is missing, and what each absence actually does:</para>
/// <list type="bullet">
/// <item><b>A default tax rate.</b> The hard one. <c>TaxEngine.ResolveDocumentRate</c> throws
/// <c>TaxRateNotResolvableException</c> when no default is in force on the document's date, so a
/// VAT-registered company without one cannot raise an invoice, a quotation or a credit note — at all,
/// and the error surfaces at save rather than anywhere near the cause.</item>
/// <item><b>Nine numbering series.</b> One per <c>DocumentTypes.All</c>. Absent, numbering fails when a
/// document is saved. Seeded at 1 here, which would have been wrong for the legacy companies — they had
/// 2,500 invoices already and starting at 1 would have reissued numbers that are on printed paper — and
/// is exactly right for a company that has none.</item>
/// <item><b>Five email templates.</b> Absent, the company cannot email a document, <i>and cannot be
/// fixed from the UI</i>: the email-template API reads and updates, it has no insert.</item>
/// </list>
/// <para>
/// Business rules are not provisioned, and should not be: they are seeded globally with a null company
/// and the reader falls back to that row, so a new company inherits sane behaviour rather than a table
/// of nulls. Mail settings are not either — that needs a real SMTP password, which belongs in the
/// settings screen where it is encrypted, not in a creation form.
/// </para>
/// <para>
/// One transaction, because a half-provisioned company is worse than no company: it exists, it appears
/// in the switcher, and it fails at the till. Anything that goes wrong here leaves nothing behind.
/// </para>
/// </remarks>
public sealed class CompanyProvisioner : ICompanyProvisioner
{
    // The zero rate every company carries. Dated far enough back that no document a new company could
    // raise falls before it — a new company has no history, so any real date is later than this.
    private static readonly DateOnly ZeroRateFrom = new(2024, 1, 1);

    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public CompanyProvisioner(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<ProvisionedCompany> CreateAsync(
        NewCompany request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        // Not a unique index — companies_m is a legacy table and the old app still reads it, so adding
        // constraints to it is a separate decision. This is a courtesy check against the obvious
        // mistake of creating "Smart Net" twice; it races in theory, and in a table that gains a row
        // once a year that is not worth a lock.
        if (await _db.Companies
                .AnyAsync(c => c.Name == name && c.DeletedAt == null, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new CompanyAlreadyExistsException(name);
        }

        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var company = new Company
        {
            Name = name,
            IsVatRegistered = request.IsVatRegistered,
            BusinessRegistrationNo = string.IsNullOrWhiteSpace(request.BusinessRegistrationNo)
                ? null
                : request.BusinessRegistrationNo.Trim(),
        };

        _db.Companies.Add(company);

        // Saved before the rest, because everything below needs the generated id.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var taxRates = await TaxRatesForAsync(company.Id, request.IsVatRegistered, cancellationToken)
            .ConfigureAwait(false);
        _db.TaxRates.AddRange(taxRates);

        var series = DocumentTypes.All
            .Select(docType => new DocumentSeries
            {
                CompanyId = company.Id,
                DocType = docType,
                Prefix = request.NumberPrefix.Trim(),
                NextNumber = 1,
                // Zero, matching the house convention: the existing numbers run STI-999, STI-1214, and
                // padding to five would produce STI-01215, which matches nothing already filed.
                Padding = 0,
            })
            .ToList();

        _db.DocumentSeries.AddRange(series);

        var templates = EmailTemplateDefaults.All
            .Select(t => new EmailTemplate
            {
                CompanyId = company.Id,
                TemplateKey = t.Key,
                Subject = t.Subject,
                Body = t.Body,
            })
            .ToList();

        _db.EmailTemplates.AddRange(templates);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ProvisionedCompany(
            company.Id, company.Name, taxRates.Count, series.Count, templates.Count);
    }

    /// <summary>
    /// The rates a new company starts with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <b>VAT-registered</b> company inherits the rate the other VAT companies charge <i>today</i> — not
    /// a figure typed into the create form, because the rate is not a per-company decision (see
    /// <c>SetVatRate</c>). It is copied by name, percentage and start date, so the new entity is taxed
    /// identically to the others from day one. Any <i>scheduled future</i> rate is deliberately not copied:
    /// a new company joins at the rate in force now, and a pending change is applied to it when it lands, the
    /// same fan-out that set it for everyone else.
    /// </para>
    /// <para>
    /// An <b>unregistered</b> company gets only the zero rate, as its default. The engine forces 0% for such
    /// a company regardless, so a VAT row would be a line that lies about what it charges.
    /// </para>
    /// <para>
    /// Every company also carries a zero rate — a line may legitimately be zero-rated (an exempt item on an
    /// otherwise taxable invoice), and it cannot be picked if it does not exist. For a VAT company it is a
    /// non-default choice; for an unregistered one it is the default.
    /// </para>
    /// <para>
    /// The fallback — a hardcoded VAT rate when <i>no</i> VAT company exists to copy from — is for the very
    /// first VAT company only. On this system Smart Net already exists, so it is a safety net, not a path
    /// anyone travels.
    /// </para>
    /// </remarks>
    private async Task<List<TaxRate>> TaxRatesForAsync(
        long companyId,
        bool isVatRegistered,
        CancellationToken cancellationToken)
    {
        var rates = new List<TaxRate>
        {
            new()
            {
                CompanyId = companyId,
                Name = "Zero-rated",
                Percentage = 0m,
                EffectiveFrom = ZeroRateFrom,
                IsDefault = !isVatRegistered,
            },
        };

        if (isVatRegistered)
        {
            var reference = await CurrentVatRateAsync(cancellationToken).ConfigureAwait(false);

            rates.Insert(0, new TaxRate
            {
                CompanyId = companyId,
                Name = reference.Name,
                Percentage = reference.Percentage,
                EffectiveFrom = reference.EffectiveFrom,
                IsDefault = true,
            });
        }

        return rates;
    }

    /// <summary>
    /// The VAT default the existing VAT-registered companies charge today, to copy onto a new one.
    /// </summary>
    private async Task<(string Name, decimal Percentage, DateOnly EffectiveFrom)> CurrentVatRateAsync(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        var candidates = await _db.TaxRates
            .Where(t => t.DeletedAt == null && t.IsDefault && t.Percentage > 0)
            .Join(
                _db.Companies.Where(c => c.DeletedAt == null && c.IsVatRegistered),
                t => t.CompanyId,
                c => c.Id,
                (t, _) => t)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The one in force now, and if a change is already scheduled, the latest that has actually started —
        // the same "latest start on or before today" the tax engine uses to resolve a document.
        var inForce = candidates
            .Where(t => t.IsInForceOn(today))
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefault();

        return inForce is not null
            ? (inForce.Name, inForce.Percentage, inForce.EffectiveFrom)
            : ("VAT 18%", 18m, ZeroRateFrom); // the first-VAT-company fallback
    }
}

/// <summary>Two companies with the same name is a data-entry slip, not a business arrangement.</summary>
public sealed class CompanyAlreadyExistsException(string name)
    : Exception($"A company named '{name}' already exists.")
{
    public string Name { get; } = name;
}

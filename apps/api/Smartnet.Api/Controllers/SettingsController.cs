using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Settings;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Everything an administrator can change without a deployment.
/// </summary>
/// <remarks>
/// The phase's exit criterion: an admin changes the company header, the VAT rate, the invoice
/// prefix, the credit-limit policy and the SMTP settings from the UI — all of which currently
/// require a developer, a rebuild and a release, because they are constants in C# and in Crystal
/// Reports templates.
/// </remarks>
[ApiController]
[Route("api/settings")]
[RequirePermission(Permissions.SettingsManage)]
public sealed class SettingsController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly ICompanyContext _companies;
    private readonly IMailSender _mail;
    private readonly ICompanyProvisioner _provisioner;
    private readonly IDataProtector _protector;

    public SettingsController(
        SmartnetDbContext db,
        ICompanyContext companies,
        IMailSender mail,
        ICompanyProvisioner provisioner,
        IDataProtectionProvider protection)
    {
        _db = db;
        _companies = companies;
        _mail = mail;
        _provisioner = provisioner;

        // A named purpose, so a ciphertext from this protector cannot be decrypted by another —
        // an SMTP password must not be interchangeable with, say, a reset token.
        _protector = protection.CreateProtector("Smartnet.MailSettings.Password");
    }

    // --- Companies ---------------------------------------------------------------------------

    /// <summary>
    /// The companies the caller may switch between. Readable by anyone signed in — the company
    /// switcher in the shell needs it, and it exposes only names the user already works under.
    /// </summary>
    [HttpGet("/api/companies")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<CompanySummary>>> Companies(
        CancellationToken cancellationToken)
    {
        var accessible = _companies.Accessible;

        return Ok(await _db.Companies
            .Where(c => c.DeletedAt == null && accessible.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => new CompanySummary(c.Id, c.Name, c.IsVatRegistered))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Adds a trading entity, and everything it needs to be able to raise a document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dev_Admin only</b>, and the first endpoint in this codebase actually gated on that permission
    /// — until now it existed solely to satisfy every other policy implicitly. Adding a trading entity
    /// is a structural act, not an administrative one: it changes what the business *is*, everyone can
    /// then see it (company scoping is not an authorisation boundary — see ICompanyAccessService), and
    /// it is done perhaps once. The rest of the settings surface stays on settings.manage.
    /// </para>
    /// <para>
    /// The work is in <c>ICompanyProvisioner</c>, not here, because creating the row is the small part:
    /// a company without a default tax rate cannot raise any document at all, and one without numbering
    /// series or email templates fails later and less legibly. All of it commits together or not at all.
    /// </para>
    /// </remarks>
    [HttpPost("/api/companies")]
    [RequirePermission(Permissions.SystemDevAdmin)]
    [RequireChangeReason]
    public async Task<ActionResult<CompanyCreatedResponse>> CreateCompany(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        ProvisionedCompany created;

        try
        {
            created = await _provisioner.CreateAsync(
                new NewCompany(
                    request.Name,
                    request.IsVatRegistered,
                    request.BusinessRegistrationNo,
                    request.NumberPrefix),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CompanyAlreadyExistsException duplicate)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: duplicate.Message);
        }

        return Ok(new CompanyCreatedResponse(
            created.Id, created.Name, created.TaxRates, created.NumberSeries, created.EmailTemplates));
    }

    [HttpGet("company")]
    public async Task<ActionResult<CompanyProfile>> Company(CancellationToken cancellationToken)
    {
        var company = await ActiveCompany(cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return NotFound();
        }

        var hasLogo = await _db.CompanyLogos.AnyAsync(l => l.CompanyId == company.Id, cancellationToken).ConfigureAwait(false);
        return Ok(Profile(company, hasLogo));
    }

    /// <summary>The document header. Today it is hardcoded in the Crystal Reports templates.</summary>
    [HttpPut("company")]
    [RequireChangeReason]
    public async Task<ActionResult<CompanyProfile>> SaveCompany(
        CompanyProfile request,
        CancellationToken cancellationToken)
    {
        var company = await ActiveCompany(cancellationToken).ConfigureAwait(false);

        if (company is null)
        {
            return NotFound();
        }

        // CompanyProfile is both the read and the write shape, so the version the screen loaded comes
        // back on the same field it was read from.
        if (this.StaleEdit(company, request.RowVersion, "company profile") is { } stale)
        {
            return stale;
        }

        company.Name = request.Name;
        company.IsVatRegistered = request.IsVatRegistered;

        // A company that is not VAT-registered must not print a VAT number. Letting one linger in
        // the field after the flag is turned off is how it ends up on an invoice.
        company.VatNumber = request.IsVatRegistered ? request.VatNumber : null;
        company.BusinessRegistrationNo = request.BusinessRegistrationNo;

        company.AddressLine1 = request.AddressLine1;
        company.AddressLine2 = request.AddressLine2;
        company.City = request.City;
        company.Country = request.Country;
        company.Phone = request.Phone;
        company.Email = request.Email;
        company.Website = request.Website;
        company.BankName = request.BankName;
        company.BankBranch = request.BankBranch;
        company.BankAccountName = request.BankAccountName;
        company.BankAccountNumber = request.BankAccountNumber;
        company.BrandColour = request.BrandColour;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var hasLogo = await _db.CompanyLogos.AnyAsync(l => l.CompanyId == company.Id, cancellationToken).ConfigureAwait(false);
        return Ok(Profile(company, hasLogo));
    }

    // --- Company logo ------------------------------------------------------------------------

    private const long MaxLogoBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly HashSet<string> AllowedLogoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml",
    };

    /// <summary>The active company's logo image, or 204 if none. Served with its stored content type so the
    /// browser and the PDF renderer decode it correctly.</summary>
    [HttpGet("company/logo")]
    public async Task<IActionResult> CompanyLogo(CancellationToken cancellationToken)
    {
        if (_companies.Active is not { } companyId)
        {
            return NoContent();
        }

        var logo = await _db.CompanyLogos
            .FirstOrDefaultAsync(l => l.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        return logo is null ? NoContent() : File(logo.Data, logo.ContentType);
    }

    /// <summary>Uploads (or replaces) the active company's logo — a PNG/JPEG/GIF/WebP/SVG up to 2 MB.</summary>
    [HttpPost("company/logo")]
    [RequestSizeLimit(MaxLogoBytes + 4096)]
    public async Task<IActionResult> UploadCompanyLogo(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("No file was uploaded.");
        }
        if (file.Length > MaxLogoBytes)
        {
            return BadRequest("The logo must be 2 MB or smaller.");
        }
        if (!AllowedLogoTypes.Contains(file.ContentType))
        {
            return BadRequest("The logo must be a PNG, JPEG, GIF, WebP or SVG image.");
        }
        if (_companies.Active is not { } companyId)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var logo = await _db.CompanyLogos
            .FirstOrDefaultAsync(l => l.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (logo is null)
        {
            logo = new CompanyLogo { CompanyId = companyId };
            _db.CompanyLogos.Add(logo);
        }

        logo.ContentType = file.ContentType;
        logo.Data = bytes;
        logo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Removes the active company's logo.</summary>
    [HttpDelete("company/logo")]
    public async Task<IActionResult> DeleteCompanyLogo(CancellationToken cancellationToken)
    {
        if (_companies.Active is not { } companyId)
        {
            return NoContent();
        }

        var logo = await _db.CompanyLogos
            .FirstOrDefaultAsync(l => l.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (logo is not null)
        {
            _db.CompanyLogos.Remove(logo);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return NoContent();
    }

    // --- Business rules ----------------------------------------------------------------------

    /// <summary>
    /// The seven rules from DEVELOPMENT-PLAN §Settings surface, each of which is a constant in the
    /// legacy C# today.
    /// </summary>
    [HttpGet("business-rules")]
    public async Task<ActionResult<IReadOnlyList<BusinessRule>>> GetBusinessRules(
        CancellationToken cancellationToken)
    {
        var companyId = _companies.Active;

        var stored = await _db.AppSettings
            .Where(s => s.CompanyId == null || s.CompanyId == companyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A company-specific value overrides the global one. Reading the global as a fallback means
        // a new company inherits sane behaviour rather than a table of nulls.
        var effective = BusinessRules.Defaults.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);

        foreach (var setting in stored.Where(s => s.CompanyId is null))
        {
            effective[setting.Key] = setting.Value;
        }

        foreach (var setting in stored.Where(s => s.CompanyId == companyId))
        {
            effective[setting.Key] = setting.Value;
        }

        // Only this company's own rows are ever written, so theirs is the version the save checks. A key
        // with no row here is shown as version 0 — "I saw no row of our own for this" — and saving it
        // creates one, or is refused if somebody else created it first.
        var ours = stored
            .Where(s => s.CompanyId == companyId)
            .ToDictionary(s => s.Key, s => s.RowVersion, StringComparer.Ordinal);

        return Ok(effective
            .Where(pair => BusinessRules.IsKnown(pair.Key))
            .Select(pair => new BusinessRule(pair.Key, pair.Value, ours.GetValueOrDefault(pair.Key)))
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToList());
    }

    [HttpPut("business-rules")]
    [RequireChangeReason]
    public async Task<IActionResult> SaveBusinessRules(
        BusinessRule[] rules,
        CancellationToken cancellationToken)
    {
        var companyId = _companies.Active;

        if (companyId is null)
        {
            return Forbid();
        }

        foreach (var rule in rules)
        {
            var existing = await _db.AppSettings
                .FirstOrDefaultAsync(
                    s => s.CompanyId == companyId && s.Key == rule.Key,
                    cancellationToken)
                .ConfigureAwait(false);

            // Per rule, because that is the grain the rows have. Two administrators changing *different*
            // rules do not conflict at all and must not be made to; two changing the same one do, and
            // the second used to win silently.
            //
            // Version 0 means the caller saw no row of this company's own. Finding one now means somebody
            // created it in between — the caller's "off" is being applied to a setting they never read.
            if (existing is null)
            {
                if (rule.RowVersion != 0)
                {
                    return Stale(rule.Key);
                }

                _db.AppSettings.Add(new AppSetting
                {
                    CompanyId = companyId,
                    Key = rule.Key,
                    Value = rule.Value,
                });
            }
            else
            {
                if (existing.RowVersion != rule.RowVersion)
                {
                    return Stale(rule.Key);
                }

                existing.Value = rule.Value;
            }
        }

        // Audited automatically, with the reason: turning credit-limit enforcement on is exactly
        // the kind of change somebody will want explained three months later.
        //
        // Nothing above has been written yet — SaveChanges is the first write — so a rule refused
        // part-way through leaves none of the others applied either. Half-saved business rules would be
        // worse than a refused save: nobody would know which half.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>A business rule somebody else changed while this screen was open.</summary>
    private ObjectResult Stale(string key) => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title:
            $"Someone else changed the '{key}' business rule while you were editing. Reload to see "
            + "their version, then make your changes again. Nothing has been saved.");

    // --- Tax rates ---------------------------------------------------------------------------

    [HttpGet("tax-rates")]
    public async Task<ActionResult<IReadOnlyList<TaxRateDto>>> TaxRates(CancellationToken cancellationToken) =>
        Ok(await _db.TaxRates
            .Where(t => t.CompanyId == _companies.Active && t.DeletedAt == null)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .Select(t => new TaxRateDto(
                t.Id, t.Name, t.Percentage, t.EffectiveFrom, t.EffectiveTo, t.IsDefault))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Set the business VAT rate — applied to every VAT-registered company at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dev_Admin, and deliberately not company-scoped.</b> VAT is a national rate: when it changes it
    /// changes for every registered entity on the same day, so this fans the new rate out across all
    /// VAT-registered companies as their default from <see cref="SetVatRateRequest.EffectiveFrom"/>.
    /// Unregistered companies are untouched — the engine taxes them at 0% regardless, and they carry only a
    /// zero rate.
    /// </para>
    /// <para>
    /// It does not close off the previous rate. Adding "20% from January" leaves the current rate open-ended
    /// and both stay default: the engine resolves each document against the default with the latest start on
    /// or before its date, so the old rate governs everything before January and the new one from January on.
    /// A single <c>SaveChanges</c> makes the whole fan-out atomic — no company is left a rate behind.
    /// </para>
    /// </remarks>
    [HttpPost("vat-rate")]
    [RequirePermission(Permissions.SystemDevAdmin)]
    [RequireChangeReason]
    public async Task<ActionResult<VatRateAppliedResponse>> SetVatRate(
        SetVatRateRequest request,
        CancellationToken cancellationToken)
    {
        var vatCompanies = await _db.Companies
            .Where(c => c.DeletedAt == null && c.IsVatRegistered)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var companyId in vatCompanies)
        {
            var rate = new TaxRate
            {
                CompanyId = companyId,
                Name = request.Name,
                Percentage = request.Percentage,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null,
                IsDefault = true,
            };

            _db.TaxRates.Add(rate);
            await ClearOtherDefaults(companyId, rate, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new VatRateAppliedResponse(vatCompanies.Count));
    }

    /// <summary>Shift when one company adopts its rate — the only per-company tax edit.</summary>
    /// <remarks>
    /// The rate and percentage are business-wide (set through <see cref="SetVatRate"/>); a company gets to
    /// vary only <i>when</i> it starts, for the case where one entity changed its systems on a different day.
    /// Moving a document's rate does not touch any document already raised — Phase 5 snapshots the rate onto
    /// each line at save — so this affects tomorrow's documents, not last year's. Dev_Admin, like setting it.
    /// </remarks>
    [HttpPut("tax-rates/{id:long}")]
    [RequirePermission(Permissions.SystemDevAdmin)]
    [RequireChangeReason]
    public async Task<IActionResult> UpdateTaxRateFrom(
        long id,
        UpdateTaxRateFromRequest request,
        CancellationToken cancellationToken)
    {
        var rate = await _db.TaxRates
            .FirstOrDefaultAsync(
                t => t.Id == id && t.CompanyId == _companies.Active && t.DeletedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return NotFound();
        }

        rate.EffectiveFrom = request.EffectiveFrom;

        // Only re-check the one-default-per-start-date invariant; name, percentage and the default flag are
        // left exactly as they were, because none of them is editable here.
        if (rate.IsDefault)
        {
            await ClearOtherDefaults(rate.CompanyId, rate, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- Mail --------------------------------------------------------------------------------

    [HttpGet("mail")]
    public async Task<ActionResult<MailSettingsResponse>> Mail(CancellationToken cancellationToken)
    {
        var settings = await MailFor(cancellationToken).ConfigureAwait(false);

        if (settings is null)
        {
            // Nothing configured yet — deliberately not seeded, because seeding it would have
            // meant putting an SMTP password in a migration.
            return Ok(new MailSettingsResponse(
                Host: string.Empty, Port: 587, UseSsl: true, Username: null,
                HasPassword: false, FromAddress: null, FromName: null, ReplyTo: null, Bcc: null,
                SendEnabled: false, DailyLimit: 0));
        }

        return Ok(new MailSettingsResponse(
            settings.Host,
            settings.Port,
            settings.UseSsl,
            settings.Username,

            // The one thing the client learns about the password: whether there is one.
            HasPassword: !string.IsNullOrEmpty(settings.PasswordEncrypted),

            settings.FromAddress,
            settings.FromName,
            settings.ReplyTo,
            settings.Bcc,
            settings.SendEnabled,
            settings.DailyLimit));
    }

    [HttpPut("mail")]
    [RequireChangeReason]
    public async Task<IActionResult> SaveMail(
        SaveMailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = _companies.Active;

        if (companyId is null)
        {
            return Forbid();
        }

        var settings = await MailFor(cancellationToken).ConfigureAwait(false);

        if (settings is null)
        {
            settings = new MailSettings { CompanyId = companyId.Value, Host = request.Host };
            _db.MailSettings.Add(settings);
        }

        settings.Host = request.Host;
        settings.Port = request.Port;
        settings.UseSsl = request.UseSsl;
        settings.Username = request.Username;
        settings.FromAddress = request.FromAddress;
        settings.FromName = request.FromName;
        settings.ReplyTo = request.ReplyTo;
        settings.Bcc = request.Bcc;
        settings.SendEnabled = request.SendEnabled;
        settings.DailyLimit = request.DailyLimit;

        // A null password means "leave it alone" — which is what the form sends when the admin
        // changes the port and does not retype the password into a field showing ••••••.
        if (!string.IsNullOrEmpty(request.Password))
        {
            settings.PasswordEncrypted = _protector.Protect(request.Password);
        }

        // The interceptor audits this. PasswordEncrypted is on the redaction list, so the log
        // records that it changed and never what it changed to.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Sends a test message.
    /// </summary>
    /// <remarks>
    /// Without this, a misconfigured mail server is discovered when a customer says they never got
    /// their invoice — which, since nothing is logged either, is also the first anyone hears of it.
    /// </remarks>
    [HttpPost("mail/test")]
    public async Task<IActionResult> SendTest(
        SendTestEmailRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await MailFor(cancellationToken).ConfigureAwait(false);

        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Mail is not configured yet. Save the settings first.");
        }

        var password = string.IsNullOrEmpty(settings.PasswordEncrypted)
            ? null
            : _protector.Unprotect(settings.PasswordEncrypted);

        var result = await _mail
            .SendTestAsync(settings, password, request.To, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Sent)
        {
            // The provider's error goes to the administrator, who is the person who can act on it.
            // It is not a customer-facing message, and it does not leak the password.
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The mail server rejected the message.",
                detail: result.Error);
        }

        return NoContent();
    }

    // --- Email templates ---------------------------------------------------------------------

    [HttpGet("email-templates")]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateDto>>> EmailTemplates(
        CancellationToken cancellationToken) =>
        Ok(await _db.EmailTemplates
            .Where(t => t.CompanyId == _companies.Active && t.DeletedAt == null)
            .OrderBy(t => t.TemplateKey)
            .Select(t => new EmailTemplateDto(t.Id, t.TemplateKey, t.Subject, t.Body))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

    [HttpPut("email-templates/{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> SaveEmailTemplate(
        long id,
        SaveEmailTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _db.EmailTemplates
            .FirstOrDefaultAsync(
                t => t.Id == id && t.CompanyId == _companies.Active && t.DeletedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return NotFound();
        }

        template.Subject = request.Subject;
        template.Body = request.Body;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<Company?> ActiveCompany(CancellationToken cancellationToken) => _db.Companies
        .FirstOrDefaultAsync(c => c.Id == _companies.Active && c.DeletedAt == null, cancellationToken);

    private Task<MailSettings?> MailFor(CancellationToken cancellationToken) => _db.MailSettings
        .FirstOrDefaultAsync(m => m.CompanyId == _companies.Active, cancellationToken);

    /// <summary>
    /// Keep at most one default per <b>start date</b>. A new default supersedes an existing one only if the
    /// two begin on the same day.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole mechanism behind scheduling a rate change, and it is subtler than "one default per
    /// company". Two defaults that begin on <i>different</i> dates coexist by design: the engine resolves a
    /// document against the default with the latest <c>EffectiveFrom</c> on or before that document's date
    /// (<c>TaxEngine.ResolveDocumentRate</c>), so "18% from 2024, 20% from 2027" schedules itself — 18%
    /// governs everything before 2027, 20% from 2027 on — with no end date written onto the 18% and no
    /// clearing of its flag.
    /// </para>
    /// <para>
    /// The earlier version cleared every <i>overlapping</i> default, which read as correct and was not.
    /// Live's 18% is open-ended, so a scheduled 20% overlaps it, so adding the 20% cleared the 18% — and the
    /// engine, finding no default in force before 2027, threw <c>TaxRateNotResolvableException</c> on every
    /// document. The only case that genuinely must not persist is two defaults sharing a start date, where
    /// "the latest" is a coin toss; that is exactly, and only, what this clears.
    /// </para>
    /// </remarks>
    private async Task ClearOtherDefaults(long companyId, TaxRate keeping, CancellationToken cancellationToken)
    {
        var sameStart = await _db.TaxRates
            .Where(t => t.CompanyId == companyId
                && t.IsDefault
                && t.DeletedAt == null
                && t.EffectiveFrom == keeping.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var other in sameStart.Where(t => !ReferenceEquals(t, keeping)))
        {
            other.IsDefault = false;
        }
    }

    private static CompanyProfile Profile(Company c, bool hasLogo) => new(
        c.Id, c.Name, c.IsVatRegistered, c.VatNumber, c.BusinessRegistrationNo,
        c.AddressLine1, c.AddressLine2, c.City, c.Country,
        c.Phone, c.Email, c.Website,
        c.BankName, c.BankBranch, c.BankAccountName, c.BankAccountNumber,
        c.BrandColour, hasLogo,
        // The version the settings screen echoes back, so two administrators cannot overwrite each other.
        c.RowVersion);
}

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
    private readonly IDataProtector _protector;

    public SettingsController(
        SmartnetDbContext db,
        ICompanyContext companies,
        IMailSender mail,
        IDataProtectionProvider protection)
    {
        _db = db;
        _companies = companies;
        _mail = mail;

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

        return Ok(effective
            .Where(pair => BusinessRules.IsKnown(pair.Key))
            .Select(pair => new BusinessRule(pair.Key, pair.Value))
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

            if (existing is null)
            {
                _db.AppSettings.Add(new AppSetting
                {
                    CompanyId = companyId,
                    Key = rule.Key,
                    Value = rule.Value,
                });
            }
            else
            {
                existing.Value = rule.Value;
            }
        }

        // Audited automatically, with the reason: turning credit-limit enforcement on is exactly
        // the kind of change somebody will want explained three months later.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

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

    [HttpPost("tax-rates")]
    [RequireChangeReason]
    public async Task<ActionResult<TaxRateDto>> CreateTaxRate(
        SaveTaxRateRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = _companies.Active;

        if (companyId is null)
        {
            return Forbid();
        }

        var rate = new TaxRate
        {
            CompanyId = companyId.Value,
            Name = request.Name,
            Percentage = request.Percentage,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsDefault = request.IsDefault,
        };

        _db.TaxRates.Add(rate);

        if (request.IsDefault)
        {
            await ClearOtherDefaults(companyId.Value, rate, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new TaxRateDto(
            rate.Id, rate.Name, rate.Percentage, rate.EffectiveFrom, rate.EffectiveTo, rate.IsDefault));
    }

    /// <remarks>
    /// Editing a rate does NOT change any document that used it: Phase 5 snapshots the rate onto
    /// each line at save. Changing 18% to 20% here affects tomorrow's invoices, not last year's —
    /// which is exactly what the legacy system gets wrong, because it re-resolves the rate at print
    /// time and reprints old invoices with today's tax on them.
    /// </remarks>
    [HttpPut("tax-rates/{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> UpdateTaxRate(
        long id,
        SaveTaxRateRequest request,
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

        rate.Name = request.Name;
        rate.Percentage = request.Percentage;
        rate.EffectiveFrom = request.EffectiveFrom;
        rate.EffectiveTo = request.EffectiveTo;
        rate.IsDefault = request.IsDefault;

        if (request.IsDefault)
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

    /// <summary>Exactly one default rate per company, or "the default" means nothing.</summary>
    private async Task ClearOtherDefaults(long companyId, TaxRate keeping, CancellationToken cancellationToken)
    {
        var others = await _db.TaxRates
            .Where(t => t.CompanyId == companyId && t.IsDefault && t.DeletedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var other in others.Where(t => !ReferenceEquals(t, keeping)))
        {
            other.IsDefault = false;
        }
    }

    private static CompanyProfile Profile(Company c, bool hasLogo) => new(
        c.Id, c.Name, c.IsVatRegistered, c.VatNumber, c.BusinessRegistrationNo,
        c.AddressLine1, c.AddressLine2, c.City, c.Country,
        c.Phone, c.Email, c.Website,
        c.BankName, c.BankBranch, c.BankAccountName, c.BankAccountNumber,
        c.BrandColour, hasLogo);
}

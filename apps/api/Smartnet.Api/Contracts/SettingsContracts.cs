using FluentValidation;
using Smartnet.Domain.Settings;

namespace Smartnet.Api.Contracts;

public sealed record CompanySummary(long Id, string Name, bool IsVatRegistered);

public sealed record CompanyProfile(
    long Id,
    string Name,
    bool IsVatRegistered,
    string? VatNumber,
    string? BusinessRegistrationNo,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    string? Website,
    string? BankName,
    string? BankBranch,
    string? BankAccountName,
    string? BankAccountNumber,
    string? BrandColour,
    bool HasLogo);

public sealed record BusinessRule(string Key, string Value);

public sealed record TaxRateDto(
    long Id,
    string Name,
    decimal Percentage,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsDefault);

/// <summary>
/// The business VAT rate, set once and applied to every VAT-registered company at once.
/// </summary>
/// <remarks>
/// VAT is a national rate, not a per-company setting: when it changes, it changes for every registered
/// entity on the same day. So this carries no company — the endpoint fans it out across all VAT-registered
/// companies as the new default from <see cref="EffectiveFrom"/>. Individual companies can then have only
/// their adoption date nudged (see <see cref="UpdateTaxRateFromRequest"/>); the rate and percentage are the
/// same everywhere by construction.
/// </remarks>
public sealed record SetVatRateRequest(
    string Name,
    decimal Percentage,
    DateOnly EffectiveFrom);

/// <summary>How many companies a VAT-rate change touched.</summary>
public sealed record VatRateAppliedResponse(int CompaniesAffected);

/// <summary>
/// Shift when one company's rate starts — the only thing editable per company.
/// </summary>
/// <remarks>
/// The rate's name and percentage are business-wide and set through <see cref="SetVatRateRequest"/>; what a
/// single company gets to vary is <i>when it adopts</i> that rate, e.g. a company that changed its systems a
/// month later. So the per-company edit carries a date and nothing else.
/// </remarks>
public sealed record UpdateTaxRateFromRequest(DateOnly EffectiveFrom);

/// <summary>
/// A new trading entity, with the minimum it needs to be able to raise a document.
/// </summary>
/// <remarks>
/// <para>
/// The fields beyond the name are here because a bare <c>companies_m</c> row is a company that cannot
/// invoice. The two existing companies were provisioned by a one-shot migration that cross-joined over
/// the companies present when it ran; nothing re-runs it, and there is no create endpoint for email
/// templates at all. So creation provisions, in one transaction — see <c>CompanyProvisioner</c>.
/// </para>
/// <para>
/// <b>No VAT detail is asked for — the tick is enough.</b> A VAT-registered company inherits the rate the
/// other VAT companies charge today; an unregistered one gets a zero rate and nothing else. Either way the
/// rate is not something to re-type per company, because it is not per-company. The rest of the profile
/// (address, bank, logo, VAT number) is edited on the settings screen afterwards.
/// </para>
/// </remarks>
/// <param name="NumberPrefix">
/// The prefix its document numbers carry, e.g. <c>SS-</c>. A template rather than a literal — see
/// <c>DocumentNumberFormat</c> — so <c>{YY}{MON}_SS_</c> is equally valid. Applied to all nine document
/// types; each is editable afterwards under Numbering.
/// </param>
public sealed record CreateCompanyRequest(
    string Name,
    bool IsVatRegistered,
    string? BusinessRegistrationNo,
    string NumberPrefix);

/// <summary>What was created, and what was provisioned alongside it.</summary>
public sealed record CompanyCreatedResponse(
    long Id,
    string Name,
    int TaxRatesCreated,
    int NumberSeriesCreated,
    int EmailTemplatesCreated);

/// <summary>
/// Mail settings as the client is allowed to see them.
/// </summary>
/// <remarks>
/// <b>There is no password field, and that is the point.</b> ISSUES A2 — the SMTP password is
/// currently <c>Admin@2023##</c>, written into two controllers and shipped with the source. The
/// replacement is encrypted at rest and write-only: <see cref="HasPassword"/> tells the UI whether
/// to render <c>••••••</c>, and nothing returns the value itself. The only reason to read a stored
/// SMTP password back out over an API is to steal it.
/// </remarks>
public sealed record MailSettingsResponse(
    string Host,
    int Port,
    bool UseSsl,
    string? Username,
    bool HasPassword,
    string? FromAddress,
    string? FromName,
    string? ReplyTo,
    string? Bcc,
    bool SendEnabled,
    int DailyLimit);

/// <param name="Password">
/// Null leaves the stored password exactly as it is — which is what the settings screen sends when
/// the administrator edits the port and does not retype the password. A value replaces it. Neither
/// is ever read back.
/// </param>
public sealed record SaveMailSettingsRequest(
    string Host,
    int Port,
    bool UseSsl,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName,
    string? ReplyTo,
    string? Bcc,
    bool SendEnabled,
    int DailyLimit);

public sealed record SendTestEmailRequest(string To);

public sealed record EmailTemplateDto(long Id, string TemplateKey, string Subject, string Body);

public sealed record SaveEmailTemplateRequest(string Subject, string Body);

// --- Validators ------------------------------------------------------------------------------

public sealed class BusinessRuleValidator : AbstractValidator<BusinessRule>
{
    public BusinessRuleValidator()
    {
        RuleFor(r => r.Key)
            .Must(BusinessRules.IsKnown)
            // A settings table anyone can invent a key in becomes a junk drawer nothing reads.
            .WithMessage("'{PropertyValue}' is not a known business rule.");

        RuleFor(r => r.Value).NotNull().MaximumLength(500);

        RuleFor(r => r.Value)
            .Must(v => v is BusinessRules.RoundPerLine or BusinessRules.RoundPerDocument)
            .When(r => r.Key == BusinessRules.VatRoundingMode)
            .WithMessage("VAT rounding must be 'line' or 'document'.");
    }
}

public sealed class SetVatRateRequestValidator : AbstractValidator<SetVatRateRequest>
{
    public SetVatRateRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(64);

        // A negative tax rate is not a discount, it is a bug.
        RuleFor(r => r.Percentage).InclusiveBetween(0m, 100m);
    }
}

public sealed class CreateCompanyRequestValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.BusinessRegistrationNo).MaximumLength(64);

        // Without a prefix every document in the new company is a bare number, indistinguishable at a
        // glance from the other company's. It is editable afterwards, but it should never start absent.
        RuleFor(r => r.NumberPrefix).NotEmpty().MaximumLength(32);

        // No VAT fields to validate — a VAT-registered company inherits the rate the others charge, and an
        // unregistered one has none. The tick is the whole of the VAT decision.
    }
}

public sealed class SaveMailSettingsRequestValidator : AbstractValidator<SaveMailSettingsRequest>
{
    public SaveMailSettingsRequestValidator()
    {
        RuleFor(r => r.Host).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Port).InclusiveBetween(1, 65535);
        RuleFor(r => r.FromAddress).EmailAddress().When(r => !string.IsNullOrWhiteSpace(r.FromAddress));
        RuleFor(r => r.ReplyTo).EmailAddress().When(r => !string.IsNullOrWhiteSpace(r.ReplyTo));
        RuleFor(r => r.DailyLimit).GreaterThanOrEqualTo(0);
    }
}

public sealed class SendTestEmailRequestValidator : AbstractValidator<SendTestEmailRequest>
{
    public SendTestEmailRequestValidator() => RuleFor(r => r.To).NotEmpty().EmailAddress();
}

public sealed class SaveEmailTemplateRequestValidator : AbstractValidator<SaveEmailTemplateRequest>
{
    public SaveEmailTemplateRequestValidator()
    {
        RuleFor(r => r.Subject).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Body).NotEmpty();
    }
}

// --- Document numbering ------------------------------------------------------------------------

/// <param name="Prefix">A template: "STI-" or "{YY}{MON}_SNIN_". See DocumentNumberFormat.</param>
/// <param name="Example">What the next document will actually be called. The useful field.</param>
public sealed record DocumentSeriesDto(
    long Id,
    string DocType,
    string Prefix,
    long NextNumber,
    int Padding,
    string Example);

/// <remarks>
/// There is no NextNumber here, deliberately. Typing a counter into a settings form is how somebody
/// reissues invoice 1200 by accident. The counter moves only by allocating a document, or by the
/// initialiser — which cannot move it backwards.
/// </remarks>
public sealed record SaveDocumentSeriesRequest(string Prefix, int Padding);

public sealed record PreviewNumberRequest(string Prefix, long NextNumber, int Padding);

/// <param name="Now">The next number, as of today.</param>
/// <param name="NextMonth">
/// The one after, a month from now. Shown so that a prefix which rolls over — and one which does
/// not — are both plainly visible before they are saved.
/// </param>
public sealed record NumberPreview(string Now, string NextMonth);

public sealed class SaveDocumentSeriesRequestValidator : AbstractValidator<SaveDocumentSeriesRequest>
{
    public SaveDocumentSeriesRequestValidator()
    {
        RuleFor(r => r.Prefix).MaximumLength(32);

        // Padding beyond the width of a real number just pads. Zero is the legacy behaviour.
        RuleFor(r => r.Padding).InclusiveBetween(0, 12);
    }
}

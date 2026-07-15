using FluentValidation;
using Smartnet.Domain.Settings;

namespace Smartnet.Api.Contracts;

public sealed record CompanySummary(long Id, string Name, bool IsVatRegistered);

public sealed record CompanyProfile(
    long Id,
    string Name,
    bool IsVatRegistered,
    string? VatNumber,
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
    string? BrandColour);

public sealed record BusinessRule(string Key, string Value);

public sealed record TaxRateDto(
    long Id,
    string Name,
    decimal Percentage,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsDefault);

public sealed record SaveTaxRateRequest(
    string Name,
    decimal Percentage,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsDefault);

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

public sealed class SaveTaxRateRequestValidator : AbstractValidator<SaveTaxRateRequest>
{
    public SaveTaxRateRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(64);

        // A negative tax rate is not a discount, it is a bug.
        RuleFor(r => r.Percentage).InclusiveBetween(0m, 100m);

        RuleFor(r => r.EffectiveTo)
            .GreaterThanOrEqualTo(r => r.EffectiveFrom)
            .When(r => r.EffectiveTo is not null)
            .WithMessage("A rate cannot stop applying before it starts.");
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

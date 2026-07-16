namespace Smartnet.Domain.Settings;

/// <summary>
/// Resolves one business rule for one company — the effective value, not the raw rows.
/// </summary>
/// <remarks>
/// A rule is resolved the way the settings screen shows it: a company-specific value wins over a
/// global one (<c>company_id</c> null), which wins over the built-in default in
/// <see cref="BusinessRules.Defaults"/>. Every consumer — the tax engine's rounding mode, the
/// credit-limit gate — goes through this, so "what is the rule here?" has one answer.
/// </remarks>
public interface IBusinessRuleReader
{
    /// <summary>The effective value of <paramref name="key"/> for <paramref name="companyId"/>.</summary>
    Task<string> ResolveAsync(long companyId, string key, CancellationToken cancellationToken = default);
}

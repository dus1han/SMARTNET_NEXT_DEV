using System.Globalization;
using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Settings;

/// <summary>
/// A single configurable business rule, scoped to a company.
/// </summary>
/// <remarks>
/// Stored as key/value rather than as columns, because these are settings rather than data: they
/// are read a handful of times per request, written rarely, and the set of them will grow. A
/// column per rule means a migration every time the business changes its mind.
/// <para>
/// The <b>keys are not free-form</b> — see <see cref="BusinessRules"/>. A settings table anyone
/// can invent a key in becomes a junk drawer that nothing reads.
/// </para>
/// </remarks>
public class AppSetting : IAuditable
{
    public long Id { get; set; }

    /// <summary>Null means the value applies to every company unless one overrides it.</summary>
    public long? CompanyId { get; set; }

    public string Key { get; set; } = null!;

    /// <summary>
    /// Serialised as a string, parsed by <see cref="BusinessRules"/> with InvariantCulture — a
    /// decimal stored as "1.5" must not read back as 15 on a machine whose locale uses a comma.
    /// </summary>
    public string Value { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>
/// The rules that were hardcoded in the legacy app, and are now settings — DEVELOPMENT-PLAN §5.
/// </summary>
/// <remarks>
/// This list is deliberately closed. The plan's own risk register says it: "Settings that nobody
/// changes are cost. Only build the seven in §Settings surface."
/// </remarks>
public static class BusinessRules
{
    /// <summary>Whether the customer credit limit is enforced at all.</summary>
    /// <remarks>
    /// Legacy: <c>creditlimitcheck</c>, hardcoded, and applied to <i>service</i> invoices only —
    /// so the same customer could exceed their limit on an item invoice without anything noticing.
    /// </remarks>
    public const string CreditLimitEnforced = "credit_limit.enforced";

    /// <summary>Days until an invoice falls due. Legacy: not present at all.</summary>
    public const string DefaultPaymentTermsDays = "payment_terms.default_days";

    /// <summary>Below this, an item is low on stock. Legacy: not present.</summary>
    public const string StockReorderLevel = "stock.reorder_level";

    /// <summary>How long a quotation stands. Legacy: pushed per-document as <c>qvalidity</c>.</summary>
    public const string QuotationValidityDays = "quotation.validity_days";

    /// <summary>Maximum discount percentage a user may apply. Legacy: unrestricted.</summary>
    public const string MaxDiscountPercent = "discount.max_percent";

    /// <summary>
    /// "line" or "document" — whether VAT is rounded per line or on the document total.
    /// </summary>
    /// <remarks>
    /// Legacy: implicit in <c>double</c> arithmetic, which is to say nobody decided it and the
    /// answer varied. It must be decided explicitly before the Phase 5 tax engine is written.
    /// </remarks>
    public const string VatRoundingMode = "vat.rounding_mode";

    /// <summary>Days before an invoice is due to send a reminder. Legacy: not present.</summary>
    public const string InvoiceDueReminderDays = "invoice.due_reminder_days";

    public const string RoundPerLine = "line";
    public const string RoundPerDocument = "document";

    /// <summary>The seven rules and their defaults — what the system does before anyone changes it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        // Off by default: switching enforcement ON for the first time, silently, would start
        // blocking invoices the counter staff have always been able to raise.
        [CreditLimitEnforced] = "false",

        [DefaultPaymentTermsDays] = "30",
        [StockReorderLevel] = "5",
        [QuotationValidityDays] = "30",
        [MaxDiscountPercent] = "100",
        [VatRoundingMode] = RoundPerLine,
        [InvoiceDueReminderDays] = "3",
    };

    public static bool IsKnown(string key) => Defaults.ContainsKey(key);

    // --- Typed readers. Money and rates are decimal, never double. ---------------------------

    public static bool AsBool(string value) =>
        bool.TryParse(value, out var parsed) && parsed;

    public static int AsInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    public static decimal AsDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
}

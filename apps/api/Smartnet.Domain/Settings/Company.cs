using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Settings;

/// <summary>
/// A trading entity. Mapped onto the legacy <c>companies_m</c> table, which has three columns:
/// id, name, vatcode.
/// </summary>
/// <remarks>
/// Everything a document header prints — the address, the VAT number, the bank details — is
/// currently hardcoded in the legacy app's Crystal Reports templates, which is why changing a
/// phone number is a deployment. It lives here instead.
/// <para>
/// Two companies exist today (Smart Technologies and Smart Net). The schema assumes more, per
/// decision 4: <c>company_id</c> is a first-class dimension, not a hack.
/// </para>
/// </remarks>
public class Company : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Legacy: an id into <c>vat_ty</c>. Kept because the old app reads it.</summary>
    public string? VatCode { get; set; }

    /// <summary>
    /// Whether this entity is registered for VAT.
    /// </summary>
    /// <remarks>
    /// Not cosmetic: a non-registered company must not print a VAT number or charge VAT, and the
    /// legacy app has no way to express that — it splits documents into SN (VAT) and ST (non-VAT)
    /// by convention instead.
    /// </remarks>
    public bool IsVatRegistered { get; set; }

    /// <summary>The VAT registration number as printed. Null when not registered.</summary>
    public string? VatNumber { get; set; }

    // --- Document header ---------------------------------------------------------------------

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    // --- Bank details, printed on invoices ----------------------------------------------------

    public string? BankName { get; set; }
    public string? BankBranch { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }

    // --- Branding -----------------------------------------------------------------------------

    /// <summary>Object-storage key, never a path under the web root (see A8).</summary>
    public string? LogoKey { get; set; }

    /// <summary>Hex, e.g. "#0f172a". Used by the PDF templates in Phase 8.</summary>
    public string? BrandColour { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

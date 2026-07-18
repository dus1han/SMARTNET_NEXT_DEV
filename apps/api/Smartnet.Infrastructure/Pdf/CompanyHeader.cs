using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The company block printed at the top right of every document — address, contact channels,
/// registration numbers.
/// </summary>
/// <remarks>
/// One implementation, because two drifted. The job sheet formatted the telephone number into readable
/// groups while the quotation printed it as stored, so the same company's header read
/// "+94 77 069 4650" on one document and "+94770694650" on the other.
/// </remarks>
public static class CompanyHeader
{
    /// <summary>
    /// Address, then contact channels, then registration numbers — one line each, so nothing crams.
    /// </summary>
    public static string Build(Company? c)
    {
        if (c is null)
        {
            return string.Empty;
        }

        var address = new List<string>();
        var channels = new List<string>();
        var registration = new List<string>();

        static void Add(List<string> into, string? value, string prefix = "")
        {
            if (!string.IsNullOrWhiteSpace(value)) into.Add(prefix + value.Trim());
        }

        Add(address, c.AddressLine1);
        Add(address, c.AddressLine2);
        Add(address, c.City);
        Add(address, c.Country);

        if (!string.IsNullOrWhiteSpace(c.Phone)) channels.Add("Tel: " + FormatPhone(c.Phone.Trim()));
        Add(channels, c.Email);
        Add(channels, c.Website);

        Add(registration, c.BusinessRegistrationNo, "Reg. No: ");
        if (c.IsVatRegistered) Add(registration, c.VatNumber, "VAT No: ");

        return string.Join("\n", new[] { address, channels, registration }
            .Select(g => string.Join(" · ", g))
            .Where(line => line.Length > 0));
    }

    /// <summary>
    /// A stored number as a readable telephone number. Sri Lankan grouping: 07X XXX XXXX nationally,
    /// +94 7X XXX XXXX internationally. Anything unrecognised is returned unchanged rather than
    /// mangled into a grouping it does not have.
    /// </summary>
    public static string FormatPhone(string raw)
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
}

namespace Smartnet.Domain.Identity;

/// <summary>
/// What counts as an acceptable password.
/// </summary>
/// <remarks>
/// Deliberately a length floor and a blocklist, rather than the familiar
/// "one uppercase, one digit, one symbol" rule. Composition rules reliably produce
/// <c>Password1!</c> — they push users toward the small, predictable corner of the search space
/// that attackers try first. Length is what actually costs an attacker anything.
/// <para>
/// The bar is set where it is because the people affected are a showroom counter and an
/// administrator, not a security team: a rule staff cannot live with gets written on a sticky
/// note, which is a worse outcome than a slightly shorter password.
/// </para>
/// </remarks>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    /// <summary>The passwords this system is known to have used, and the perennials.</summary>
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234",          // ManageUserController assigned this to every new user it created
        "12345",
        "123456",
        "1234567890",
        "password",
        "password1",
        "smartnet",
        "smartnet1",
        "admin",
        "administrator",
        "welcome1",
        "qwertyuiop",
    };

    /// <param name="username">
    /// Rejected as a password. "chanaka" / "chanaka" is not a credential, it is a formality.
    /// </param>
    public static bool IsAcceptable(string? password, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            return false;
        }

        if (Forbidden.Contains(password))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(username)
            || !password.Contains(username, StringComparison.OrdinalIgnoreCase);
    }
}

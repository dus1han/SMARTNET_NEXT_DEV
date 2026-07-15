using System.Security.Cryptography;

namespace Smartnet.Domain.Identity;

/// <summary>
/// A single-use password for a new user, or for a reset.
/// </summary>
/// <remarks>
/// Closes the second half of ISSUES A4: <c>ManageUserController.cs:173</c> gave every new user the
/// password <c>1234</c>. Not a default to be changed — the actual password, the same one, for
/// everybody, also printed in the source code.
///
/// <para>Generated with a CSPRNG, shown to the administrator exactly once, and paired with
/// <c>must_change_password</c> so it stops working the moment the user has chosen their own.</para>
/// </remarks>
public static class TemporaryPassword
{
    /// <summary>
    /// No I/l/1/O/0. A temporary password gets read off a screen and typed, or spoken down a
    /// phone; characters that look like each other turn into a support call.
    /// </summary>
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzACDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Comfortably above PasswordPolicy.MinimumLength, and it only has to survive one login.</summary>
    private const int Length = 14;

    public static string Generate()
    {
        var characters = new char[Length];

        for (var i = 0; i < Length; i++)
        {
            // Rejection-sampled by the framework, so the distribution is uniform — a modulo of a
            // random byte would quietly favour the start of the alphabet.
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }
}

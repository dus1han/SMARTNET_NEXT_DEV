using Isopoh.Cryptography.Argon2;
using Smartnet.Domain.Identity;

namespace Smartnet.Infrastructure.Identity;

/// <summary>
/// Argon2id. Closes ISSUES A4 — passwords compared as plaintext (<c>LoginController.cs:77</c>),
/// written raw on change, and handed out as a hardcoded <c>1234</c> to every new user.
/// </summary>
/// <remarks>
/// Argon2id rather than bcrypt because it is memory-hard: it costs an attacker RAM as well as
/// time, which is what blunts GPU cracking. The cost parameters below are the point of the whole
/// exercise, so they are stated explicitly rather than left to a default that could change under
/// us in a future package version.
/// <para>
/// The salt is generated per password and embedded in the returned string, along with the
/// parameters — so a hash made today still verifies after these numbers are raised tomorrow.
/// </para>
/// </remarks>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    /// <summary>64 MB. The memory an attacker must find for <i>every</i> guess, in parallel.</summary>
    private const int MemoryCostKib = 65536;

    /// <summary>Passes over memory. Three is the OWASP floor for this memory size.</summary>
    private const int TimeCost = 3;

    /// <summary>Lanes. Matches a typical server core count without starving the request thread.</summary>
    private const int Parallelism = 1;

    private const int HashLength = 32;

    /// <remarks>
    /// This overload generates a fresh cryptographic salt per call and embeds it, with the
    /// parameters above, in the returned encoded string. So two users with the same password get
    /// different hashes, and a hash made today still verifies after these costs are raised.
    /// </remarks>
    public string Hash(string password) => Argon2.Hash(
        password: password,
        timeCost: TimeCost,
        memoryCost: MemoryCostKib,
        parallelism: Parallelism,
        type: Argon2Type.HybridAddressing, // = Argon2id
        hashLength: HashLength);

    /// <summary>
    /// Constant-time by construction — Argon2.Verify compares the derived hash, not the string,
    /// so a wrong password takes the same time to reject regardless of how nearly right it was.
    /// </summary>
    public bool Verify(string password, string hash) => Argon2.Verify(hash, password);
}

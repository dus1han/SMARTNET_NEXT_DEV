namespace Smartnet.Domain.Identity;

public interface IPasswordHasher
{
    /// <summary>Hashes a password. The salt and parameters are embedded in the returned string.</summary>
    string Hash(string password);

    /// <summary>Verifies a password against a stored hash, in constant time.</summary>
    bool Verify(string password, string hash);
}

using System.Security.Cryptography;

namespace Paperbunkr.Sharing.Server;

/// <summary>
/// PBKDF2-SHA256 password storage (spec §6). The stored string is self-describing -
/// <c>pbkdf2-sha256$iterations$salt$hash</c> (base64) - so the iteration count can be raised later
/// without breaking existing hashes, and the plaintext never touches disk or memory beyond the call.
/// </summary>
public static class PasswordHasher
{
    /// <summary>OWASP's current PBKDF2-HMAC-SHA256 guidance.</summary>
    public const int DefaultIterations = 600_000;

    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Constant-time verification. A malformed stored value verifies as false rather than throwing.</summary>
    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

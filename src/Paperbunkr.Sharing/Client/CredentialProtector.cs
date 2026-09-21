using System.Security.Cryptography;
using System.Text;

namespace Paperbunkr.Sharing.Client;

/// <summary>
/// Protects a saved remote-library password for storage (docs/superpowers/specs/2026-09-19-remote-
/// library-sharing-design.md §7.1). On Windows the bytes are wrapped with DPAPI for the current user,
/// so the stored value is useless if the database is copied to another account or machine; on other
/// platforms there is no equivalent here, so <see cref="IsProtectionAvailable"/> is false and callers
/// should offer "don't save the password" rather than store it in the clear.
/// </summary>
public static class CredentialProtector
{
    public static bool IsProtectionAvailable => OperatingSystem.IsWindows();

    /// <summary>Base64 of the DPAPI-protected UTF-8 password. Throws <see cref="PlatformNotSupportedException"/> when <see cref="IsProtectionAvailable"/> is false.</summary>
    public static string Protect(string password)
    {
        if (!IsProtectionAvailable)
        {
            throw new PlatformNotSupportedException("No credential protection is available on this platform.");
        }

        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>The original password, or null if the value is corrupt or was protected by a different user/machine.</summary>
    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue) || !IsProtectionAvailable)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

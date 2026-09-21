using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Paperbunkr.Sharing.Server;

/// <summary>
/// The host's self-signed TLS identity (spec §6). Generated once (ECDSA P-256), then persisted in
/// <c>share-cert.bin</c> with the whole PFX - private key included - wrapped by Windows DPAPI for the
/// current user, so the file is useless if copied to another account or machine. Clients don't chain
/// to a CA; they pin <see cref="Fingerprint"/> on first connect, and a regenerated certificate shows
/// up to them as a "host certificate changed" event that needs their explicit re-trust.
/// </summary>
public sealed class CertificateManager
{
    private const string FileName = "share-cert.bin";

    private readonly string _directory;
    private readonly object _gate = new();
    private X509Certificate2? _certificate;

    public CertificateManager(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    private string FilePath => Path.Combine(_directory, FileName);

    /// <summary>Loads the persisted certificate, or generates and persists one on first use.</summary>
    public X509Certificate2 GetOrCreate()
    {
        lock (_gate)
        {
            return _certificate ??= TryLoad() ?? CreateAndPersist();
        }
    }

    /// <summary>Uppercase hex SHA-256 of the certificate - the value shown in the trust prompt and stored by clients.</summary>
    public string Fingerprint => FingerprintOf(GetOrCreate());

    /// <summary>Replaces the certificate. Every client that pinned the old one must re-trust.</summary>
    public X509Certificate2 Regenerate()
    {
        lock (_gate)
        {
            _certificate?.Dispose();
            _certificate = null;
            return _certificate = CreateAndPersist();
        }
    }

    public static string FingerprintOf(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    private X509Certificate2? TryLoad()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            byte[] pfx = Unprotect(File.ReadAllBytes(FilePath));
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            // Unreadable (different user/machine, corrupt): fall through and mint a fresh identity.
            // Clients see this as a changed fingerprint, which is the correct outcome.
            return null;
        }
    }

    private X509Certificate2 CreateAndPersist()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=Paperbunkr Share ({Environment.MachineName})", key, HashAlgorithmName.SHA256);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false)); // serverAuth

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
        byte[] pfx = ephemeral.Export(X509ContentType.Pfx);

        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(FilePath, Protect(pfx));

        // Re-import rather than reuse the ephemeral instance: SslStream on Windows can't use
        // ephemeral (in-memory-only) keys, and this also proves the persisted bytes load.
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser) : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser) : data;
}

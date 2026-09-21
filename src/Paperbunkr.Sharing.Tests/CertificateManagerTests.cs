using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Sharing.Tests;

public class CertificateManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_cert_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreate_GeneratesOnce_AndReloadsTheSameIdentity()
    {
        string first = new CertificateManager(_dir).Fingerprint;
        string second = new CertificateManager(_dir).Fingerprint; // a fresh instance reads the file

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length); // SHA-256 hex
    }

    [Fact]
    public void GetOrCreate_ProducesAnEcdsaP256Certificate_WithAPrivateKey()
    {
        using X509Certificate2 cert = new CertificateManager(_dir).GetOrCreate();

        Assert.True(cert.HasPrivateKey);
        using ECDsa? key = cert.GetECDsaPrivateKey();
        Assert.NotNull(key);
        Assert.Equal(256, key!.KeySize);
    }

    [Fact]
    public void Regenerate_ChangesTheFingerprint_AndPersistsTheNewOne()
    {
        var manager = new CertificateManager(_dir);
        string before = manager.Fingerprint;

        manager.Regenerate();

        Assert.NotEqual(before, manager.Fingerprint);
        Assert.Equal(manager.Fingerprint, new CertificateManager(_dir).Fingerprint);
    }

    [Fact]
    public void DeletingTheFile_MintsANewIdentity()
    {
        string before = new CertificateManager(_dir).Fingerprint;
        File.Delete(Path.Combine(_dir, "share-cert.bin"));

        Assert.NotEqual(before, new CertificateManager(_dir).Fingerprint);
    }

    [Fact]
    public void ACorruptFile_IsReplacedRatherThanCrashing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "share-cert.bin"), new byte[] { 1, 2, 3, 4, 5 });

        using X509Certificate2 cert = new CertificateManager(_dir).GetOrCreate();

        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void PrivateKeyFile_IsNotReadableAsAPlainPfx_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI wrapping is Windows-only; elsewhere the file relies on OS permissions.
        }

        _ = new CertificateManager(_dir).Fingerprint;
        byte[] bytes = File.ReadAllBytes(Path.Combine(_dir, "share-cert.bin"));

        Assert.ThrowsAny<CryptographicException>(() => X509CertificateLoader.LoadPkcs12(bytes, null));
    }
}

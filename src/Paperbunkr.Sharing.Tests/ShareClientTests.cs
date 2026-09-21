using Paperbunkr.Sharing.Client;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Sharing.Tests;

/// <summary>
/// <see cref="ShareClient"/> against the real <see cref="ShareServer"/> over loopback HTTPS
/// (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §5/§6): the pinning guarantee
/// (a different certificate can never be silently accepted), typed failures for the UI, transparent
/// re-login, and conditional catalog pulls.
/// </summary>
public sealed class ShareClientTests : IAsyncLifetime
{
    private const string Password = "s3cret-pass";

    private readonly string _certDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_cli_{Guid.NewGuid():N}");
    private readonly FakeCatalog _catalog = new();
    private readonly FakePages _pages = new();
    private CertificateManager _certs = null!;
    private ShareServer _server = null!;
    private readonly List<ShareClient> _clients = new();

    public async Task InitializeAsync()
    {
        _certs = new CertificateManager(_certDir);
        _server = new ShareServer(
            new ShareServerOptions
            {
                Port = 0, PasswordHash = PasswordHasher.Hash(Password, 1_000), DisplayName = "Test Host",
                InstanceId = "22222222-2222-2222-2222-222222222222", MaxFailedAttempts = 3,
            },
            _catalog, _pages, _certs.GetOrCreate());
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var c in _clients) c.Dispose();
        await _server.DisposeAsync();
        if (Directory.Exists(_certDir)) Directory.Delete(_certDir, recursive: true);
    }

    private ShareClient Client(string? fingerprint = null, string password = Password, int? port = null)
    {
        var client = new ShareClient("127.0.0.1", port ?? _server.Port, fingerprint ?? _certs.Fingerprint, password, TimeSpan.FromSeconds(10));
        _clients.Add(client);
        return client;
    }

    [Fact]
    public async Task Probe_ReturnsIdentityAndTheFingerprintToTrust()
    {
        var probe = await ShareClient.ProbeAsync("127.0.0.1", _server.Port);

        Assert.Equal("Test Host", probe.Hello.DisplayName);
        Assert.Equal("22222222-2222-2222-2222-222222222222", probe.Hello.InstanceId);
        Assert.Equal(_certs.Fingerprint, probe.Fingerprint);
    }

    [Fact]
    public async Task Probe_OfADeadPort_IsAHostUnreachableError()
    {
        await _server.StopAsync();

        await Assert.ThrowsAsync<HostUnreachableException>(() => ShareClient.ProbeAsync("127.0.0.1", _server.Port, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task PinnedClient_WithTheRightFingerprint_PullsTheWholeCatalog_AcrossPages()
    {
        var pull = await Client().GetCatalogAsync(ifNoneMatch: null, pageSize: 1);

        Assert.False(pull.NotModified);
        Assert.Equal("\"v1\"", pull.ETag);
        Assert.Equal(new[] { "one", "two" }, pull.Issues.Select(i => i.Title).ToArray()); // two pages, merged
        Assert.Single(pull.Series);                                                       // de-duplicated
    }

    [Fact]
    public async Task Catalog_WithTheCurrentETag_IsNotModified_ButAChangedVersionIsPulledAgain()
    {
        var client = Client();
        var first = await client.GetCatalogAsync(null);

        var second = await client.GetCatalogAsync(first.ETag);
        Assert.True(second.NotModified);
        Assert.Empty(second.Issues);

        _catalog.Version = "v2";
        var third = await client.GetCatalogAsync(first.ETag);
        Assert.False(third.NotModified);
        Assert.Equal("\"v2\"", third.ETag);
    }

    [Fact]
    public async Task WrongFingerprint_IsACertificateMismatch_NeverAccepted()
    {
        string wrongPin = new string('A', 64);
        var client = Client(fingerprint: wrongPin);

        var ex = await Assert.ThrowsAsync<CertificateMismatchException>(() => client.GetCatalogAsync(null));

        Assert.Equal(wrongPin, ex.Expected);
        Assert.Equal(_certs.Fingerprint, ex.Actual);
    }

    [Fact]
    public async Task ARegeneratedHostCertificate_BlocksAPreviouslyTrustedClient()
    {
        var client = Client(); // pinned to the current certificate
        await client.GetCatalogAsync(null);

        await _server.StopAsync();
        string oldFingerprint = _certs.Fingerprint;
        _certs.Regenerate();
        await using var restarted = new ShareServer(
            new ShareServerOptions { Port = 0, PasswordHash = PasswordHasher.Hash(Password, 1_000) }, _catalog, _pages, _certs.GetOrCreate());
        await restarted.StartAsync();

        var stale = new ShareClient("127.0.0.1", restarted.Port, oldFingerprint, Password);
        _clients.Add(stale);
        var ex = await Assert.ThrowsAsync<CertificateMismatchException>(() => stale.GetCatalogAsync(null));

        Assert.Equal(oldFingerprint, ex.Expected);
        Assert.Equal(_certs.Fingerprint, ex.Actual);
        Assert.NotEqual(ex.Expected, ex.Actual);
    }

    [Fact]
    public async Task WrongPassword_IsAuthFailed_AndRepeatedFailuresLockOut()
    {
        var client = Client(password: "nope");

        await Assert.ThrowsAsync<AuthFailedException>(() => client.GetCatalogAsync(null));
        await Assert.ThrowsAsync<AuthFailedException>(() => client.GetCatalogAsync(null));
        await Assert.ThrowsAsync<AuthFailedException>(() => client.GetCatalogAsync(null));

        var locked = await Assert.ThrowsAsync<LockedOutException>(() => client.GetCatalogAsync(null));
        Assert.True(locked.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task ExpiredOrLostSession_IsRenewedTransparently()
    {
        var client = Client();
        await client.GetCatalogAsync(null);

        _server.Sessions.Clear(); // what a host restart does to every session

        var pull = await client.GetCatalogAsync(null);
        Assert.False(pull.NotModified);
    }

    [Fact]
    public async Task PageBytes_ComeBackIntact_WithAndWithoutWidth_AndAMissingPageIsNull()
    {
        var client = Client();

        Assert.Equal(FakePages.PageBytes, await client.GetPageBytesAsync(1, 0));
        Assert.NotNull(await client.GetPageBytesAsync(1, 1, maxWidth: 300));
        Assert.Equal(300, _pages.LastRequestedWidth);
        Assert.Null(await client.GetPageBytesAsync(1, 9));
        Assert.Equal(2, (await client.GetPagesAsync(1)).PageCount);
        Assert.NotNull(await client.GetCoverBytesAsync(1, 200));
    }

    [Fact]
    public async Task AnIssueOutsideTheScope_IsSimplyNull_NotAnError()
    {
        Assert.Null(await Client().GetPageBytesAsync(99, 0));
    }

    [Fact]
    public async Task Hello_OverThePinnedConnection_ReportsTheInstanceId()
    {
        var hello = await Client().HelloAsync();

        Assert.Equal("22222222-2222-2222-2222-222222222222", hello.InstanceId);
    }

    [Fact]
    public void Constructor_RejectsAMalformedPin()
    {
        Assert.Throws<ArgumentException>(() => new ShareClient("127.0.0.1", 1, "not-hex", "pw"));
        Assert.Throws<ArgumentException>(() => new ShareClient("127.0.0.1", 1, new string('A', 63), "pw"));
    }

    [Fact]
    public void CredentialProtector_RoundTrips_AndNeverStoresPlaintext()
    {
        if (!CredentialProtector.IsProtectionAvailable)
        {
            return;
        }

        string stored = CredentialProtector.Protect("plain-secret-value");

        Assert.DoesNotContain("plain-secret-value", stored);
        Assert.DoesNotContain("plain-secret-value", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(stored)));
        Assert.Equal("plain-secret-value", CredentialProtector.Unprotect(stored));
        Assert.Null(CredentialProtector.Unprotect("garbage"));
        Assert.Null(CredentialProtector.Unprotect(null));
    }
}

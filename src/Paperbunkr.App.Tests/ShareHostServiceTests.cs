using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Sharing;
using Paperbunkr.Sharing.Client;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ShareHostService"/> (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md
/// §4/§6/§9): lifecycle, the "never share without a password" rule, and that everything the user needs
/// to know goes through the Activity Center rather than ad-hoc UI.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class ShareHostServiceTests : IAsyncLifetime
{
    private const string Password = "hunter2-hunter2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_hostsvc_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly ActivityService _activity = new(dispatch: a => a(), recordRun: _ => { });
    private ShareHostService _host = null!;
    private int _issueId;

    public ShareHostServiceTests()
    {
        Directory.CreateDirectory(_root);
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "t.db")}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
        var series = new Series { Name = "Saga" };
        context.Series.Add(series);
        context.SaveChanges();
        string cbz = CbzFixture.Create(Path.Combine(_root, "saga.cbz"), pageCount: 2);
        var issue = new Issue { SeriesId = series.Id, Number = "1", Title = "One", FilePath = cbz };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
    }

    public Task InitializeAsync()
    {
        _host = Create();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ShareHostService Create(int port = 0, Action<Paperbunkr.Sharing.Server.ShareServerOptions>? configure = null)
    {
        var store = new ShareSettingsStore(Path.Combine(_root, "sharing", "settings.json"));
        var settings = store.Load();
        settings.Port = port;
        settings.Scope = new ShareScope { Mode = ShareMode.All };
        store.Save(settings);
        return new ShareHostService(
            store, () => new PaperbunkrDbContext(_options), _activity, Path.Combine(_root, "sharing"),
            configure);
    }

    private bool AlertRaised(string titleFragment) => _activity.Alerts.Any(a => a.Title.Contains(titleFragment));

    [Fact]
    public async Task Start_WithoutAPassword_Refuses_RecordsWhy_AndAlerts()
    {
        _host.Settings.Enabled = true;

        await _host.StartAsync();

        Assert.False(_host.IsRunning);
        Assert.Contains("password", _host.LastError!, StringComparison.OrdinalIgnoreCase);
        Assert.True(AlertRaised("Sharing didn't start"));
    }

    [Fact]
    public async Task Start_WithAPassword_Serves_AndShowsAsAnActivityJob()
    {
        _host.SetPassword(Password);
        await _host.StartAsync();

        Assert.True(_host.IsRunning);
        Assert.Null(_host.LastError);
        var job = Assert.Single(_activity.ActiveJobs, j => j.Kind == ActivityJobKind.Sharing);
        Assert.Contains(_host.Port.ToString(), job.Detail);

        var probe = await ShareClient.ProbeAsync("127.0.0.1", _host.Port);
        Assert.Equal(_host.Settings.InstanceId, probe.Hello.InstanceId);
        Assert.Equal(_host.CertificateFingerprint, probe.Fingerprint);
    }

    [Fact]
    public async Task SuccessfulStart_ClearsAnEarlierStartFailureAlert()
    {
        _host.Settings.Enabled = true;
        await _host.StartAsync();                 // fails: no password
        Assert.True(AlertRaised("Sharing didn't start"));

        _host.SetPassword(Password);
        await _host.StartAsync();

        Assert.True(_host.IsRunning);
        Assert.False(AlertRaised("Sharing didn't start"));
    }

    [Fact]
    public async Task CancellingTheActivityJob_StopsTheServer()
    {
        _host.SetPassword(Password);
        await _host.StartAsync();
        var job = _activity.ActiveJobs.Single(j => j.Kind == ActivityJobKind.Sharing);

        _activity.CancelJob(job.Id);
        await WaitUntilAsync(() => !_host.IsRunning);

        Assert.False(_host.IsRunning);
        await Assert.ThrowsAnyAsync<Exception>(() => ShareClient.ProbeAsync("127.0.0.1", 1, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task SaveAndApply_FollowsTheEnabledFlag_BothWays()
    {
        _host.SetPassword(Password);
        _host.Settings.Enabled = true;
        await _host.SaveAndApplyAsync();
        Assert.True(_host.IsRunning);

        _host.Settings.Enabled = false;
        await _host.SaveAndApplyAsync();

        Assert.False(_host.IsRunning);
        Assert.Empty(_activity.ActiveJobs.Where(j => j.Kind == ActivityJobKind.Sharing));
    }

    [Fact]
    public async Task NarrowingTheScope_TakesEffectImmediately_NotAfterTheSnapshotTtl()
    {
        // A fixed port, like the real feature: with port 0 the restart would bind a different one.
        await _host.DisposeAsync();
        _host = Create(port: FreePort());
        _host.SetPassword(Password);
        _host.Settings.Enabled = true;
        await _host.SaveAndApplyAsync();
        var client = new ShareClient("127.0.0.1", _host.Port, _host.CertificateFingerprint, Password);
        using var _ = client;
        Assert.Single((await client.GetCatalogAsync(null)).Issues);

        _host.Settings.Scope = new ShareScope { Mode = ShareMode.None };
        await _host.SaveAndApplyAsync();

        // The restart dropped every session; the client re-logs in transparently.
        Assert.Empty((await client.GetCatalogAsync(null)).Issues);
        Assert.Null(await client.GetPageBytesAsync(_issueId, 0)); // and pages of an unshared issue are gone too
    }

    [Fact]
    public async Task ServesRealPagesFromTheArchive()
    {
        _host.SetPassword(Password);
        await _host.StartAsync();
        using var client = new ShareClient("127.0.0.1", _host.Port, _host.CertificateFingerprint, Password);

        byte[]? page = await client.GetPageBytesAsync(_issueId, 0);

        Assert.NotNull(page);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, page![..4]);
    }

    [Fact]
    public async Task RepeatedWrongPasswords_RaiseALockoutAlert()
    {
        await _host.DisposeAsync();
        _host = Create(configure: o => o.MaxFailedAttempts = 2);
        _host.SetPassword(Password);
        await _host.StartAsync();
        using var bad = new ShareClient("127.0.0.1", _host.Port, _host.CertificateFingerprint, "wrong");

        for (int i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<AuthFailedException>(() => bad.GetCatalogAsync(null));
        }

        Assert.True(AlertRaised("failed sign-ins"));
    }

    [Fact]
    public async Task RegeneratingTheCertificate_ChangesTheFingerprint_AndKeepsServing()
    {
        _host.SetPassword(Password);
        await _host.StartAsync();
        string before = _host.CertificateFingerprint;

        await _host.RegenerateCertificateAsync();

        Assert.True(_host.IsRunning);
        Assert.NotEqual(before, _host.CertificateFingerprint);
        var probe = await ShareClient.ProbeAsync("127.0.0.1", _host.Port);
        Assert.Equal(_host.CertificateFingerprint, probe.Fingerprint);
    }

    [Fact]
    public async Task APortAlreadyInUse_FailsCleanly_WithAnAlert()
    {
        using var blocker = new TcpListener(IPAddress.Any, 0);
        blocker.Start();
        int taken = ((IPEndPoint)blocker.LocalEndpoint).Port;
        await _host.DisposeAsync();
        _host = Create(port: taken);
        _host.SetPassword(Password);
        _host.Settings.Enabled = true;

        await _host.StartAsync();

        Assert.False(_host.IsRunning);
        Assert.NotNull(_host.LastError);
        Assert.True(AlertRaised("Sharing didn't start"));
    }

    [Fact]
    public void SetPassword_StoresOnlyAHash()
    {
        _host.SetPassword("plain-secret-value");

        string file = File.ReadAllText(Path.Combine(_root, "sharing", "settings.json"));
        Assert.DoesNotContain("plain-secret-value", file);
        Assert.Contains("pbkdf2-sha256", file);
        Assert.True(_host.HasPassword);
    }

    [Fact]
    public async Task StartIfEnabled_DoesNothingWhenDisabled_AndStartsWhenEnabled()
    {
        _host.SetPassword(Password);
        await _host.StartIfEnabledAsync();
        Assert.False(_host.IsRunning);

        _host.Settings.Enabled = true;
        await _host.StartIfEnabledAsync();
        Assert.True(_host.IsRunning);
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }
}

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
/// <see cref="RemoteLibraryService"/> against a real <see cref="ShareHostService"/> on loopback, with a
/// separate database on each side (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md
/// §6/§7): the whole client lifecycle, and above all the security-relevant failures - a changed
/// certificate is never accepted silently, and nothing local is ever lost.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class RemoteLibraryServiceTests : IAsyncLifetime
{
    private const string Password = "hunter2-hunter2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_remotesvc_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _hostDb;
    private readonly DbContextOptions<PaperbunkrDbContext> _clientDb;
    private readonly ActivityService _hostActivity = new(a => a(), _ => { });
    private readonly ActivityService _clientActivity = new(a => a(), _ => { });
    private readonly List<int> _removed = new();
    private IReadOnlyList<int> _removedIssueIds = Array.Empty<int>();
    private readonly List<(int Source, IReadOnlyList<int>? Ids)> _invalidated = new();
    private readonly int _port;
    private ShareHostService _host = null!;
    private RemoteLibraryService _client = null!;
    private int _hostIssueId;

    public RemoteLibraryServiceTests()
    {
        Directory.CreateDirectory(_root);
        _hostDb = Options("host.db");
        _clientDb = Options("client.db");

        using (var context = new PaperbunkrDbContext(_hostDb))
        {
            context.Database.EnsureCreated();
            var series = new Series { Name = "Saga" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "1", Title = "One", FilePath = CbzFixture.Create(Path.Combine(_root, "saga1.cbz"), 2) };
            context.Issues.Add(issue);
            context.SaveChanges();
            _hostIssueId = issue.Id;
        }

        using (var context = new PaperbunkrDbContext(_clientDb))
        {
            context.Database.EnsureCreated();
            // Local data on the client that must never be touched: same series name as the host's, on purpose.
            var local = new Series { Name = "Saga" };
            context.Series.Add(local);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = local.Id, Number = "1", Title = "Local One", FilePath = @"C:\mine\saga1.cbz", LastPageRead = 9 });
            context.SaveChanges();
        }

        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public async Task InitializeAsync()
    {
        var store = new ShareSettingsStore(Path.Combine(_root, "hostshare", "settings.json"));
        var settings = store.Load();
        settings.Port = _port;
        settings.DisplayName = "Den PC";
        settings.Scope = new ShareScope { Mode = ShareMode.All };
        settings.Enabled = true;   // an enabled host: SaveAndApply restarts it rather than leaving it stopped
        store.Save(settings);
        _host = new ShareHostService(store, () => new PaperbunkrDbContext(_hostDb), _hostActivity, Path.Combine(_root, "hostshare"));
        _host.SetPassword(Password);
        await _host.StartAsync();

        _client = new RemoteLibraryService(inc => Client(inc), _clientActivity, (id, issueIds) => { _removed.Add(id); _removedIssueIds = issueIds; },
            onContentInvalidated: (id, ids) => _invalidated.Add((id, ids)));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DbContextOptions<PaperbunkrDbContext> Options(string file) =>
        new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, file)}").Options;

    private PaperbunkrDbContext Client(bool includeRemote) => new(_clientDb) { IncludeRemote = includeRemote };

    private async Task<RemoteSource> AddAsync(string password = Password, bool save = true)
    {
        var probe = await _client.ProbeAsync("127.0.0.1", _port);
        return await _client.AddAsync("127.0.0.1", _port, password, probe, save);
    }

    private bool AlertRaised(string fragment) => _clientActivity.Alerts.Any(a => a.Title.Contains(fragment));

    private int MirrorIssueCount() { using var c = Client(true); return c.Issues.Count(i => i.RemoteSourceId != null); }

    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Add_MirrorsTheHostLibrary_AndKeepsTheLocalOneUntouched()
    {
        var source = await AddAsync();

        Assert.Equal("Den PC", source.DisplayName);
        Assert.Equal(_host.CertificateFingerprint, source.CertFingerprint);
        Assert.Equal(_host.Settings.InstanceId, source.InstanceId);
        Assert.Equal(RemoteSourceState.Online, _client.StatusOf(source.Id).State);

        using var context = Client(true);
        var mirror = context.Issues.Single(i => i.RemoteSourceId == source.Id);
        Assert.Equal("One", mirror.Title);
        Assert.Null(mirror.FilePath);
        var local = context.Issues.Single(i => i.RemoteSourceId == null);
        Assert.Equal("Local One", local.Title);       // same-named series, separate rows
        Assert.Equal(9, local.LastPageRead);
        Assert.Equal(2, context.Series.Count());
    }

    [Fact]
    public async Task Add_StoresThePasswordProtected_NeverPlaintext()
    {
        if (!CredentialProtector.IsProtectionAvailable) return;

        var source = await AddAsync();

        using var context = Client(true);
        string stored = context.RemoteSources.Single().ProtectedPassword!;
        Assert.DoesNotContain(Password, stored);
        Assert.Equal(Password, CredentialProtector.Unprotect(stored));
    }

    [Fact]
    public async Task Add_WithAWrongPassword_LeavesNothingBehind()
    {
        var probe = await _client.ProbeAsync("127.0.0.1", _port);

        await Assert.ThrowsAsync<AuthFailedException>(() => _client.AddAsync("127.0.0.1", _port, "wrong", probe, savePassword: true));

        Assert.Empty(_client.List());
        Assert.Equal(0, MirrorIssueCount());
    }

    [Fact]
    public async Task Add_TheSameHostTwice_IsRefused()
    {
        await AddAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => AddAsync());
        Assert.Single(_client.List());
    }

    [Fact]
    public async Task Sync_WhenNothingChanged_IsAnEtagNoOp_AndWhenTheHostAddsABook_PicksItUp()
    {
        var source = await AddAsync();
        var first = await _client.SyncAsync(source.Id);
        Assert.Null(first);                                           // NotModified: no mirror work at all
        Assert.Contains(_clientActivity.RecentJobs, j => j.Kind == ActivityJobKind.RemoteSync && j.Detail?.Contains("up to date") == true || j.Kind == ActivityJobKind.RemoteSync);

        using (var context = new PaperbunkrDbContext(_hostDb))
        {
            var series = context.Series.Single();
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "2", Title = "Two", FilePath = CbzFixture.Create(Path.Combine(_root, "saga2.cbz"), 2) });
            context.SaveChanges();
        }
        _host.InvalidateCatalog();

        var result = await _client.SyncAsync(source.Id);

        Assert.Equal(1, result!.IssuesAdded);
        Assert.Equal(2, MirrorIssueCount());
    }

    [Fact]
    public async Task HostGoesOffline_MarksOffline_KeepsTheMirror_AndRecoversWhenItReturns()
    {
        var source = await AddAsync();
        await _host.StopAsync();

        await _client.SyncAsync(source.Id);

        Assert.Equal(RemoteSourceState.Offline, _client.StatusOf(source.Id).State);
        Assert.True(_client.List().Single().IsOffline);
        Assert.Equal(1, MirrorIssueCount());                          // still there, still browsable
        Assert.True(AlertRaised("is offline"));                       // manual sync explains itself

        await _host.StartAsync();
        await _client.SyncAsync(source.Id);

        Assert.Equal(RemoteSourceState.Online, _client.StatusOf(source.Id).State);
        Assert.False(_client.List().Single().IsOffline);
        Assert.False(AlertRaised("is offline"));
    }

    [Fact]
    public async Task ScheduledSync_OfAnOfflineHost_IsQuiet()
    {
        var source = await AddAsync();
        await _host.StopAsync();

        await _client.SyncAllAsync(ActivityTrigger.Startup);

        Assert.Equal(RemoteSourceState.Offline, _client.StatusOf(source.Id).State);
        Assert.False(AlertRaised("is offline"));                      // no alert spam on launch
    }

    [Fact]
    public async Task ChangedCertificate_BlocksTheSync_TouchesNothing_AndOnlyAnExplicitRetrustReconnects()
    {
        var source = await AddAsync();
        string trusted = source.CertFingerprint;
        await _host.RegenerateCertificateAsync();
        string presented = _host.CertificateFingerprint;

        await _client.SyncAsync(source.Id);

        var status = _client.StatusOf(source.Id);
        Assert.Equal(RemoteSourceState.CertificateChanged, status.State);
        Assert.Equal(presented, status.PresentedFingerprint);          // what the re-trust dialog shows as "new"
        Assert.Equal(trusted, _client.List().Single().CertFingerprint); // the pin is untouched - never auto-updated
        Assert.True(AlertRaised("certificate changed"));
        Assert.Equal(1, MirrorIssueCount());

        // A background "refresh all" must not retry (and so must not re-trust) a blocked source.
        await _client.SyncAllAsync(ActivityTrigger.Scheduled);
        Assert.Equal(trusted, _client.List().Single().CertFingerprint);

        // The user reviewed old vs new and confirmed.
        await _client.RetrustAsync(source.Id, presented);

        Assert.Equal(RemoteSourceState.Online, _client.StatusOf(source.Id).State);
        Assert.Equal(presented, _client.List().Single().CertFingerprint);
        Assert.False(AlertRaised("certificate changed"));
        Assert.Equal(1, MirrorIssueCount());                           // same mirror, no delete-and-re-add
    }

    [Fact]
    public async Task RotatedHostPassword_IsAuthFailed_UntilTheNewOneIsSupplied()
    {
        var source = await AddAsync();
        _host.SetPassword("a-brand-new-password");
        await _host.SaveAndApplyAsync();                               // restart drops sessions

        await _client.SyncAsync(source.Id);
        Assert.Equal(RemoteSourceState.AuthFailed, _client.StatusOf(source.Id).State);
        Assert.True(AlertRaised("rejected your password"));

        _client.SetPassword(source.Id, "a-brand-new-password", save: true);
        await _client.SyncAsync(source.Id);

        Assert.Equal(RemoteSourceState.Online, _client.StatusOf(source.Id).State);
        Assert.False(AlertRaised("rejected your password"));
    }

    [Fact]
    public async Task ChangedInstance_IsFlaggedHostChanged_AndBlocksSyncingUntilTheUserDecides()
    {
        var source = await AddAsync();
        _host.Settings.InstanceId = Guid.NewGuid().ToString();         // the host's library was reset
        await _host.SaveAndApplyAsync();

        var result = await _client.SyncAsync(source.Id);

        Assert.Null(result);
        Assert.Equal(RemoteSourceState.HostChanged, _client.StatusOf(source.Id).State);
        Assert.True(_client.List().Single().HostChanged);
        Assert.True(AlertRaised("different library"));
        Assert.Equal(1, MirrorIssueCount());                           // nothing deleted "to be safe"

        // Later syncs stay blocked rather than quietly re-pointing rows at other books.
        await _client.SyncAsync(source.Id);
        Assert.Equal(RemoteSourceState.HostChanged, _client.StatusOf(source.Id).State);
    }

    [Fact]
    public async Task Relink_AfterAHostReset_KeepsTheUsersReadingProgress()
    {
        var source = await AddAsync();
        using (var context = Client(true))
        {
            var issue = context.Issues.Single(i => i.RemoteSourceId == source.Id);
            issue.LastPageRead = 14;
            issue.Rating = 4f;
            context.SaveChanges();
        }

        // The host rebuilds: new instance id and new database ids, same books.
        await _host.StopAsync();
        using (var context = new PaperbunkrDbContext(_hostDb))
        {
            context.Issues.RemoveRange(context.Issues.ToList());
            context.Series.RemoveRange(context.Series.ToList());
            context.SaveChanges();
            var series = new Series { Name = "Saga" };
            context.Series.Add(series);
            context.SaveChanges();
            for (int i = 0; i < 40; i++) context.Series.Add(new Series { Name = $"pad{i}" }); // shift the id space
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", Title = "One", FilePath = CbzFixture.Create(Path.Combine(_root, "saga1b.cbz"), 2) });
            context.SaveChanges();
        }
        _host.Settings.InstanceId = Guid.NewGuid().ToString();
        await _host.SaveAndApplyAsync();
        await _client.SyncAsync(source.Id);
        Assert.Equal(RemoteSourceState.HostChanged, _client.StatusOf(source.Id).State);

        var probe = await _client.ProbeAsync("127.0.0.1", _port);
        var relink = await _client.RelinkAsync(source.Id, probe, Password);

        Assert.Equal(1, relink.IssuesMatched);
        Assert.Equal(0, relink.IssuesOrphaned);
        Assert.Equal(RemoteSourceState.Online, _client.StatusOf(source.Id).State);
        Assert.False(_client.List().Single().HostChanged);
        Assert.Equal(_host.Settings.InstanceId, _client.List().Single().InstanceId);
        using var check = Client(true);
        var mirror = check.Issues.Single(i => i.RemoteSourceId == source.Id);
        Assert.Equal(14, mirror.LastPageRead);                         // progress followed the book
        Assert.Equal(4f, mirror.Rating);
        Assert.Equal(1, check.Issues.Count(i => i.RemoteSourceId != null)); // no duplicate mirror
    }

    [Fact]
    public async Task ReplacedBookContent_OnTheHost_TellsTheCachesWhichIssuesWentStale()
    {
        var source = await AddAsync();
        using (var context = new PaperbunkrDbContext(_hostDb))
        {
            var issue = context.Issues.Single();
            issue.PageCount = 24;                      // the host swapped in a different file with a different page count
            context.SaveChanges();
        }
        using (var seed = Client(true))
        {
            var mirror = seed.Issues.Single(i => i.RemoteSourceId == source.Id);
            mirror.PageCount = 22;                     // what the client mirrored before
            seed.SaveChanges();
        }
        _host.InvalidateCatalog();

        await _client.SyncAsync(source.Id);

        var (id, ids) = Assert.Single(_invalidated);
        Assert.Equal(source.Id, id);
        Assert.Equal(new[] { _hostIssueId }, ids);
    }

    [Fact]
    public async Task Relink_TellsTheCachesEverythingUnderTheSourceIsStale()
    {
        var source = await AddAsync();
        await _host.StopAsync();
        _host.Settings.InstanceId = Guid.NewGuid().ToString();
        await _host.SaveAndApplyAsync();
        await _client.SyncAsync(source.Id);
        var probe = await _client.ProbeAsync("127.0.0.1", _port);

        await _client.RelinkAsync(source.Id, probe, Password);

        var (id, ids) = Assert.Single(_invalidated);
        Assert.Equal(source.Id, id);
        Assert.Null(ids);                              // null = every host id under this source was re-keyed
    }

    [Fact]
    public async Task Remove_DeletesTheMirror_NotTheLocalLibrary_AndTellsTheCachesToPurge()
    {
        var source = await AddAsync();

        _client.Remove(source.Id);

        Assert.Empty(_client.List());
        Assert.Equal(0, MirrorIssueCount());
        Assert.Equal(new[] { source.Id }, _removed);
        Assert.Single(_removedIssueIds);                             // the caches learn exactly which local issues went
        using var context = Client(true);
        Assert.Equal("Local One", context.Issues.Single().Title);
        Assert.Equal(9, context.Issues.Single().LastPageRead);
        Assert.Equal(1, context.Series.Count());
    }

    [Fact]
    public async Task GetClient_ReadsRealPages_ThroughThePinnedSession()
    {
        var source = await AddAsync();
        int remoteId;
        using (var context = Client(true)) remoteId = context.Issues.Single(i => i.RemoteSourceId == source.Id).RemoteIssueId!.Value;

        byte[]? page = await _client.GetClient(source.Id).GetPageBytesAsync(remoteId, 0);

        Assert.NotNull(page);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, page![..4]);
    }

    [Fact]
    public async Task ApasswordThatWasNotSaved_IsKeptOnlyInMemory_AndSyncStillWorks()
    {
        var source = await AddAsync(save: false);

        using (var context = Client(true))
        {
            Assert.Null(context.RemoteSources.Single().ProtectedPassword);
        }

        await _client.SyncAsync(source.Id);
        Assert.Equal(RemoteSourceState.Online, _client.StatusOf(source.Id).State);

        // A fresh service instance (an app restart) has no password and asks for one.
        using var restarted = new RemoteLibraryService(inc => Client(inc), _clientActivity);
        await restarted.SyncAsync(source.Id);
        Assert.Equal(RemoteSourceState.AuthFailed, restarted.StatusOf(source.Id).State);
    }
}

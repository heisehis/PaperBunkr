using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Preferences → Connections → Metron account (docs/superpowers/specs/2026-10-05-metron-account-sync-design.md): the switches
/// persist, everything starts off, and the buttons report what happened. The sync itself is covered in Data.Tests.
/// </summary>
public sealed class MetronSyncSettingsViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metron_prefs_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly Account _account = new();
    private bool _hasLogin = true;

    public MetronSyncSettingsViewModelTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Db();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Db() => new(_options);

    // The activity service is given a no-op recorder: its default writes each finished job to the real library database.
    private MetronSyncSettingsViewModel Vm() => new(Db, new ActivityService(action => action(), _ => { }),
        createSync: factory => _hasLogin ? new MetronAccountSync(factory, _account) : null,
        runInBackground: work => work());

    private AppSettings Settings()
    {
        using var context = Db();
        return context.GetOrCreateAppSettings();
    }

    /// <summary>An empty account that only records which calls were made.</summary>
    private sealed class Account : IMetronAccount
    {
        public List<string> Calls { get; } = new();

        public Task<IReadOnlyList<MetronPullListSeries>> GetPullListAsync(CancellationToken cancellationToken)
        {
            Calls.Add("pull:list");
            return Task.FromResult<IReadOnlyList<MetronPullListSeries>>(Array.Empty<MetronPullListSeries>());
        }

        public Task AddToPullListAsync(int seriesId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveFromPullListAsync(int seriesId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MetronCollectionItem> ScrobbleAsync(int issueId, DateTime readAtUtc, int? rating, CancellationToken cancellationToken) =>
            Task.FromResult(new MetronCollectionItem(1, issueId, true, readAtUtc, rating));

        public Task<MetronCollectionItem> AddToCollectionAsync(int issueId, CancellationToken cancellationToken) =>
            Task.FromResult(new MetronCollectionItem(1, issueId, false, null, null));

        public Task SetCollectionRatingAsync(int itemId, int? rating, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MetronCollectionPage> GetCollectionPageAsync(string? pageUrl, CancellationToken cancellationToken)
        {
            Calls.Add("collection:page");
            return Task.FromResult(new MetronCollectionPage(Array.Empty<MetronCollectionItem>(), null));
        }

        public Task<MetronWishListItem> AddToWishListAsync(int issueId, CancellationToken cancellationToken) =>
            Task.FromResult(new MetronWishListItem(1, issueId, "Wanted"));

        public Task<int?> AcquireWishListItemAsync(int itemId, CancellationToken cancellationToken) => Task.FromResult<int?>(null);

        public Task RemoveFromWishListAsync(int itemId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void EverythingStartsOff()
    {
        var vm = Vm();

        Assert.False(vm.IsEnabled || vm.SyncPullList || vm.SyncReading || vm.SyncCollection || vm.SyncWishList);
        var settings = Settings();
        Assert.False(settings.MetronSyncEnabled || settings.MetronSyncPullList || settings.MetronSyncReading || settings.MetronSyncCollection || settings.MetronSyncWishList);
    }

    [Fact]
    public void Switches_PersistAndReload()
    {
        var vm = Vm();
        vm.IsEnabled = true;
        vm.SyncPullList = true;
        vm.SyncCollection = true;
        vm.SyncWishList = true;

        var settings = Settings();
        Assert.True(settings.MetronSyncEnabled && settings.MetronSyncPullList && settings.MetronSyncCollection && settings.MetronSyncWishList);
        Assert.False(settings.MetronSyncReading);

        var reloaded = Vm();
        Assert.True(reloaded.IsEnabled && reloaded.SyncPullList && reloaded.SyncCollection && reloaded.SyncWishList);
    }

    [Fact]
    public void TheMasterSwitch_TurnsTheHourlyTaskOnAndOffWithIt()
    {
        var switched = new List<bool>();
        var vm = Vm();
        vm.SetScheduledTaskEnabled = switched.Add;

        vm.IsEnabled = true;
        vm.IsEnabled = false;
        vm.Refresh();                                            // re-reading the settings is not the user flipping the switch

        Assert.Equal(new[] { true, false }, switched);
    }

    [Fact]
    public void TurningReadingOn_StartsFromTheNewestRead_SoOldHistoryStaysPut()
    {
        int newest;
        using (var context = Db())
        {
            context.ReadingEvents.AddRange(
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = DateTime.UtcNow.AddDays(-9) },
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 2, Kind = ReadingEventKind.Finished, TimestampUtc = DateTime.UtcNow.AddDays(-1) });
            context.SaveChanges();
            newest = context.ReadingEvents.Max(e => e.Id);
        }

        Vm().SyncReading = true;

        var settings = Settings();
        Assert.True(settings.MetronSyncReading);
        Assert.Equal(newest, settings.MetronSyncReadingEventId);
    }

    [Fact]
    public async Task SyncNow_WithoutALogin_SaysSo_AndWhenSwitchedOff_SendsNothing()
    {
        _hasLogin = false;
        var noLogin = Vm();
        await noLogin.SyncNowCommand.ExecuteAsync(null);
        Assert.Equal(MetronAccountSyncRunner.NoLoginMessage, noLogin.StatusText);

        _hasLogin = true;
        var off = Vm();
        await off.SyncNowCommand.ExecuteAsync(null);
        Assert.Equal(MetronAccountSyncRunner.SwitchedOffMessage, off.StatusText);
        Assert.Empty(_account.Calls);
    }

    [Fact]
    public async Task SyncNow_RunsTheSync_AndShowsItsSummary()
    {
        var vm = Vm();
        vm.IsEnabled = true;
        vm.SyncPullList = true;

        await vm.SyncNowCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "pull:list" }, _account.Calls);
        Assert.Equal("Nothing to send", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SendReadingHistory_NeedsTheReadingSwitch_ThenRewindsToTheStart()
    {
        var vm = Vm();
        vm.IsEnabled = true;
        await vm.SendReadingHistoryCommand.ExecuteAsync(null);
        Assert.Contains("Reading switch", vm.StatusText);

        using (var context = Db())
        {
            context.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = DateTime.UtcNow });
            context.SaveChanges();
        }

        vm.SyncReading = true;                                      // watermark jumps to the newest read
        Assert.True(Settings().MetronSyncReadingEventId > 0);

        await vm.SendReadingHistoryCommand.ExecuteAsync(null);

        // Rewound to 0, then the run walked forward again past the one read (it has no Metron id, so nothing was sent).
        Assert.Equal("Nothing to send", vm.StatusText);
    }

    [Fact]
    public async Task ImportFromMetron_RunsEvenWithTheAreaSwitchesOff()
    {
        var vm = Vm();

        await vm.ImportFromMetronCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "collection:page" }, _account.Calls);
        Assert.Contains("Nothing in your Metron collection", vm.StatusText);
    }

    [Fact]
    public async Task TheScheduledTask_IsRegistered_AndDoesNothingWhileSwitchedOff()
    {
        var task = Assert.Single(ScheduledTaskCatalog.All, t => t.Id == ScheduledTaskCatalog.MetronSync);
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Priority).Distinct().Count());
        Assert.Equal(TimeSpan.FromHours(1), task.DefaultInterval);
        Assert.False(task.DefaultEnabled);                       // the master switch turns it on

        string result = await MetronAccountSyncRunner.RunScheduledAsync(Db, handle: null, CancellationToken.None);

        Assert.Equal(MetronAccountSyncRunner.SwitchedOffMessage, result);
    }
}

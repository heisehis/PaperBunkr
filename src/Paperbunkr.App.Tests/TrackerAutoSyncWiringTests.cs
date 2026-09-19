using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Wiring guard for docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md §4/§5:
/// every screen ViewModel receives its <see cref="ITrackerAutoSyncService"/> through an optional
/// constructor parameter that defaults to a no-op null object - so a forgotten <c>MainViewModel</c>
/// hand-off would silently disable a hook. This asserts the real service reaches every hooked screen,
/// and that <see cref="DispatcherToastHost"/> marshals off-thread toasts onto the UI thread.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TrackerAutoSyncWiringTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly CoverCacheTestRedirect _coverRedirect;

    public TrackerAutoSyncWiringTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_trackerwiring_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        _coverRedirect = new CoverCacheTestRedirect();

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        _coverRedirect.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static ITrackerAutoSyncService ServiceOf(object viewModel) =>
        (ITrackerAutoSyncService)viewModel.GetType()
            .GetField("_trackerAutoSync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel)!;

    [Fact]
    public void MainViewModel_HandsTheRealService_ToEveryHookedScreen()
    {
        var vm = new MainViewModel();

        Assert.IsType<TrackerAutoSyncService>(vm.TrackerAutoSync);
        Assert.Same(vm.TrackerAutoSync, ServiceOf(vm.Reader));
        Assert.Same(vm.TrackerAutoSync, ServiceOf(vm.Library));
        Assert.Same(vm.TrackerAutoSync, ServiceOf(vm.Reading));
        Assert.Same(vm.TrackerAutoSync, ServiceOf(vm.Detail.Tabs));
        Assert.Same(vm.TrackerAutoSync, ServiceOf(vm.MangaDetail));
        Assert.Same(vm.TrackerAutoSync, ServiceOf(vm.MangaDetail.Tabs));
        Assert.True(vm.MangaDetail.Tabs.IsMangaDetailHost);
        Assert.False(vm.Detail.Tabs.IsMangaDetailHost); // auto-open is manga-only
    }

    [Fact]
    public void ScreensBuiltWithoutTheService_GetTheNoOpNullObject_NotNull()
    {
        var reader = new ReaderScreenViewModel(() => { });

        Assert.Same(NoOpTrackerAutoSyncService.Instance, ServiceOf(reader));
    }

    [Fact]
    public void DispatcherToastHost_OffTheUiThread_PostsInsteadOfRunningInline()
    {
        var shown = new List<ToastRequest>();
        var posted = new List<Action>();
        var host = new DispatcherToastHost(shown.Add, _ => { }, isOnUiThread: () => false, post: posted.Add);
        var toast = new ToastRequest("t");

        host.Show(toast);

        Assert.Empty(shown); // not run inline on the calling (background) thread
        posted.Single()();
        Assert.Equal(new[] { toast }, shown);
    }

    [Fact]
    public void DispatcherToastHost_OnTheUiThread_RunsInline()
    {
        var shown = new List<ToastRequest>();
        var host = new DispatcherToastHost(shown.Add, _ => { }, isOnUiThread: () => true, post: _ => throw new InvalidOperationException("must not post"));

        host.Show(new ToastRequest("t"));

        Assert.Single(shown);
    }
}

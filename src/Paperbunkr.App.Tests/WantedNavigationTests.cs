using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Wanted screen's place in the app shell: rail order, Ctrl+Tab cycling, screen key. Isolated exactly like <c>MainViewModelTests</c>:
/// <c>MainViewModel</c> reads the per-user database and fires a background cover-cache reconcile in its constructor, so both are redirected to
/// temp locations (and the test joins <see cref="AvaloniaTestCollection"/> because the DB-path override is a shared static).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WantedNavigationTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly CoverCacheTestRedirect _coverRedirect;

    public WantedNavigationTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_wantednav_test_{Guid.NewGuid():N}.db");
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
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void GoWanted_ShowsTheWantedScreen()
    {
        var vm = new MainViewModel();

        vm.GoWantedCommand.Execute(null);

        Assert.True(vm.IsWanted);
        Assert.False(vm.IsHome);
        Assert.Same(vm.Wanted, vm.ActiveScreenContent);
        Assert.Equal("Wanted", vm.RootScreenLabel);
    }

    [Fact]
    public void Wanted_SitsBetweenContinuityAndPreferences_InTheScreenCycle()
    {
        var vm = new MainViewModel();
        vm.GoEventsCommand.Execute(null);

        vm.CycleScreenForwardCommand.Execute(null);
        Assert.True(vm.IsWanted);

        vm.CycleScreenForwardCommand.Execute(null);
        Assert.True(vm.IsPreferences);

        vm.CycleScreenBackCommand.Execute(null);
        Assert.True(vm.IsWanted);

        vm.CycleScreenBackCommand.Execute(null);
        Assert.True(vm.IsEvents);
    }

    [Fact]
    public void TheAcquisitionSection_IsReachableFromPreferences()
    {
        var vm = new MainViewModel();

        vm.GoPreferencesCommand.Execute(null);
        vm.Preferences.GoAcquisitionCommand.Execute(null);

        Assert.True(vm.IsPreferences);
        Assert.True(vm.Preferences.IsAcquisitionSection);
    }

    [Fact]
    public void TheDaemon_IsConstructedButNotRunning_UntilTheAppStartsIt()
    {
        var vm = new MainViewModel();

        Assert.NotNull(vm.Acquisition);
        Assert.NotNull(vm.AcquisitionBridge);
        Assert.NotNull(vm.AcquisitionEvents);
        // Constructing the view model must have no side effects on the network or database: nothing has been published.
        Assert.False(vm.AcquisitionEvents.Reader.TryRead(out _));
    }
}

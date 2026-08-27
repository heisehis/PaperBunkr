using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Preferences &gt; Libraries "Watch for changes" checkbox
/// (docs/superpowers/specs/2026-08-23-live-folder-watch-scanning-design.md §4) - the live
/// <c>FileSystemWatcher</c> behavior itself is already exercised against the real filesystem by
/// <c>Paperbunkr.App.Tests.LiveFolderWatchServiceTests</c> (a real watcher isn't something a mock
/// would add confidence over); what only an actual rendered window can confirm is that the checkbox
/// binds/renders correctly and that toggling it round-trips through <c>ToggleWatchCommand</c> into
/// the real per-user database, surviving an app restart.
/// </summary>
public class LiveFolderWatchToggleTests : IDisposable
{
    private readonly AppFixture _fixture = new();
    private readonly string _watchedFolderPath;

    public LiveFolderWatchToggleTests()
    {
        _watchedFolderPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_uitest_watchfolder_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_watchedFolderPath);

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_fixture.DbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.WatchedFolders.Add(new WatchedFolder { Path = _watchedFolderPath, Watch = false });
        context.SaveChanges();
    }

    public void Dispose()
    {
        _fixture.Dispose();
        try
        {
            if (Directory.Exists(_watchedFolderPath)) Directory.Delete(_watchedFolderPath, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private bool ReadWatchFlag()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_fixture.DbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        return context.WatchedFolders.Single(w => w.Path == _watchedFolderPath).Watch;
    }

    [Fact]
    public void WatchCheckBox_RendersUnchecked_ForASeededUnwatchedFolder()
    {
        var window = _fixture.Window;
        window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesRailButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesLibrariesTabButton"))!.AsButton().Invoke();

        var checkBox = window.FindFirstDescendant(cf => cf.ByAutomationId("WatchedFolderWatchCheckBox"))!.AsCheckBox();

        Assert.Equal(ToggleState.Off, checkBox.ToggleState);
        Assert.False(ReadWatchFlag());
    }

    [Fact]
    public void TogglingWatchCheckBox_PersistsToDatabase_AndSurvivesRestart()
    {
        var window = _fixture.Window;
        window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesRailButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesLibrariesTabButton"))!.AsButton().Invoke();

        var checkBox = window.FindFirstDescendant(cf => cf.ByAutomationId("WatchedFolderWatchCheckBox"))!.AsCheckBox();
        checkBox.Toggle();

        Assert.Equal(ToggleState.On, checkBox.ToggleState);
        Assert.True(ReadWatchFlag());

        _fixture.Restart();
        window = _fixture.Window;
        window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesRailButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesLibrariesTabButton"))!.AsButton().Invoke();

        var checkBoxAfterRestart = window.FindFirstDescendant(cf => cf.ByAutomationId("WatchedFolderWatchCheckBox"))!.AsCheckBox();
        Assert.Equal(ToggleState.On, checkBoxAfterRestart.ToggleState);
    }
}

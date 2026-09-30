using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels.Home;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Preferences › Appearance › Home (docs/superpowers/specs/2026-09-28-home-improvements-design.md I1/I5, cosmetics C10): reorder,
/// hide, reset, the seasonal switch and Unhide all persist to the database immediately.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomePreferencesViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_prefs_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public HomePreferencesViewModelTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Context();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext Context() => new(_options);

    private HomePreferencesViewModel Loaded()
    {
        var vm = new HomePreferencesViewModel(Context);
        vm.Load();
        return vm;
    }

    private AppSettings Saved()
    {
        using var context = Context();
        return context.GetOrCreateAppSettings();
    }

    [Fact]
    public void PreferencesSearch_FindsTheHomeTab()
    {
        var entry = PreferenceIndex.Find("appearance.home");
        Assert.NotNull(entry);
        Assert.Equal(PreferencesSection.Appearance, entry!.Section);
        Assert.Contains("not interested", entry.Keywords);
    }

    [Fact]
    public void Load_ShowsEverySectionInTheDefaultOrder()
    {
        var vm = Loaded();

        Assert.Equal(HomeSectionKey.Default, vm.Sections.Select(s => s.Key));
        Assert.All(vm.Sections, s => Assert.True(s.IsVisible));
        Assert.False(vm.SeasonalFlourish);
    }

    [Fact]
    public void MoveDown_AndHide_PersistImmediately()
    {
        var vm = Loaded();

        vm.MoveDownCommand.Execute(vm.Sections[0]);   // spotlight below needs attention
        TestDispatcher.Drain();
        vm.Sections.Single(s => s.Key == HomeSectionKey.Collections).IsVisible = false;

        var layout = HomeLayout.Resolve(Saved().HomeSectionOrder, Saved().HomeHiddenSections);
        Assert.Equal(HomeSectionKey.NeedsAttention, layout.Order[0]);
        Assert.Equal(HomeSectionKey.Spotlight, layout.Order[1]);
        Assert.DoesNotContain(HomeSectionKey.Collections, layout.Visible);
    }

    [Fact]
    public void MoveTo_ReordersLikeADragAndDrop()
    {
        var vm = Loaded();
        var readingList = vm.Sections.Single(s => s.Key == HomeSectionKey.ReadingList);

        vm.MoveTo(readingList, vm.Sections[0]);
        TestDispatcher.Drain();

        Assert.Equal(HomeSectionKey.ReadingList, vm.Sections[0].Key);
        Assert.StartsWith(HomeSectionKey.ReadingList, Saved().HomeSectionOrder);
    }

    [Fact]
    public void Reset_ClearsTheSavedLayout()
    {
        var vm = Loaded();
        vm.MoveUpCommand.Execute(vm.Sections[^1]);
        TestDispatcher.Drain();
        vm.Sections[0].IsVisible = false;
        Assert.NotNull(Saved().HomeSectionOrder);

        vm.ResetLayoutCommand.Execute(null);

        Assert.Null(Saved().HomeSectionOrder);
        Assert.Null(Saved().HomeHiddenSections);
        Assert.Equal(HomeSectionKey.Default, vm.Sections.Select(s => s.Key));
    }

    [Fact]
    public void SeasonalSwitch_Persists()
    {
        var vm = Loaded();

        vm.SeasonalFlourish = true;

        Assert.True(Saved().HomeSeasonalFlourish);
        Assert.True(Loaded().SeasonalFlourish);
    }

    [Fact]
    public void Unhide_RestoresTheRecommendation_AndDropsTheRow()
    {
        int seriesId;
        using (var context = Context())
        {
            var series = new Series { Name = "Hidden One" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
            DismissedRecommendations.Dismiss(context, seriesId, DateTime.UtcNow);
        }

        var vm = Loaded();
        var row = Assert.Single(vm.HiddenRecommendations);
        Assert.Equal("Hidden One", row.SeriesName);
        Assert.True(vm.HasHiddenRecommendations);

        vm.UnhideCommand.Execute(row);
        TestDispatcher.Drain();

        Assert.Empty(vm.HiddenRecommendations);
        Assert.False(vm.HasHiddenRecommendations);
        using var check = Context();
        Assert.Empty(DismissedRecommendations.GetIds(check));
    }
}

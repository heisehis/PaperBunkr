using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Library sidebar's real content-type/collection filtering (docs/superpowers/specs/
/// 2026-08-09-library-sidebar-categorization-design.md) - previously untested, since the sidebar
/// rows were plain non-interactive counts. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// to a temp SQLite file, matching <see cref="SmartScreenViewModelTests"/>'s pattern -
/// <see cref="LibraryScreenViewModel"/> has no injected context-factory seam. Runs under
/// <see cref="AvaloniaTestCollection"/> since cover brushes/images need a real Skia platform.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static int CreateSeries(string name, ContentType contentType)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = contentType };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static int CreateSeriesWithIssue(string name, bool fileIsMissing = false, int? lastPageRead = null, string? publisher = null, string? genre = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = ContentType.Comic, Publisher = publisher, Genre = genre };
        context.Series.Add(series);
        context.SaveChanges();

        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FileIsMissing = fileIsMissing, LastPageRead = lastPageRead });
        context.SaveChanges();
        return series.Id;
    }

    private static void AddTrackingLink(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.TrackingLinks.Add(new TrackingLink { SeriesId = seriesId, Service = TrackingService.AniList, ExternalId = "123" });
        context.SaveChanges();
    }

    private static int CreateCategoryWithSeries(string name, params int[] seriesIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var category = new Category { Name = name };
        foreach (var seriesId in seriesIds)
        {
            category.Series.Add(context.Series.First(s => s.Id == seriesId));
        }

        context.Categories.Add(category);
        context.SaveChanges();
        return category.Id;
    }

    [Fact]
    public void LoadFromDatabase_GroupsContentTypes_SkippingEmptyBuckets()
    {
        CreateSeries("Comic One", ContentType.Comic);
        CreateSeries("Comic Two", ContentType.Comic);
        CreateSeries("Manga One", ContentType.Manga);

        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        Assert.Equal(2, vm.ContentTypes.Count); // only Comic + Manga - no Manhua/Manhwa/Unknown rows
        Assert.Equal(2, vm.ContentTypes.Single(c => c.ContentType == ContentType.Comic).Count);
        Assert.Equal(1, vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga).Count);
    }

    [Fact]
    public void LoadFromDatabase_Collections_ReflectsRealCategoryRows()
    {
        int seriesId = CreateSeries("Series A", ContentType.Comic);
        CreateCategoryWithSeries("Favorites", seriesId);

        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        var collection = Assert.Single(vm.Collections);
        Assert.Equal("Favorites", collection.Name);
        Assert.Equal(1, collection.Count);
        Assert.True(vm.HasCollections);
    }

    [Fact]
    public void Collections_Empty_ReportsHasCollectionsFalse()
    {
        CreateSeries("Series A", ContentType.Comic);

        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        Assert.Empty(vm.Collections);
        Assert.False(vm.HasCollections);
    }

    [Fact]
    public void SelectContentType_FiltersCovers_AndSetsIsActive()
    {
        CreateSeries("Comic One", ContentType.Comic);
        CreateSeries("Manga One", ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });
        var mangaBucket = vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga);

        vm.SelectContentTypeCommand.Execute(mangaBucket);

        var cover = Assert.Single(vm.Covers);
        Assert.Equal("Manga One", cover.Name);
        Assert.False(vm.IsAllSeriesActive);
        Assert.True(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga).IsActive);
        Assert.False(vm.ContentTypes.Single(c => c.ContentType == ContentType.Comic).IsActive);
    }

    [Fact]
    public void SelectCollection_FiltersCovers_AndSetsIsActive()
    {
        int seriesAId = CreateSeries("Series A", ContentType.Comic);
        CreateSeries("Series B", ContentType.Comic);
        CreateCategoryWithSeries("Favorites", seriesAId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });
        var favorites = vm.Collections.Single();

        vm.SelectCollectionCommand.Execute(favorites);

        var cover = Assert.Single(vm.Covers);
        Assert.Equal("Series A", cover.Name);
        Assert.False(vm.IsAllSeriesActive);
        Assert.True(vm.Collections.Single().IsActive);
    }

    [Fact]
    public void SelectAllSeries_ClearsFilter_RestoresFullCovers()
    {
        CreateSeries("Comic One", ContentType.Comic);
        CreateSeries("Manga One", ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));
        Assert.Single(vm.Covers);

        vm.SelectAllSeriesCommand.Execute(null);

        Assert.Equal(2, vm.Covers.Count);
        Assert.True(vm.IsAllSeriesActive);
        Assert.All(vm.ContentTypes, c => Assert.False(c.IsActive));
    }

    [Fact]
    public void SetViewMode_UpdatesIsXProperties()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);

        Assert.True(vm.IsTilesView);
        Assert.False(vm.IsComfortableGrid);
        Assert.False(vm.IsCompactGrid);
        Assert.False(vm.IsCoverOnlyGrid);
        Assert.False(vm.IsPanoramaGrid);
        Assert.False(vm.IsListView);
        Assert.False(vm.IsDetailsView);
        Assert.Equal("Tiles", vm.DisplayModeLabel);
    }

    [Fact]
    public void GridDensity_ClampsToRange()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.GridDensity = 5.0;
        Assert.Equal(1.6, vm.GridDensity);

        vm.GridDensity = 0.1;
        Assert.Equal(0.6, vm.GridDensity);
    }

    [Fact]
    public void SearchQuery_FiltersByNamePublisherGenre()
    {
        CreateSeriesWithIssue("Amazing Spider-Man", publisher: "Marvel");
        CreateSeriesWithIssue("Batman", publisher: "DC", genre: "Noir");
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.SearchQuery = "spider";
        Assert.Equal("Amazing Spider-Man", Assert.Single(vm.Covers).Name);

        vm.SearchQuery = "marvel"; // matches Publisher, not Name
        Assert.Equal("Amazing Spider-Man", Assert.Single(vm.Covers).Name);

        vm.SearchQuery = "noir"; // matches Genre
        Assert.Equal("Batman", Assert.Single(vm.Covers).Name);

        vm.SearchQuery = "";
        Assert.Equal(2, vm.Covers.Count);
    }

    [Fact]
    public void FilterUnreadOnly_NarrowsCovers()
    {
        CreateSeriesWithIssue("Read Series", lastPageRead: 5);
        CreateSeriesWithIssue("Unread Series", lastPageRead: null);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.FilterUnreadOnly = true;

        Assert.Equal("Unread Series", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void FilterMissingIssues_NarrowsCovers()
    {
        CreateSeriesWithIssue("Has All Files", fileIsMissing: false);
        CreateSeriesWithIssue("Missing A File", fileIsMissing: true);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.FilterMissingIssues = true;

        Assert.Equal("Missing A File", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void FilterTrackedOnly_NarrowsCovers()
    {
        int trackedId = CreateSeriesWithIssue("Tracked Series");
        CreateSeriesWithIssue("Untracked Series");
        AddTrackingLink(trackedId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.FilterTrackedOnly = true;

        Assert.Equal("Tracked Series", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void CombinedSearchAndFilters_AllApplyTogether()
    {
        int trackedId = CreateSeriesWithIssue("Marvel Hero", publisher: "Marvel", lastPageRead: null);
        CreateSeriesWithIssue("Marvel Villain", publisher: "Marvel", lastPageRead: 3); // read - excluded by unread filter
        CreateSeriesWithIssue("DC Hero", publisher: "DC", lastPageRead: null); // excluded by search text
        AddTrackingLink(trackedId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { });

        vm.SearchQuery = "marvel";
        vm.FilterUnreadOnly = true;
        vm.FilterTrackedOnly = true;

        Assert.Equal("Marvel Hero", Assert.Single(vm.Covers).Name);
    }
}

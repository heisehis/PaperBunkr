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
}

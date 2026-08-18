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

    private static int CreateSeriesWithIssue(string name, bool fileIsMissing = false, int? lastPageRead = null,
        string? publisher = null, string? genre = null, ContentType contentType = ContentType.Comic,
        DateTime? addedTime = null, DateTime? openedTime = null, long? fileSize = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = contentType, Publisher = publisher, Genre = genre };
        context.Series.Add(series);
        context.SaveChanges();

        context.Issues.Add(new Issue
        {
            SeriesId = series.Id,
            Number = "1",
            FileIsMissing = fileIsMissing,
            LastPageRead = lastPageRead,
            AddedTime = addedTime,
            OpenedTime = openedTime,
            FileSize = fileSize,
        });
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

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(2, vm.ContentTypes.Count); // only Comic + Manga - no Manhua/Manhwa/Unknown rows
        Assert.Equal(2, vm.ContentTypes.Single(c => c.ContentType == ContentType.Comic).Count);
        Assert.Equal(1, vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga).Count);
    }

    [Fact]
    public void LoadFromDatabase_Collections_ReflectsRealCategoryRows()
    {
        int seriesId = CreateSeries("Series A", ContentType.Comic);
        CreateCategoryWithSeries("Favorites", seriesId);

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        var collection = Assert.Single(vm.Collections);
        Assert.Equal("Favorites", collection.Name);
        Assert.Equal(1, collection.Count);
        Assert.True(vm.HasCollections);
    }

    [Fact]
    public void Collections_Empty_ReportsHasCollectionsFalse()
    {
        CreateSeries("Series A", ContentType.Comic);

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Empty(vm.Collections);
        Assert.False(vm.HasCollections);
    }

    [Fact]
    public void SelectContentType_FiltersCovers_AndSetsIsActive()
    {
        CreateSeries("Comic One", ContentType.Comic);
        CreateSeries("Manga One", ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterUnreadOnly = true;

        Assert.Equal("Unread Series", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void FilterMissingIssues_NarrowsCovers()
    {
        CreateSeriesWithIssue("Has All Files", fileIsMissing: false);
        CreateSeriesWithIssue("Missing A File", fileIsMissing: true);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterMissingIssues = true;

        Assert.Equal("Missing A File", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void FilterTrackedOnly_NarrowsCovers()
    {
        int trackedId = CreateSeriesWithIssue("Tracked Series");
        CreateSeriesWithIssue("Untracked Series");
        AddTrackingLink(trackedId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

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
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "marvel";
        vm.FilterUnreadOnly = true;
        vm.FilterTrackedOnly = true;

        Assert.Equal("Marvel Hero", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void SortField_Name_OrdersAlphabetically()
    {
        CreateSeriesWithIssue("Zebra");
        CreateSeriesWithIssue("Apple");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.Name;
        vm.SortDirection = SortDirection.Ascending;

        Assert.Equal(new[] { "Apple", "Zebra" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SortField_DateAdded_OrdersByMostRecentIssueAddedTime()
    {
        CreateSeriesWithIssue("Older", addedTime: new DateTime(2026, 1, 1));
        CreateSeriesWithIssue("Newer", addedTime: new DateTime(2026, 6, 1));
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.DateAdded; // default direction: Descending
        Assert.Equal(new[] { "Newer", "Older" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SortField_LastRead_OrdersByMostRecentIssueOpenedTime()
    {
        CreateSeriesWithIssue("Read Long Ago", openedTime: new DateTime(2026, 1, 1));
        CreateSeriesWithIssue("Read Recently", openedTime: new DateTime(2026, 6, 1));
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.LastRead;
        Assert.Equal(new[] { "Read Recently", "Read Long Ago" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SortField_Size_OrdersByTotalFileSize()
    {
        CreateSeriesWithIssue("Small", fileSize: 100);
        CreateSeriesWithIssue("Large", fileSize: 900);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.Size;
        Assert.Equal(new[] { "Large", "Small" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SortField_IssueCount_OrdersByIssueCount()
    {
        CreateSeriesWithIssue("One Issue");
        int twoIssueId = CreateSeriesWithIssue("Two Issues");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.Add(new Issue { SeriesId = twoIssueId, Number = "2" });
            context.SaveChanges();
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SortField = LibrarySortField.IssueCount;

        Assert.Equal(new[] { "Two Issues", "One Issue" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SortField_UnreadCount_OrdersByUnreadCount()
    {
        CreateSeriesWithIssue("Fully Read", lastPageRead: 5);
        CreateSeriesWithIssue("Unread", lastPageRead: null);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.UnreadCount;

        Assert.Equal(new[] { "Unread", "Fully Read" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SortField_Publisher_OrdersAlphabeticallyByPublisher()
    {
        CreateSeriesWithIssue("Marvel Book", publisher: "Marvel");
        CreateSeriesWithIssue("DC Book", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.Publisher;
        vm.SortDirection = SortDirection.Ascending;

        Assert.Equal(new[] { "DC Book", "Marvel Book" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void GroupField_ContentType_PartitionsIntoGroups()
    {
        CreateSeriesWithIssue("A Comic", contentType: ContentType.Comic);
        CreateSeriesWithIssue("A Manga", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.GroupField = LibraryGroupField.ContentType;

        Assert.True(vm.IsGrouped);
        Assert.Empty(vm.Covers);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal("Comic", vm.Groups[0].Header);
        Assert.Equal("A Comic", Assert.Single(vm.Groups[0].Items).Name);
        Assert.Equal("Manga", vm.Groups[1].Header);
        Assert.Equal("A Manga", Assert.Single(vm.Groups[1].Items).Name);
    }

    [Fact]
    public void GroupField_Publisher_PartitionsIntoGroups()
    {
        CreateSeriesWithIssue("Marvel Book", publisher: "Marvel");
        CreateSeriesWithIssue("DC Book", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.GroupField = LibraryGroupField.Publisher;

        Assert.Equal(new[] { "DC", "Marvel" }, vm.Groups.Select(g => g.Header));
    }

    [Fact]
    public void GroupField_Alphabetical_PartitionsByFirstLetter()
    {
        CreateSeriesWithIssue("Batman");
        CreateSeriesWithIssue("Superman");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.GroupField = LibraryGroupField.Alphabetical;

        Assert.Equal(new[] { "B", "S" }, vm.Groups.Select(g => g.Header));
    }

    [Fact]
    public void Sort_AppliesWithinGroups_NotJustAcrossThem()
    {
        CreateSeriesWithIssue("Zebra Comic", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Apple Comic", contentType: ContentType.Comic);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SortField = LibrarySortField.Name;
        vm.SortDirection = SortDirection.Ascending;
        vm.GroupField = LibraryGroupField.ContentType;

        var group = Assert.Single(vm.Groups);
        Assert.Equal(new[] { "Apple Comic", "Zebra Comic" }, group.Items.Select(c => c.Name));
    }

    [Fact]
    public void ContinueReadingCommand_NavigatesToFirstUnreadIssue()
    {
        int seriesId = CreateSeriesWithIssue("Ongoing Series", lastPageRead: 10); // issue "1", already read
        int unreadIssueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var unreadIssue = new Issue { SeriesId = seriesId, Number = "2", LastPageRead = null };
            context.Issues.Add(unreadIssue);
            context.SaveChanges();
            unreadIssueId = unreadIssue.Id;
        }

        int? navigatedIssueId = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: id => navigatedIssueId = id, goToNewIssueProperties: (_, _, _) => { });
        var card = vm.Covers.Single();

        vm.ContinueReadingCommand.Execute(card);

        Assert.Equal(unreadIssueId, navigatedIssueId);
    }

    [Fact]
    public void ContinueReadingIssueId_NullWhenAllIssuesRead()
    {
        CreateSeriesWithIssue("Fully Read Series", lastPageRead: 10);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        var card = vm.Covers.Single();

        Assert.Null(card.ContinueReadingIssueId);
        Assert.False(card.HasContinueReading);
    }

    [Fact]
    public void HasAnyResults_FalseWhenSearchMatchesNothing()
    {
        CreateSeriesWithIssue("Some Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        Assert.True(vm.HasAnyResults);

        vm.SearchQuery = "nothing matches this";

        Assert.Empty(vm.Covers);
        Assert.False(vm.HasAnyResults);
    }

    [Fact]
    public void HasAnyResults_TrueWhenGroupedResultsExist_EvenThoughCoversIsEmpty()
    {
        CreateSeriesWithIssue("Some Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.GroupField = LibraryGroupField.ContentType;

        Assert.Empty(vm.Covers); // grouped mode populates Groups, not Covers
        Assert.True(vm.HasAnyResults);
    }

    /// <summary>docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §2/§3 - the manual "add a physical book" entry point.</summary>
    [Fact]
    public void CreatePlaceholderIssue_InvokesCallbackWithNewIssueAndSeriesIds_WasCreatedTrue()
    {
        (int IssueId, int SeriesId, bool DeleteIfUnedited)? captured = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { },
            goToNewIssueProperties: (issueId, seriesId, deleteIfUnedited) => captured = (issueId, seriesId, deleteIfUnedited));
        vm.NewIssueSeriesName = "Brand New Physical Series";
        vm.NewIssueNumber = "1";

        vm.CreatePlaceholderIssueCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.True(captured!.Value.DeleteIfUnedited);
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.First(i => i.Id == captured.Value.IssueId);
        Assert.True(issue.IsPlaceholder);
        Assert.Equal(captured.Value.SeriesId, issue.SeriesId);
    }

    [Fact]
    public void CreatePlaceholderIssue_MatchingExistingSeries_ResolvesToExistingSeries_NoDuplicate()
    {
        int existingSeriesId = CreateSeries("Kilo Station", ContentType.Comic);
        (int IssueId, int SeriesId, bool DeleteIfUnedited)? captured = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { },
            goToNewIssueProperties: (issueId, seriesId, deleteIfUnedited) => captured = (issueId, seriesId, deleteIfUnedited));
        vm.NewIssueSeriesName = "Kilo Station";
        vm.NewIssueNumber = "1";

        vm.CreatePlaceholderIssueCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Equal(existingSeriesId, captured!.Value.SeriesId);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(1, context.Series.Count(s => s.Name == "Kilo Station")); // no duplicate series created
    }

    [Fact]
    public void CreatePlaceholderIssue_MatchingExistingIssue_WasCreatedFalse_DoesNotFlagDeletable()
    {
        CreateSeriesWithIssue("Existing Book"); // creates Series "Existing Book" + Issue Number "1"
        (int IssueId, int SeriesId, bool DeleteIfUnedited)? captured = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { },
            goToNewIssueProperties: (issueId, seriesId, deleteIfUnedited) => captured = (issueId, seriesId, deleteIfUnedited));
        vm.NewIssueSeriesName = "Existing Book";
        vm.NewIssueNumber = "1";

        vm.CreatePlaceholderIssueCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.False(captured!.Value.DeleteIfUnedited);
    }

    // ===================== Content Type / Reading Direction (docs/superpowers/specs/
    // 2026-08-16-manga-content-type-classification-design.md) =====================

    [Fact]
    public void SetSeriesContentTypeManga_PersistsToTheSeries()
    {
        int seriesId = CreateSeries("A Series", ContentType.Comic);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var card = vm.Covers.Single(c => c.SeriesId == seriesId);

        vm.SetSeriesContentTypeMangaCommand.Execute(card);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ContentType.Manga, context.Series.First(s => s.Id == seriesId).ContentType);
    }

    [Fact]
    public void SetSeriesReadingModeRightToLeft_PersistsToTheSeries()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var card = vm.Covers.Single(c => c.SeriesId == seriesId);

        vm.SetSeriesReadingModeRightToLeftCommand.Execute(card);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingMode.RightToLeft, context.Series.First(s => s.Id == seriesId).ReadingMode);
    }

    [Fact]
    public void CreatePlaceholderIssue_NewSeries_AppliesPickedContentTypeAndReadingMode()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.NewIssueSeriesName = "Brand New Manga";
        vm.NewIssueNumber = "1";
        vm.NewIssueContentType = ContentType.Manga;
        vm.NewIssueReadingMode = ReadingMode.RightToLeft;

        vm.CreatePlaceholderIssueCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.First(s => s.Name == "Brand New Manga");
        Assert.Equal(ContentType.Manga, series.ContentType);
        Assert.Equal(ReadingMode.RightToLeft, series.ReadingMode);
    }

    /// <summary>
    /// The picker's default (Comic, unvisited) must never reset an existing matched series' real
    /// classification - only a newly-created series ever gets the picker's value applied (docs/
    /// superpowers/specs/2026-08-16-manga-content-type-classification-design.md §3).
    /// </summary>
    [Fact]
    public void CreatePlaceholderIssue_MatchingExistingSeries_NeverOverwritesItsExistingContentType()
    {
        CreateSeries("Kilo Station", ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.NewIssueSeriesName = "Kilo Station";
        vm.NewIssueNumber = "1"; // matches CreateSeriesWithIssue's own default Number in other tests, but this series has no issue yet

        vm.CreatePlaceholderIssueCommand.Execute(null); // picker left at its Comic default

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ContentType.Manga, context.Series.First(s => s.Name == "Kilo Station").ContentType);
    }

    // --- Saved List Layouts (docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md) ---

    private static void SeedAppSettings(Action<AppSettings> configure)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        configure(settings);
        context.SaveChanges();
    }

    private static AppSettings ReadAppSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings();
    }

    [Fact]
    public void Construct_ReflectsNonDefaultAppSettingsImmediately()
    {
        int categoryId = CreateCategoryWithSeries("Owned", CreateSeries("Owned Series", ContentType.Comic));
        SeedAppSettings(settings =>
        {
            settings.LibrarySortField = LibrarySortField.Publisher;
            settings.LibrarySortDirection = SortDirection.Ascending;
            settings.LibraryGroupField = LibraryGroupField.Publisher;
            settings.LibraryViewMode = LibraryViewMode.List;
            settings.LibraryGridDensity = 1.3;
            settings.LibraryShowUnreadBadge = false;
            settings.LibraryShowPublisherBadge = true;
            settings.LibraryShowLanguageBadge = true;
            settings.LibraryUseLanguageIcon = true;
            settings.LibraryShowContinueReadingButton = true;
            settings.LibrarySearchQuery = "kilo";
            settings.LibraryActiveCategoryId = categoryId;
            settings.LibraryFilterUnreadOnly = true;
            settings.LibraryFilterMissingIssues = true;
            settings.LibraryFilterTrackedOnly = true;
        });

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(LibrarySortField.Publisher, vm.SortField);
        Assert.Equal(SortDirection.Ascending, vm.SortDirection);
        Assert.Equal(LibraryGroupField.Publisher, vm.GroupField);
        Assert.Equal(LibraryViewMode.List, vm.ViewMode);
        Assert.Equal(1.3, vm.GridDensity);
        Assert.False(vm.ShowUnreadBadge);
        Assert.True(vm.ShowPublisherBadge);
        Assert.True(vm.ShowLanguageBadge);
        Assert.True(vm.UseLanguageIcon);
        Assert.True(vm.ShowContinueReadingButton);
        Assert.Equal("kilo", vm.SearchQuery);
        Assert.True(vm.FilterUnreadOnly);
        Assert.True(vm.FilterMissingIssues);
        Assert.True(vm.FilterTrackedOnly);
        Assert.False(vm.IsAllSeriesActive);
        Assert.Contains(vm.Collections, c => c.Id == categoryId && c.IsActive);
    }

    [Fact]
    public void SortFieldAndDirection_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetSortFieldCommand.Execute(LibrarySortField.Size);
        vm.ToggleSortDirectionCommand.Execute(null);

        var settings = ReadAppSettings();
        Assert.Equal(LibrarySortField.Size, settings.LibrarySortField);
        Assert.Equal(SortDirection.Ascending, settings.LibrarySortDirection);
    }

    [Fact]
    public void GroupFieldAndViewModeAndGridDensity_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetGroupFieldCommand.Execute(LibraryGroupField.Alphabetical);
        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);
        vm.GridDensity = 0.8;

        var settings = ReadAppSettings();
        Assert.Equal(LibraryGroupField.Alphabetical, settings.LibraryGroupField);
        Assert.Equal(LibraryViewMode.Tiles, settings.LibraryViewMode);
        Assert.Equal(0.8, settings.LibraryGridDensity);
    }

    [Fact]
    public void OverlayBadgeToggles_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.ShowUnreadBadge = false;
        vm.ShowPublisherBadge = true;
        vm.ShowLanguageBadge = true;
        vm.UseLanguageIcon = true;
        vm.ShowContinueReadingButton = true;

        var settings = ReadAppSettings();
        Assert.False(settings.LibraryShowUnreadBadge);
        Assert.True(settings.LibraryShowPublisherBadge);
        Assert.True(settings.LibraryShowLanguageBadge);
        Assert.True(settings.LibraryUseLanguageIcon);
        Assert.True(settings.LibraryShowContinueReadingButton);
    }

    [Fact]
    public void SearchQueryAndFilterCheckboxes_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "saga";
        vm.FilterUnreadOnly = true;
        vm.FilterMissingIssues = true;
        vm.FilterTrackedOnly = true;

        var settings = ReadAppSettings();
        Assert.Equal("saga", settings.LibrarySearchQuery);
        Assert.True(settings.LibraryFilterUnreadOnly);
        Assert.True(settings.LibraryFilterMissingIssues);
        Assert.True(settings.LibraryFilterTrackedOnly);
    }

    [Fact]
    public void SearchQuery_ClearedToEmpty_PersistsAsNullNotEmptyString()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "saga";
        vm.SearchQuery = string.Empty;

        Assert.Null(ReadAppSettings().LibrarySearchQuery);
    }

    [Fact]
    public void SelectContentType_PersistsActiveContentType_ClearsActiveCategory()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        CreateCategoryWithSeries("Owned", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));

        var settings = ReadAppSettings();
        Assert.Equal(ContentType.Manga, settings.LibraryActiveContentType);
        Assert.Null(settings.LibraryActiveCategoryId);
    }

    [Fact]
    public void SelectCollection_PersistsActiveCategory_ClearsActiveContentType()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        int categoryId = CreateCategoryWithSeries("Owned", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single());

        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Id == categoryId));

        var settings = ReadAppSettings();
        Assert.Equal(categoryId, settings.LibraryActiveCategoryId);
        Assert.Null(settings.LibraryActiveContentType);
    }

    [Fact]
    public void SelectAllSeries_ClearsBothActiveFilters_PersistsToAppSettings()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        CreateCategoryWithSeries("Owned", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        vm.SelectAllSeriesCommand.Execute(null);

        var settings = ReadAppSettings();
        Assert.Null(settings.LibraryActiveContentType);
        Assert.Null(settings.LibraryActiveCategoryId);
    }

    [Fact]
    public void Construct_StaleActiveCategoryId_FallsBackToAllSeries()
    {
        int realCategoryId = CreateCategoryWithSeries("Owned", CreateSeries("A Series", ContentType.Comic));
        SeedAppSettings(settings => settings.LibraryActiveCategoryId = realCategoryId + 999999);

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.True(vm.IsAllSeriesActive);
        Assert.DoesNotContain(vm.Collections, c => c.IsActive);
    }
}

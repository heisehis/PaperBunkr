using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Library sidebar's real content-type/collection filtering (docs/superpowers/specs/
/// 2026-08-09-library-sidebar-categorization-design.md) and two independent data pipelines: the
/// per-issue one every Display mode shared exclusively after docs/superpowers/specs/
/// 2026-08-18-library-book-centric-redesign-design.md's Slice 3 (<see cref="LibraryScreenViewModel.IssueList"/>),
/// and the original per-series aggregated-card one (<c>Covers</c>/<c>Groups</c>/<c>SortField</c>/
/// <c>GroupField</c>), restored the same session as a real, switchable option via
/// <see cref="LibraryContentGranularity"/> rather than staying permanently superseded. Both are
/// always computed on every <see cref="LibraryScreenViewModel.LoadFromDatabase"/> call regardless
/// of which <see cref="LibraryScreenViewModel.Granularity"/> is active - see that method's doc
/// comment. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
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

    /// <summary>Every Display mode is per-issue now, so a series with no issues contributes no
    /// rows at all (matching CE's real book-list model) - tests that need a visible tile use this
    /// helper, not the bare <see cref="CreateSeries"/> above.</summary>
    private static int CreateSeriesWithIssue(string name, bool fileIsMissing = false, int? lastPageRead = null,
        string? publisher = null, string? genre = null, ContentType contentType = ContentType.Comic,
        DateTime? addedTime = null, DateTime? openedTime = null, long? fileSize = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = contentType, Publisher = publisher, Genre = genre };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue
        {
            SeriesId = series.Id,
            Number = "1",
            FileIsMissing = fileIsMissing,
            LastPageRead = lastPageRead,
            AddedTime = addedTime,
            OpenedTime = openedTime,
            FileSize = fileSize,
            // IssueListRow reads Publisher/Genre from the Issue, not the Series (docs/superpowers/
            // specs/2026-08-18-library-book-centric-redesign-design.md Slice 3) - set on both so
            // both series-level search (MatchesSearch's default fields) and issue-level sort/group
            // (IssueListFieldCatalog) see the same value.
            Publisher = publisher,
        };
        if (genre is not null)
        {
            issue.MergeFrom(IssueTagField.Genre, new[] { genre });
        }

        context.Issues.Add(issue);
        context.SaveChanges();
        return series.Id;
    }

    private static void AddTrackingLink(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.TrackingLinks.Add(new TrackingLink { SeriesId = seriesId, Service = TrackingService.AniList, ExternalId = "123" });
        context.SaveChanges();
    }

    private static int CreateCollectionWithSeries(string name, params int[] seriesIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var collection = new Collection { Name = name };
        int order = 0;
        foreach (var seriesId in seriesIds)
        {
            collection.Items.Add(new CollectionItem { SeriesId = seriesId, SortOrder = order++ });
        }

        context.Collections.Add(collection);
        context.SaveChanges();
        return collection.Id;
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
    public async Task ResyncSeriesFromFileCommand_EmbeddedSeriesDisagrees_ReassignsAndReloadsGrid()
    {
        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_vm_resync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string cbzPath = Path.Combine(root, "will-of-iron-05.cbz");
            var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo { Series = "Warhammer 40,000: Will of Iron" };
            CbzFixture.Create(cbzPath, pageCount: 1, embedded);

            using (var context = PaperbunkrDb.CreateContext())
            {
                var series = new Series { Name = "Warhammer 40" };
                context.Series.Add(series);
                context.Issues.Add(new Issue { Series = series, FilePath = cbzPath });
                context.SaveChanges();
            }

            (string Title, string Message)? toast = null;
            var vm = new LibraryScreenViewModel(
                goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
                showToast: (title, message) => toast = (title, message));

            await vm.ResyncSeriesFromFileCommand.ExecuteAsync(null);

            Assert.False(vm.IsResyncingSeries);
            Assert.NotNull(toast);
            Assert.Equal("Series resync complete", toast!.Value.Title);
            Assert.Contains("Reassigned 1 issue", toast!.Value.Message);

            using var verify = PaperbunkrDb.CreateContext();
            Assert.Contains(verify.Series, s => s.Name == "Warhammer 40,000: Will of Iron");
            Assert.DoesNotContain(verify.Series, s => s.Name == "Warhammer 40");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDatabase_Collections_ReflectsRealCollectionRows()
    {
        int seriesId = CreateSeries("Series A", ContentType.Comic);
        CreateCollectionWithSeries("Favorites", seriesId);

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
    public void SelectContentType_FiltersRows_AndSetsIsActive()
    {
        CreateSeriesWithIssue("Comic One", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var mangaBucket = vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga);

        vm.SelectContentTypeCommand.Execute(mangaBucket);

        var row = Assert.Single(vm.IssueList.Rows);
        Assert.Equal("Manga One", row.SeriesName);
        Assert.False(vm.IsAllSeriesActive);
        Assert.True(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga).IsActive);
        Assert.False(vm.ContentTypes.Single(c => c.ContentType == ContentType.Comic).IsActive);
    }

    [Fact]
    public void SelectCollection_FiltersRows_AndSetsIsActive()
    {
        int seriesAId = CreateSeriesWithIssue("Series A");
        CreateSeriesWithIssue("Series B");
        CreateCollectionWithSeries("Favorites", seriesAId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var favorites = vm.Collections.Single();

        vm.SelectCollectionCommand.Execute(favorites);

        var row = Assert.Single(vm.IssueList.Rows);
        Assert.Equal("Series A", row.SeriesName);
        Assert.False(vm.IsAllSeriesActive);
        Assert.True(vm.Collections.Single().IsActive);
    }

    [Fact]
    public void CommitCreateCollection_CreatesAndSelectsIt()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.BeginCreateCollectionCommand.Execute(null);
        Assert.True(vm.IsCreatingCollection);

        vm.NewCollectionName = "Favorites";
        vm.CommitCreateCollectionCommand.Execute(null);

        Assert.False(vm.IsCreatingCollection);
        var created = Assert.Single(vm.Collections);
        Assert.Equal("Favorites", created.Name);
        Assert.True(created.IsActive);
    }

    [Fact]
    public void CommitCreateCollection_BlankName_DoesNotCreate()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.BeginCreateCollectionCommand.Execute(null);
        vm.NewCollectionName = "   ";

        vm.CommitCreateCollectionCommand.Execute(null);

        Assert.False(vm.IsCreatingCollection);
        Assert.Empty(vm.Collections);
    }

    [Fact]
    public void CancelCreateCollection_HidesFieldWithoutCreating()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.BeginCreateCollectionCommand.Execute(null);
        vm.NewCollectionName = "Never mind";

        vm.CancelCreateCollectionCommand.Execute(null);

        Assert.False(vm.IsCreatingCollection);
        Assert.Empty(vm.Collections);
    }

    [Fact]
    public void MoveCollectionUpAndDown_ReorderSidebar()
    {
        CreateCollectionWithSeries("Alpha");
        CreateCollectionWithSeries("Bravo");
        CreateCollectionWithSeries("Charlie");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, vm.Collections.Select(c => c.Name));

        vm.MoveCollectionDownCommand.Execute(vm.Collections.Single(c => c.Name == "Alpha"));
        Assert.Equal(new[] { "Bravo", "Alpha", "Charlie" }, vm.Collections.Select(c => c.Name));

        vm.MoveCollectionUpCommand.Execute(vm.Collections.Single(c => c.Name == "Charlie"));
        Assert.Equal(new[] { "Bravo", "Charlie", "Alpha" }, vm.Collections.Select(c => c.Name));
    }

    [Fact]
    public void MoveCollectionUp_AtTop_IsNoOp()
    {
        CreateCollectionWithSeries("Alpha");
        CreateCollectionWithSeries("Bravo");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.MoveCollectionUpCommand.Execute(vm.Collections.Single(c => c.Name == "Alpha"));

        Assert.Equal(new[] { "Alpha", "Bravo" }, vm.Collections.Select(c => c.Name));
    }

    [Fact]
    public void DeleteConfirm_Armed_RemovesCollection_AndFallsBackWhenActive()
    {
        int seriesId = CreateSeriesWithIssue("Series A");
        int collectionId = CreateCollectionWithSeries("Favorites", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        var row = vm.Collections.Single(c => c.Id == collectionId);
        row.DeleteConfirm!.TriggerCommand.Execute(null);
        Assert.True(row.DeleteConfirm.IsArmed);
        row.DeleteConfirm.TriggerCommand.Execute(null);

        Assert.Empty(vm.Collections);
        Assert.True(vm.IsAllSeriesActive);
    }

    [Fact]
    public void OpenCollectionProperties_InvokesCallback_WithCollectionId()
    {
        int collectionId = CreateCollectionWithSeries("Favorites");
        int? openedId = null;
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            openCollectionProperties: id => openedId = id);

        vm.OpenCollectionPropertiesCommand.Execute(vm.Collections.Single());

        Assert.Equal(collectionId, openedId);
    }

    private static int CreateMixedCollection(int seriesId, int issueId, int bookId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var collection = new Collection { Name = "Mixed" };
        collection.Items.Add(new CollectionItem { SeriesId = seriesId, SortOrder = 0 });
        collection.Items.Add(new CollectionItem { IssueId = issueId, SortOrder = 1 });
        collection.Items.Add(new CollectionItem { BookId = bookId, SortOrder = 2 });
        context.Collections.Add(collection);
        context.SaveChanges();
        return collection.Id;
    }

    private static int CreateBook(string title)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = new Book { Title = title, FilePath = $"C:/books/{title}.epub", AddedTime = DateTime.UtcNow };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public void SelectCollection_WithMixedMembers_EntersCollectionView_WithAllThreeKinds_InManualOrder()
    {
        int seriesId = CreateSeriesWithIssue("Alpha Series");
        int otherSeriesId = CreateSeriesWithIssue("Beta Series");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.First(i => i.SeriesId == otherSeriesId).Id;
        }
        int bookId = CreateBook("Some Novel");
        int collectionId = CreateMixedCollection(seriesId, issueId, bookId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Id == collectionId));

        Assert.True(vm.IsCollectionView);
        Assert.False(vm.IsPosterGrid);
        Assert.Equal(3, vm.CollectionTiles.Count);
        Assert.Equal(new[] { LibraryTileKind.Series, LibraryTileKind.Issue, LibraryTileKind.Book }, vm.CollectionTiles.Select(t => t.Kind));
        Assert.Equal("Alpha Series", vm.CollectionTiles[0].Title);
        Assert.Equal("Some Novel", vm.CollectionTiles[2].Title);
        Assert.True(vm.HasAnyResults);
    }

    [Fact]
    public void SelectCollection_SeriesOnly_DoesNotEnterCollectionView()
    {
        int seriesId = CreateSeriesWithIssue("Alpha");
        int collectionId = CreateCollectionWithSeries("Series Only", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Id == collectionId));

        Assert.False(vm.IsCollectionView);
        Assert.Empty(vm.CollectionTiles);
    }

    [Fact]
    public void SelectAllSeries_AfterMixedCollection_LeavesCollectionView()
    {
        int seriesId = CreateSeriesWithIssue("Alpha Series");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.First(i => i.SeriesId == seriesId).Id;
        }
        int bookId = CreateBook("A Novel");
        int collectionId = CreateMixedCollection(seriesId, issueId, bookId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Id == collectionId));
        Assert.True(vm.IsCollectionView);

        vm.SelectAllSeriesCommand.Execute(null);

        Assert.False(vm.IsCollectionView);
        Assert.Empty(vm.CollectionTiles);
        Assert.True(vm.IsPosterGrid);
    }

    [Fact]
    public void SelectCollectionMember_DispatchesByKind()
    {
        int? openedSeriesId = null, openedIssueId = null, openedBookId = null;
        int seriesId = CreateSeriesWithIssue("Alpha Series");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.First(i => i.SeriesId == seriesId).Id;
        }
        int bookId = CreateBook("A Novel");
        int collectionId = CreateMixedCollection(seriesId, issueId, bookId);
        var vm = new LibraryScreenViewModel(
            goDetail: id => openedSeriesId = id,
            goReaderForIssue: id => openedIssueId = id,
            goToNewIssueProperties: (_, _, _) => { },
            goBookDetailForBook: id => openedBookId = id);
        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Id == collectionId));

        vm.SelectCollectionMemberCommand.Execute(vm.CollectionTiles.Single(t => t.Kind == LibraryTileKind.Series));
        vm.SelectCollectionMemberCommand.Execute(vm.CollectionTiles.Single(t => t.Kind == LibraryTileKind.Issue));
        vm.SelectCollectionMemberCommand.Execute(vm.CollectionTiles.Single(t => t.Kind == LibraryTileKind.Book));

        Assert.Equal(seriesId, openedSeriesId);
        Assert.Equal(issueId, openedIssueId);
        Assert.Equal(bookId, openedBookId);
    }

    [Fact]
    public void SelectAllSeries_ClearsFilter_RestoresFullRows()
    {
        CreateSeriesWithIssue("Comic One", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));
        Assert.Single(vm.IssueList.Rows);

        vm.SelectAllSeriesCommand.Execute(null);

        Assert.Equal(2, vm.IssueList.Rows.Count);
        Assert.True(vm.IsAllSeriesActive);
        Assert.All(vm.ContentTypes, c => Assert.False(c.IsActive));
    }

    // --- Browse history (docs/superpowers/specs/2026-08-19-library-browse-history-design.md) ---

    [Fact]
    public void Construct_CanBrowsePrevious_IsFalse()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.False(vm.CanBrowsePrevious);
        Assert.False(vm.CanBrowseNext);
    }

    [Fact]
    public void SelectContentType_PushesHistoryEntry_EnablesCanBrowsePrevious()
    {
        CreateSeriesWithIssue("Comic One", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));

        Assert.True(vm.CanBrowsePrevious);
        Assert.False(vm.CanBrowseNext);
    }

    [Fact]
    public void BrowsePrevious_ReturnsToThePriorSidebarSelection()
    {
        CreateSeriesWithIssue("Comic One", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));
        Assert.False(vm.IsAllSeriesActive);

        vm.BrowsePreviousCommand.Execute(null);

        Assert.True(vm.IsAllSeriesActive);
        Assert.Equal(2, vm.IssueList.Rows.Count);
        Assert.False(vm.CanBrowsePrevious); // back at the seeded starting entry
        Assert.True(vm.CanBrowseNext); // the Manga selection is still there to redo
    }

    [Fact]
    public void BrowseNext_RedoesTheSelectionThatWasBrowsedAwayFrom()
    {
        CreateSeriesWithIssue("Comic One", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));
        vm.BrowsePreviousCommand.Execute(null);

        vm.BrowseNextCommand.Execute(null);

        Assert.False(vm.IsAllSeriesActive);
        Assert.True(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga).IsActive);
        Assert.True(vm.CanBrowsePrevious);
        Assert.False(vm.CanBrowseNext);
    }

    [Fact]
    public void SelectingADifferentFilter_AfterBrowsingBack_TruncatesTheAbandonedForwardEntry()
    {
        int seriesAId = CreateSeriesWithIssue("Series A");
        CreateSeriesWithIssue("Series B");
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        CreateCollectionWithSeries("Favorites", seriesAId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));
        vm.BrowsePreviousCommand.Execute(null);
        Assert.True(vm.CanBrowseNext); // the Manga selection is redo-able right up until this next click

        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        Assert.False(vm.CanBrowseNext); // the abandoned Manga branch is gone, same as a real browser
        Assert.True(vm.CanBrowsePrevious);
    }

    [Fact]
    public void SearchQueryChange_DoesNotImmediatelyPushHistory_UntilDebounceFlushes()
    {
        CreateSeriesWithIssue("Batman");
        CreateSeriesWithIssue("Superman");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "Batman";
        Assert.False(vm.CanBrowsePrevious); // reload already happened (unchanged existing behavior) - only the history push waits

        vm.FlushSearchHistoryDebounce();

        Assert.True(vm.CanBrowsePrevious);
    }

    [Fact]
    public void BrowsePrevious_RestoresThePriorSearchQuery()
    {
        CreateSeriesWithIssue("Batman");
        CreateSeriesWithIssue("Superman");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchQuery = "Batman";
        vm.FlushSearchHistoryDebounce();

        vm.BrowsePreviousCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.Equal(2, vm.IssueList.Rows.Count);
    }

    [Fact]
    public void ReselectingTheSameFilter_DoesNotPushARedundantHistoryEntry()
    {
        CreateSeriesWithIssue("Comic One", contentType: ContentType.Comic);
        CreateSeriesWithIssue("Manga One", contentType: ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var mangaBucket = vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga);
        vm.SelectContentTypeCommand.Execute(mangaBucket);

        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));

        // Still just one step back to the seeded starting entry - re-selecting the same filter
        // didn't push a second, redundant one.
        vm.BrowsePreviousCommand.Execute(null);
        Assert.False(vm.CanBrowsePrevious);
    }

    [Fact]
    public void SetViewMode_UpdatesIsXProperties()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);

        Assert.True(vm.IsTilesView);
        Assert.False(vm.IsPosterGrid);
        Assert.False(vm.IsPanoramaGrid);
        Assert.False(vm.IsListView);
        Assert.False(vm.IsDetailsView);
        Assert.Equal("Tiles", vm.DisplayModeLabel);
    }

    [Fact]
    public void PosterGrid_IsDefault_AndCardWidthTracksDensity()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(LibraryViewMode.PosterGrid, vm.ViewMode);
        Assert.True(vm.IsPosterGrid);
        Assert.Equal("Poster grid", vm.DisplayModeLabel);
        Assert.Equal(150, vm.PosterCardWidth);

        vm.GridDensity = 1.5;
        Assert.Equal(225, vm.PosterCardWidth);
    }

    [Fact]
    public void ShowTileTitles_PersistsAndAutoHidesAtLowDensity()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.True(vm.EffectiveShowTileTitles);

        vm.ShowTileTitles = false;
        var reloaded = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        Assert.False(reloaded.ShowTileTitles);

        reloaded.ShowTileTitles = true;
        reloaded.GridDensity = 0.6; // 150 * 0.6 = 90 < 108 threshold
        Assert.True(reloaded.ShowTileTitles);
        Assert.False(reloaded.EffectiveShowTileTitles);
    }

    [Fact]
    public void IssueList_Rows_PopulatedRegardlessOfDisplayMode()
    {
        // Every Display mode now shares the exact same per-issue data (docs/superpowers/specs/
        // 2026-08-18-library-book-centric-redesign-design.md Slice 3) - switching modes is a pure
        // layout choice, not a data reload; IssueList.Rows is populated from construction.
        CreateSeriesWithIssue("Series A");
        CreateSeriesWithIssue("Series B");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(2, vm.IssueList.Rows.Count);

        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);
        Assert.Equal(2, vm.IssueList.Rows.Count);

        vm.SetViewModeCommand.Execute(LibraryViewMode.Details);
        Assert.Equal(2, vm.IssueList.Rows.Count);
    }

    [Fact]
    public void RespectsSeriesLevelFilters_SearchAndContentType()
    {
        CreateSeriesWithIssue("Amazing Spider-Man", publisher: "Marvel");
        CreateSeriesWithIssue("Batman", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "spider";

        Assert.Equal("Amazing Spider-Man", Assert.Single(vm.IssueList.Rows).SeriesName);
    }

    [Fact]
    public void FilterUnreadOnly_AppliesPerIssueNotPerSeries()
    {
        int seriesId = CreateSeriesWithIssue("Mixed Series", lastPageRead: 5); // one read issue
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.Add(new Issue { SeriesId = seriesId, Number = "2", LastPageRead = null }); // one unread issue, same series
            context.SaveChanges();
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterUnreadOnly = true;

        // Every mode is per-issue now, so this applies per issue, not "series containing >=1
        // unread issue" - only the actually-unread one survives.
        var row = Assert.Single(vm.IssueList.Rows);
        Assert.Equal("2", row.Number);
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
        Assert.Equal("Amazing Spider-Man", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchQuery = "marvel"; // matches Publisher, not Name
        Assert.Equal("Amazing Spider-Man", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchQuery = "noir"; // matches Genre
        Assert.Equal("Batman", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchQuery = "";
        Assert.Equal(2, vm.IssueList.Rows.Count);
    }

    [Fact]
    public void SearchQuery_MatchesAlternateSeriesTitle()
    {
        int seriesId = CreateSeriesWithIssue("Attack on Titan");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.SeriesTitles.Add(new SeriesTitle { SeriesId = seriesId, Value = "進撃の巨人", Type = SeriesTitleType.Native });
            context.SaveChanges();
        }
        CreateSeriesWithIssue("Unrelated Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "進撃";
        Assert.Equal("Attack on Titan", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchMode = SearchMode.Series;
        Assert.Equal("Attack on Titan", Assert.Single(vm.IssueList.Rows).SeriesName);
    }

    private static void SetIssueFields(int seriesId, Action<Issue> configure)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.Issues).First(s => s.Id == seriesId);
        var issue = series.Issues.First();
        configure(issue);
        context.SaveChanges();
    }

    [Fact]
    public void SearchMode_Writer_OnlyMatchesIssueWriterField()
    {
        int seriesId = CreateSeriesWithIssue("Damnation Crusade");
        SetIssueFields(seriesId, i => i.Writer = "Dan Abnett");
        CreateSeriesWithIssue("Unrelated Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchMode = SearchMode.Writer;
        vm.SearchQuery = "abnett";
        Assert.Equal("Damnation Crusade", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchMode = SearchMode.Series;
        Assert.Empty(vm.IssueList.Rows);
    }

    [Fact]
    public void SearchMode_Artists_MatchesAnyCreatorRoleNotJustWriter()
    {
        int seriesId = CreateSeriesWithIssue("Forge of War");
        SetIssueFields(seriesId, i => i.Penciller = "Lui Antonio");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchMode = SearchMode.Artists;
        vm.SearchQuery = "antonio";
        Assert.Equal("Forge of War", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchMode = SearchMode.Writer;
        Assert.Empty(vm.IssueList.Rows);
    }

    [Fact]
    public void SearchMode_File_OnlyMatchesFilePath()
    {
        int seriesId = CreateSeriesWithIssue("Deathwatch");
        SetIssueFields(seriesId, i => i.FilePath = @"C:\Comics\Deathwatch\issue01.cbz");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchMode = SearchMode.File;
        vm.SearchQuery = "issue01";
        Assert.Equal("Deathwatch", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchMode = SearchMode.Catalog;
        Assert.Empty(vm.IssueList.Rows);
    }

    [Fact]
    public void SearchMode_Catalog_MatchesBookCollectionFieldsOnly()
    {
        int seriesId = CreateSeriesWithIssue("Exterminatus");
        SetIssueFields(seriesId, i => i.ISBN = "978-1-2345-6789-0");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchMode = SearchMode.Catalog;
        vm.SearchQuery = "978-1-2345";
        Assert.Equal("Exterminatus", Assert.Single(vm.IssueList.Rows).SeriesName);

        vm.SearchMode = SearchMode.File;
        Assert.Empty(vm.IssueList.Rows);
    }

    [Fact]
    public void SearchMode_All_FindsIssueLevelFieldsTooNotJustSeriesLevel()
    {
        int seriesId = CreateSeriesWithIssue("Sisters of Battle");
        SetIssueFields(seriesId, i => i.Writer = "Jaime Martin");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchMode = SearchMode.All;
        vm.SearchQuery = "martin";

        Assert.Equal("Sisters of Battle", Assert.Single(vm.IssueList.Rows).SeriesName);
    }

    [Fact]
    public void FilterUnreadOnly_NarrowsRows()
    {
        CreateSeriesWithIssue("Read Series", lastPageRead: 5);
        CreateSeriesWithIssue("Unread Series", lastPageRead: null);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterUnreadOnly = true;

        Assert.Equal("Unread Series", Assert.Single(vm.IssueList.Rows).SeriesName);
    }

    [Fact]
    public void FilterMissingIssues_NarrowsRows()
    {
        CreateSeriesWithIssue("Has All Files", fileIsMissing: false);
        CreateSeriesWithIssue("Missing A File", fileIsMissing: true);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterMissingIssues = true;

        Assert.Equal("Missing A File", Assert.Single(vm.IssueList.Rows).SeriesName);
    }

    [Fact]
    public void FilterTrackedOnly_NarrowsRows()
    {
        int trackedId = CreateSeriesWithIssue("Tracked Series");
        CreateSeriesWithIssue("Untracked Series");
        AddTrackingLink(trackedId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterTrackedOnly = true;

        Assert.Equal("Tracked Series", Assert.Single(vm.IssueList.Rows).SeriesName);
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

        Assert.Equal("Marvel Hero", Assert.Single(vm.IssueList.Rows).SeriesName);
    }

    [Fact]
    public void SortField_Series_OrdersAlphabetically()
    {
        CreateSeriesWithIssue("Zebra");
        CreateSeriesWithIssue("Apple");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Series;
        vm.IssueList.SortDirection = SortDirection.Ascending;

        Assert.Equal(new[] { "Apple", "Zebra" }, vm.IssueList.Rows.Select(r => r.SeriesName));
    }

    [Fact]
    public void SortField_Added_OrdersByAddedTime()
    {
        CreateSeriesWithIssue("Older", addedTime: new DateTime(2026, 1, 1));
        CreateSeriesWithIssue("Newer", addedTime: new DateTime(2026, 6, 1));
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Added; // default direction: Descending
        Assert.Equal(new[] { "Newer", "Older" }, vm.IssueList.Rows.Select(r => r.SeriesName));
    }

    [Fact]
    public void SortField_Opened_OrdersByOpenedTime()
    {
        CreateSeriesWithIssue("Read Long Ago", openedTime: new DateTime(2026, 1, 1));
        CreateSeriesWithIssue("Read Recently", openedTime: new DateTime(2026, 6, 1));
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Opened; // default direction: Descending
        Assert.Equal(new[] { "Read Recently", "Read Long Ago" }, vm.IssueList.Rows.Select(r => r.SeriesName));
    }

    [Fact]
    public void SortField_FileSize_OrdersBySize()
    {
        CreateSeriesWithIssue("Small", fileSize: 100);
        CreateSeriesWithIssue("Large", fileSize: 900);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.FileSize; // default direction: Descending
        Assert.Equal(new[] { "Large", "Small" }, vm.IssueList.Rows.Select(r => r.SeriesName));
    }

    [Fact]
    public void SortField_Publisher_OrdersAlphabetically()
    {
        CreateSeriesWithIssue("Marvel Book", publisher: "Marvel");
        CreateSeriesWithIssue("DC Book", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Publisher;
        vm.IssueList.SortDirection = SortDirection.Ascending;

        Assert.Equal(new[] { "DC Book", "Marvel Book" }, vm.IssueList.Rows.Select(r => r.SeriesName));
    }

    [Fact]
    public void GroupField_Publisher_PartitionsIntoGroups()
    {
        CreateSeriesWithIssue("Marvel Book", publisher: "Marvel");
        CreateSeriesWithIssue("DC Book", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        Assert.Equal(new[] { "DC", "Marvel" }, vm.IssueList.Groups.Select(g => g.Header));
    }

    [Fact]
    public void Sort_AppliesWithinGroups_NotJustAcrossThem()
    {
        CreateSeriesWithIssue("Zebra Comic", publisher: "Shared");
        CreateSeriesWithIssue("Apple Comic", publisher: "Shared");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Series;
        vm.IssueList.SortDirection = SortDirection.Ascending;
        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        var group = Assert.Single(vm.IssueList.Groups);
        Assert.Equal(new[] { "Apple Comic", "Zebra Comic" }, group.Items.Select(r => r.SeriesName));
    }

    [Fact]
    public void HasAnyResults_FalseWhenSearchMatchesNothing()
    {
        CreateSeriesWithIssue("Some Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        Assert.True(vm.IssueList.HasAnyResults);

        vm.SearchQuery = "nothing matches this";

        Assert.Empty(vm.IssueList.Rows);
        Assert.False(vm.IssueList.HasAnyResults);
    }

    [Fact]
    public void HasAnyResults_TrueWhenGroupedResultsExist_EvenThoughRowsIsEmpty()
    {
        CreateSeriesWithIssue("Some Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        Assert.Empty(vm.IssueList.Rows); // grouped mode populates Groups, not Rows
        Assert.True(vm.IssueList.HasAnyResults);
    }

    // --- Series granularity (Covers/Groups/SortField/GroupField) - restored as a real, switchable
    // option alongside IssueList's own per-issue pipeline above; see this file's own top doc
    // comment. Both pipelines are computed on every LoadFromDatabase regardless of which
    // Granularity is active, so these assert on Covers/Groups directly without needing to first
    // flip vm.Granularity to Series - only HasAnyResults/ShowAlphabetIndex actually read Granularity
    // to decide which pipeline's results are "the" current ones.

    [Fact]
    public void Granularity_DefaultsToIssue()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(LibraryContentGranularity.Issue, vm.Granularity);
        Assert.True(vm.IsIssueGranularity);
        Assert.False(vm.IsSeriesGranularity);
    }

    [Fact]
    public void SeriesCards_SortViaSharedPool_ByRepresentativeRow()
    {
        // 2026-09-03: one sort/group pool for both granularities - series cards sort via
        // IssueListFieldCatalog against SeriesCardSample.RepresentativeRow.
        CreateSeriesWithIssue("Zebra");
        CreateSeriesWithIssue("Apple");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Series;
        vm.IssueList.SortDirection = SortDirection.Ascending;

        Assert.Equal(new[] { "Apple", "Zebra" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SeriesCards_SortViaSharedPool_ByAPerIssueField()
    {
        // A per-issue field (Publisher) applied to series cards resolves against the cover issue.
        CreateSeriesWithIssue("Zed's Series", publisher: "Zebra");
        CreateSeriesWithIssue("Abe's Series", publisher: "Apple");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.Publisher;
        vm.IssueList.SortDirection = SortDirection.Ascending;

        Assert.Equal(new[] { "Abe's Series", "Zed's Series" }, vm.Covers.Select(c => c.Name));
    }

    [Fact]
    public void SeriesCards_GroupViaSharedPool_ByPublisher()
    {
        CreateSeriesWithIssue("Marvel Book", publisher: "Marvel");
        CreateSeriesWithIssue("DC Book", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        Assert.Equal(new[] { "DC", "Marvel" }, vm.Groups.Select(g => g.Header));
        Assert.Empty(vm.Covers); // grouped mode populates Groups, not Covers
    }

    [Fact]
    public void SeriesCards_GroupViaSharedPool_ByContentType_CarriedOverField()
    {
        CreateSeries("A Manga", ContentType.Manga);
        CreateSeries("A Comic", ContentType.Comic);
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.Add(new Issue { SeriesId = context.Series.Single(s => s.Name == "A Manga").Id, Number = "1" });
            context.Issues.Add(new Issue { SeriesId = context.Series.Single(s => s.Name == "A Comic").Id, Number = "1" });
            context.SaveChanges();
        }
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.GroupField = IssueListGroupField.ContentType;

        // Enum-declaration order (Comic before Manga), not alphabetical.
        Assert.Equal(new[] { "Comic", "Manga" }, vm.Groups.Select(g => g.Header));
    }

    [Fact]
    public void FlatCovers_Ungrouped_IsExactlyTheCovers()
    {
        CreateSeriesWithIssue("Apple");
        CreateSeriesWithIssue("Zebra");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(vm.Covers, vm.FlatCovers.Cast<SeriesCardSample>());
        Assert.DoesNotContain(vm.FlatCovers, x => x is GridSectionHeader);
    }

    [Fact]
    public void FlatCovers_Grouped_InterleavesHeadersBeforeEachSection()
    {
        CreateSeriesWithIssue("Marvel Book", publisher: "Marvel");
        CreateSeriesWithIssue("DC Book", publisher: "DC");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        var headers = vm.FlatCovers.OfType<GridSectionHeader>().ToList();
        Assert.Equal(vm.Groups.Select(g => g.Header), headers.Select(h => h.Header));
        Assert.Equal(vm.Groups.Select(g => g.Items.Count), headers.Select(h => h.Count));
        Assert.Equal(vm.Groups.Sum(g => g.Items.Count) + headers.Count, vm.FlatCovers.Count);
        Assert.IsType<GridSectionHeader>(vm.FlatCovers[0]);
    }

    [Fact]
    public void SeriesFilterUnreadOnly_MatchesIfAnyIssueInSeriesIsUnread()
    {
        // Deliberately different semantics from IssueList's per-issue filtering (tested above,
        // Search_ScopedToSearchMode et al.): a series card represents the whole series, so it stays
        // visible if ANY of its issues is unread, not just when every one of them is.
        CreateSeriesWithIssue("Partially Read", lastPageRead: null);
        int allReadSeriesId = CreateSeries("All Read", ContentType.Comic);
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.Add(new Issue { SeriesId = allReadSeriesId, Number = "1", LastPageRead = 5 });
            context.SaveChanges();
        }
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.FilterUnreadOnly = true;

        Assert.Equal("Partially Read", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void SeriesHasAnyResults_TrueWhenGroupedResultsExist_EvenThoughCoversIsEmpty()
    {
        CreateSeriesWithIssue("Some Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.Granularity = LibraryContentGranularity.Series;

        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        Assert.Empty(vm.Covers);
        Assert.True(vm.HasAnyResults);
    }

    [Fact]
    public void HasAnyResults_FalseForBothGranularities_WhenLibraryIsEmpty()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.False(vm.HasAnyResults);
        vm.Granularity = LibraryContentGranularity.Series;
        Assert.False(vm.HasAnyResults);
    }

    [Fact]
    public void RevealSeries_ResolvesTheSeriesBeforeReveal()
    {
        // Same not-asserting-real-OS-shell-side-effects convention as RevealIssue_ResolvesTheIssueBeforeReveal above.
        CreateSeriesWithIssue("Some Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var card = Assert.Single(vm.Covers);

        var exception = Record.Exception(() => vm.RevealSeriesCommand.Execute(card));

        Assert.Null(exception);
    }

    [Fact]
    public void SelectCard_InvokesGoDetailWithTheCardsSeriesId()
    {
        int seriesId = CreateSeriesWithIssue("Some Series");
        int? navigatedSeriesId = null;
        var vm = new LibraryScreenViewModel(goDetail: id => navigatedSeriesId = id, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var card = Assert.Single(vm.Covers);

        vm.SelectCardCommand.Execute(card);

        Assert.Equal(seriesId, navigatedSeriesId);
    }

    [Fact]
    public void ContinueReading_InvokesGoReaderForIssue_WithTheCardsContinueReadingIssueId()
    {
        int seriesId = CreateSeriesWithIssue("Some Series", lastPageRead: null);
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
        }
        int? readerIssueId = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: id => readerIssueId = id, goToNewIssueProperties: (_, _, _) => { });
        var card = Assert.Single(vm.Covers);

        vm.ContinueReadingCommand.Execute(card);

        Assert.Equal(issueId, readerIssueId);
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

        // Tile context-menu commands take the raw SeriesId now (docs/superpowers/specs/
        // 2026-08-18-library-book-centric-redesign-design.md Slice 3), not a card object.
        vm.SetSeriesContentTypeMangaCommand.Execute(seriesId);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ContentType.Manga, context.Series.First(s => s.Id == seriesId).ContentType);
    }

    [Fact]
    public void SetSeriesReadingModeRightToLeft_PersistsToTheSeries()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetSeriesReadingModeRightToLeftCommand.Execute(seriesId);

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

    // ===================== Status (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-and-bookmarks-design.md) =====================

    [Fact]
    public void SetSeriesStatusCompleted_PersistsToTheRightSeries()
    {
        int seriesId = CreateSeries("A Series", ContentType.Comic);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetSeriesStatusCompletedCommand.Execute(seriesId);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(SeriesStatus.Completed, context.Series.First(s => s.Id == seriesId).Status);
    }

    [Fact]
    public void SetSeriesStatus_DoesNotTouchOtherSeries()
    {
        int seriesOneId = CreateSeries("Series One", ContentType.Comic);
        int seriesTwoId = CreateSeries("Series Two", ContentType.Comic);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetSeriesStatusHiatusCommand.Execute(seriesOneId);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(SeriesStatus.Hiatus, context.Series.First(s => s.Id == seriesOneId).Status);
        Assert.Equal(SeriesStatus.Unknown, context.Series.First(s => s.Id == seriesTwoId).Status);
    }

    // ===================== Reading Status (docs/superpowers/specs/2026-08-19-metadata-model-reading-status-design.md) =====================

    [Fact]
    public void SetSeriesReadingStatusCompleted_PersistsToTheRightSeries()
    {
        int seriesId = CreateSeries("A Series", ContentType.Comic);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetSeriesReadingStatusCompletedCommand.Execute(seriesId);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingStatus.Completed, context.Series.First(s => s.Id == seriesId).ReadingStatus);
    }

    [Fact]
    public void SetSeriesReadingStatus_DoesNotTouchOtherSeries()
    {
        int seriesOneId = CreateSeries("Series One", ContentType.Comic);
        int seriesTwoId = CreateSeries("Series Two", ContentType.Comic);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetSeriesReadingStatusDroppedCommand.Execute(seriesOneId);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingStatus.Dropped, context.Series.First(s => s.Id == seriesOneId).ReadingStatus);
        Assert.Equal(ReadingStatus.Unknown, context.Series.First(s => s.Id == seriesTwoId).ReadingStatus);
    }

    [Fact]
    public void GoToSeries_InvokesGoDetailWithTheRightSeriesId()
    {
        int? navigatedSeriesId = null;
        var vm = new LibraryScreenViewModel(goDetail: id => navigatedSeriesId = id, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.GoToSeriesCommand.Execute(42);

        Assert.Equal(42, navigatedSeriesId);
    }

    [Fact]
    public void RevealIssue_ResolvesTheIssueBeforeReveal()
    {
        // RevealInExplorerHelper.RevealIssue itself needs a real file on disk to actually launch
        // Explorer, which this unit test deliberately doesn't provide - this only exercises that
        // the command resolves the right Issue row from the id without throwing, matching the
        // existing project convention of not asserting real OS shell side effects in unit tests.
        int seriesId = CreateSeriesWithIssue("Some Series");
        using var context = PaperbunkrDb.CreateContext();
        int issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        var exception = Record.Exception(() => vm.RevealIssueCommand.Execute(issueId));

        Assert.Null(exception);
    }

    // --- Saved List Layouts (docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md) ---
    // Sort/Group/ShowContinueReadingButton persist again as of the same-session follow-up that
    // restored series-card Granularity as a real option (see this file's own top doc comment) -
    // they're the series-card equivalent of IssueList's own Sort/Group persistence tested right
    // below. Display mode/density/badges/search/filters remain real, still-persisted Library-level
    // state, same as before.

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
        int collectionId = CreateCollectionWithSeries("Owned", CreateSeries("Owned Series", ContentType.Comic));
        SeedAppSettings(settings =>
        {
            settings.LibraryViewMode = LibraryViewMode.List;
            settings.LibraryGridDensity = 1.3;
            settings.LibraryShowUnreadBadge = false;
            settings.LibraryShowPublisherBadge = true;
            settings.LibraryShowLanguageBadge = true;
            settings.LibraryUseLanguageIcon = true;
            settings.LibrarySearchQuery = "kilo";
            settings.LibraryActiveCollectionId = collectionId;
            settings.LibraryFilterUnreadOnly = true;
            settings.LibraryFilterMissingIssues = true;
            settings.LibraryFilterTrackedOnly = true;
        });

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(LibraryViewMode.List, vm.ViewMode);
        Assert.Equal(1.3, vm.GridDensity);
        Assert.False(vm.ShowUnreadBadge);
        Assert.True(vm.ShowPublisherBadge);
        Assert.True(vm.ShowLanguageBadge);
        Assert.True(vm.UseLanguageIcon);
        Assert.Equal("kilo", vm.SearchQuery);
        Assert.True(vm.FilterUnreadOnly);
        Assert.True(vm.FilterMissingIssues);
        Assert.True(vm.FilterTrackedOnly);
        Assert.False(vm.IsAllSeriesActive);
        Assert.Contains(vm.Collections, c => c.Id == collectionId && c.IsActive);
    }

    [Fact]
    public void IssueListSortAndGroup_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.IssueList.SortField = IssueListSortField.FileSize;
        vm.IssueList.SortDirection = SortDirection.Ascending;
        vm.IssueList.GroupField = IssueListGroupField.Publisher;

        var settings = ReadAppSettings();
        Assert.Equal(IssueListSortField.FileSize, settings.LibraryIssueListSortField);
        Assert.Equal(SortDirection.Ascending, settings.LibraryIssueListSortDirection);
        Assert.Equal(IssueListGroupField.Publisher, settings.LibraryIssueListGroupField);
    }

    [Fact]
    public void Construct_ReflectsNonDefaultIssueListSortAndGroup_FromAppSettings()
    {
        SeedAppSettings(settings =>
        {
            settings.LibraryIssueListSortField = IssueListSortField.Publisher;
            settings.LibraryIssueListSortDirection = SortDirection.Ascending;
            settings.LibraryIssueListGroupField = IssueListGroupField.Publisher;
        });

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(IssueListSortField.Publisher, vm.IssueList.SortField);
        Assert.Equal(SortDirection.Ascending, vm.IssueList.SortDirection);
        Assert.Equal(IssueListGroupField.Publisher, vm.IssueList.GroupField);
    }

    [Fact]
    public void SeriesGranularity_SortGroupChange_PersistsToTheSharedIssueListColumns()
    {
        // 2026-09-03: one pool - a sort/group change while in Series granularity writes the same
        // LibraryIssueListSort*/GroupField columns the per-issue side uses.
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.Granularity = LibraryContentGranularity.Series;

        vm.IssueList.SortField = IssueListSortField.SeriesIssueCount;
        vm.IssueList.SortDirection = SortDirection.Ascending;
        vm.IssueList.GroupField = IssueListGroupField.ContentType;
        vm.ShowContinueReadingButton = true;

        var settings = ReadAppSettings();
        Assert.Equal(IssueListSortField.SeriesIssueCount, settings.LibraryIssueListSortField);
        Assert.Equal(SortDirection.Ascending, settings.LibraryIssueListSortDirection);
        Assert.Equal(IssueListGroupField.ContentType, settings.LibraryIssueListGroupField);
        Assert.Equal(LibraryContentGranularity.Series, settings.LibraryGranularity);
        Assert.True(settings.LibraryShowContinueReadingButton);
    }

    [Fact]
    public void ViewModeAndGridDensity_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);
        vm.GridDensity = 0.8;

        var settings = ReadAppSettings();
        Assert.Equal(LibraryViewMode.Tiles, settings.LibraryViewMode);
        Assert.Equal(0.8, settings.LibraryGridDensity);
    }

    // --- Configurable Details table columns (docs/superpowers/specs/2026-08-27-library-browsing-4b-
    // toolbar-rework-design.md §8) ---

    [Fact]
    public void DetailsColumns_NullSetting_FallsBackToCuratedDefault()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        var visible = vm.DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field).ToArray();
        Assert.Equal(IssueListFieldCatalog.DefaultDetailsColumns, visible);
        // Every column-eligible field is present (hidden ones appended for the right-click menu).
        Assert.Equal(IssueListFieldCatalog.ColumnFields.Count, vm.DetailsColumns.Count);
    }

    [Fact]
    public void DetailsColumns_StoredList_ParsedInOrder_UnknownNamesSkipped()
    {
        SeedAppSettings(s => s.LibraryDetailsColumns = "Title,Bogus,Series,Status");

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        var visible = vm.DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field).ToArray();
        // "Bogus" is not an enum value; "Status" is sort-only (not column-eligible) - both skipped.
        Assert.Equal(new[] { IssueListSortField.Title, IssueListSortField.Series }, visible);
    }

    [Fact]
    public void TogglingDetailsColumnVisibility_PersistsToAppSettings()
    {
        SeedAppSettings(s => s.LibraryDetailsColumns = "Title,Series");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.DetailsColumns.Single(c => c.Field == IssueListSortField.Year).IsVisible = true;
        vm.DetailsColumns.Single(c => c.Field == IssueListSortField.Series).IsVisible = false;

        Assert.Equal("Title,Year", ReadAppSettings().LibraryDetailsColumns);

        // A fresh VM reads the customization back.
        var reloaded = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        Assert.Equal(
            new[] { IssueListSortField.Title, IssueListSortField.Year },
            reloaded.DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field).ToArray());
    }

    [Fact]
    public void SetDetailsSort_SetsField_ThenFlipsDirectionOnRepeat()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SetDetailsSortCommand.Execute(IssueListSortField.Year);
        Assert.Equal(IssueListSortField.Year, vm.IssueList.SortField);
        var firstDirection = vm.IssueList.SortDirection;

        vm.SetDetailsSortCommand.Execute(IssueListSortField.Year);
        Assert.Equal(IssueListSortField.Year, vm.IssueList.SortField);
        Assert.NotEqual(firstDirection, vm.IssueList.SortDirection);
    }

    [Fact]
    public void OverlayBadgeToggles_Changed_PersistToAppSettings()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.ShowUnreadBadge = false;
        vm.ShowPublisherBadge = true;
        vm.ShowLanguageBadge = true;
        vm.UseLanguageIcon = true;

        var settings = ReadAppSettings();
        Assert.False(settings.LibraryShowUnreadBadge);
        Assert.True(settings.LibraryShowPublisherBadge);
        Assert.True(settings.LibraryShowLanguageBadge);
        Assert.True(settings.LibraryUseLanguageIcon);
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
    public void SelectContentType_PersistsActiveContentType_ClearsActiveCollection()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        CreateCollectionWithSeries("Owned", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));

        var settings = ReadAppSettings();
        Assert.Equal(ContentType.Manga, settings.LibraryActiveContentType);
        Assert.Null(settings.LibraryActiveCollectionId);
    }

    [Fact]
    public void SelectCollection_PersistsActiveCollection_ClearsActiveContentType()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        int collectionId = CreateCollectionWithSeries("Owned", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectContentTypeCommand.Execute(vm.ContentTypes.Single());

        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Id == collectionId));

        var settings = ReadAppSettings();
        Assert.Equal(collectionId, settings.LibraryActiveCollectionId);
        Assert.Null(settings.LibraryActiveContentType);
    }

    [Fact]
    public void SelectAllSeries_ClearsBothActiveFilters_PersistsToAppSettings()
    {
        int seriesId = CreateSeries("A Manga Series", ContentType.Manga);
        CreateCollectionWithSeries("Owned", seriesId);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        vm.SelectAllSeriesCommand.Execute(null);

        var settings = ReadAppSettings();
        Assert.Null(settings.LibraryActiveContentType);
        Assert.Null(settings.LibraryActiveCollectionId);
    }

    [Fact]
    public void Construct_StaleActiveCollectionId_FallsBackToAllSeries()
    {
        int realCollectionId = CreateCollectionWithSeries("Owned", CreateSeries("A Series", ContentType.Comic));
        SeedAppSettings(settings => settings.LibraryActiveCollectionId = realCollectionId + 999999);

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.True(vm.IsAllSeriesActive);
        Assert.DoesNotContain(vm.Collections, c => c.IsActive);
    }

    // --- Delete Series/Issue (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) ---

    [Fact]
    public void DeleteIssue_RemovesTheIssue_ButLeavesTheSeries()
    {
        int seriesId = CreateSeriesWithIssue("Kilo Station");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.DeleteIssueCommand.Execute(issueId);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Null(verifyContext.Issues.Find(issueId));
        Assert.NotNull(verifyContext.Series.Find(seriesId)); // the (now-empty) series itself survives
    }

    [Fact]
    public void DeleteSeries_RemovesTheSeriesAndAllItsIssues()
    {
        int seriesId = CreateSeriesWithIssue("Kilo Station");

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.DeleteSeriesCommand.Execute(seriesId);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Null(verifyContext.Series.Find(seriesId));
        Assert.Empty(verifyContext.Issues.Where(i => i.SeriesId == seriesId));
    }

    [Fact]
    public void DeleteIssue_RemovesReadingListReferencesFirst_SoTheDeleteDoesNotThrow()
    {
        // ReadingListItem.Issue's FK is Restrict, not Cascade - deleting a still-referenced Issue
        // would throw a DbUpdateException unless the reference is removed first (real bug found and
        // fixed in NeedsReviewViewModel.RemoveMissingFile while building this feature).
        int seriesId = CreateSeriesWithIssue("Kilo Station");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
            var list = new ReadingList { Name = "Test List", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            list.Items.Add(new ReadingListItem { IssueId = issueId, SortOrder = 0 });
            context.ReadingLists.Add(list);
            context.SaveChanges();
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var exception = Record.Exception(() => vm.DeleteIssueCommand.Execute(issueId));

        Assert.Null(exception);
        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Empty(verifyContext.ReadingListItems.Where(i => i.IssueId == issueId));
    }

    // --- Mark as Read/Unread (docs/superpowers/specs/2026-08-23-mark-as-read-design.md) ---

    [Fact]
    public void MarkIssueRead_SetsLastPageReadToLastValidIndex_AndRefreshesTheGrid()
    {
        int seriesId = CreateSeriesWithIssue("Kilo Station");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
            context.Issues.Find(issueId)!.PageCount = 20;
            context.SaveChanges();
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.MarkIssueReadCommand.Execute(issueId);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(19, verifyContext.Issues.Find(issueId)!.LastPageRead);
    }

    [Fact]
    public void MarkIssueUnread_ZeroesLastPageRead()
    {
        int seriesId = CreateSeriesWithIssue("Kilo Station", lastPageRead: 19);
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.MarkIssueUnreadCommand.Execute(issueId);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(0, verifyContext.Issues.Find(issueId)!.LastPageRead);
    }

    // --- Multi-selection (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md) ---

    [Fact]
    public void ToggleIssueSelection_UpdatesSelectionAndSelectionCount()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);

        Assert.True(vm.IssueList.Rows[0].IsSelected);
        Assert.Equal(1, vm.SelectionCount);
        Assert.True(vm.HasSelection);
    }

    [Fact]
    public void ToggleIssueSelection_ShiftClick_SelectsRange()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        CreateSeriesWithIssue("Charlie Three");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[2], isShiftHeld: true);

        Assert.Equal(3, vm.SelectionCount);
        Assert.All(vm.IssueList.Rows, r => Assert.True(r.IsSelected));
    }

    [Fact]
    public void ClearSelection_ResetsCountAndVisibleFlags()
    {
        CreateSeriesWithIssue("Alpha One");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);

        vm.ClearSelectionCommand.Execute(null);

        Assert.Equal(0, vm.SelectionCount);
        Assert.False(vm.HasSelection);
        Assert.False(vm.IssueList.Rows[0].IsSelected);
    }

    /// <summary>Re-sorting rebuilds every <see cref="IssueListRow"/> from scratch
    /// (<c>IssueListScreenViewModel.Render</c>) - this guards against the selection silently
    /// vanishing because the old row instances (and their flags) were discarded.</summary>
    [Fact]
    public void ChangingSortField_PreservesSelectionAcrossRebuiltRows()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        int selectedId = vm.IssueList.Rows[0].Id;
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);

        vm.IssueList.SortDirection = vm.IssueList.SortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;

        Assert.Equal(1, vm.SelectionCount);
        Assert.True(vm.IssueList.Rows.Single(r => r.Id == selectedId).IsSelected);
    }

    [Fact]
    public void EditIssueProperties_NothingSelected_OpensSingleEditor()
    {
        int seriesId = CreateSeriesWithIssue("Alpha One");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
        }

        int? openedSingle = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            goIssueProperties: id => openedSingle = id);

        vm.EditIssuePropertiesCommand.Execute(issueId);

        Assert.Equal(issueId, openedSingle);
    }

    [Fact]
    public void EditIssueProperties_WithSelectionUnion_OpensBulkEditor()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);

        IReadOnlyList<int>? bulkIds = null;
        var vmWithBulkCallback = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            goBulkIssueProperties: ids => bulkIds = ids);
        vmWithBulkCallback.ToggleIssueSelection(vmWithBulkCallback.IssueList.Rows[0], isShiftHeld: false);

        // Right-clicking the OTHER (unselected) row extends the union to both - same precedent as
        // DetailTabsViewModel.EditIssueProperties.
        vmWithBulkCallback.EditIssuePropertiesCommand.Execute(vmWithBulkCallback.IssueList.Rows[1].Id);

        Assert.NotNull(bulkIds);
        Assert.Equal(2, bulkIds!.Count);
    }

    [Fact]
    public void DeleteIssue_WithSelectionUnion_DeletesEveryIssueInTheUnion()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        CreateSeriesWithIssue("Charlie Three");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.DeleteIssueCommand.Execute(vm.IssueList.Rows[2].Id);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Empty(verifyContext.Issues);
        Assert.Equal(0, vm.SelectionCount);
    }

    [Fact]
    public void DeleteSelection_DeletesTheWholeCurrentSelection()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        CreateSeriesWithIssue("Charlie Three");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.DeleteSelectionCommand.Execute(null);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Single(verifyContext.Issues);
    }

    /// <summary>
    /// Ctrl+A/Delete keyboard entry points (docs/superpowers/specs/2026-08-31-app-wide-and-library-
    /// keyboard-shortcuts-design.md) - both dispatch by <see cref="LibraryScreenViewModel.IsSeriesGranularity"/>
    /// to the same commands the context menu/action bar already use, so these tests only cover the
    /// new "does the gesture reach the right grain" wiring, not re-verify the underlying select-all/
    /// delete logic those existing tests already cover.
    /// </summary>
    [Fact]
    public void SelectAllVisible_IssueGranularity_SelectsAllIssuesNotSeries()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SelectAllVisibleCommand.Execute(null);

        Assert.Equal(vm.IssueList.Rows.Count, vm.SelectionCount);
        Assert.Equal(0, vm.SeriesSelectionCount);
    }

    [Fact]
    public void SelectAllVisible_SeriesGranularity_SelectsAllSeriesNotIssues()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.Granularity = LibraryContentGranularity.Series;

        vm.SelectAllVisibleCommand.Execute(null);

        Assert.Equal(vm.Covers.Count, vm.SeriesSelectionCount);
        Assert.Equal(0, vm.SelectionCount);
    }

    [Fact]
    public void DeleteCurrentSelection_IssueGranularity_DeletesSelectedIssues()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);

        vm.DeleteCurrentSelectionCommand.Execute(null);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Single(verifyContext.Issues);
    }

    [Fact]
    public void DeleteCurrentSelection_SeriesGranularity_DeletesSelectedSeries()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.Granularity = LibraryContentGranularity.Series;
        vm.ToggleSeriesSelection(vm.Covers[0], isShiftHeld: false);

        vm.DeleteCurrentSelectionCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.Series);
    }

    [Fact]
    public void DeleteCurrentSelection_NoSelection_NoOps()
    {
        CreateSeriesWithIssue("Alpha One");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.DeleteCurrentSelectionCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.Issues);
    }

    [Fact]
    public void BulkEditSelection_OpensBulkEditorForTheWholeSelection()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        IReadOnlyList<int>? bulkIds = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            goBulkIssueProperties: ids => bulkIds = ids);
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.BulkEditSelectionCommand.Execute(null);

        Assert.NotNull(bulkIds);
        Assert.Equal(2, bulkIds!.Count);
    }

    // --- Slice 2: bulk mark read/unread + add to reading list
    //     (docs/superpowers/specs/2026-08-24-library-multiselect-slice2-design.md) ---

    [Fact]
    public void MarkSelectionRead_MarksEveryIssueInSelection_AndToasts()
    {
        int seriesA = CreateSeriesWithIssue("Alpha One");
        int seriesB = CreateSeriesWithIssue("Bravo Two");
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues)
            {
                issue.PageCount = 10;
            }
            context.SaveChanges();
        }

        (string Title, string Message)? toast = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (title, message) => toast = (title, message));
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.MarkSelectionReadCommand.Execute(null);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.All(verifyContext.Issues, i => Assert.Equal(9, i.LastPageRead));
        Assert.NotNull(toast);
        Assert.Contains("2", toast!.Value.Message);
    }

    [Fact]
    public void MarkIssueRead_SingleIssueNoSelection_DoesNotToast()
    {
        int seriesId = CreateSeriesWithIssue("Alpha One");
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
            context.Issues.Find(issueId)!.PageCount = 10;
            context.SaveChanges();
        }

        bool toasted = false;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (_, _) => toasted = true);

        vm.MarkIssueReadCommand.Execute(issueId);

        Assert.False(toasted);
    }

    [Fact]
    public void MarkSelectionUnread_ZeroesLastPageReadForEveryIssueInSelection()
    {
        CreateSeriesWithIssue("Alpha One", lastPageRead: 5);
        CreateSeriesWithIssue("Bravo Two", lastPageRead: 7);
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.MarkSelectionUnreadCommand.Execute(null);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.All(verifyContext.Issues, i => Assert.Equal(0, i.LastPageRead));
    }

    [Fact]
    public void CreateReadingListAndAddSelection_CreatesListAndAddsWholeSelection()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        (string Title, string Message)? toast = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (title, message) => toast = (title, message));
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.CreateReadingListAndAddSelectionCommand.Execute(null);

        using var verifyContext = PaperbunkrDb.CreateContext();
        var list = Assert.Single(verifyContext.ReadingLists);
        Assert.Equal(2, verifyContext.ReadingListItems.Count(i => i.ReadingListId == list.Id));
        Assert.NotNull(toast);
        Assert.Contains("Added 2", toast!.Value.Message);
        Assert.False(vm.IsAddToListOpen);
    }

    [Fact]
    public void AddSelectionToReadingList_ExistingList_AddsSelectionWithoutDuplicates()
    {
        int seriesA = CreateSeriesWithIssue("Alpha One");
        int seriesB = CreateSeriesWithIssue("Bravo Two");
        int listId;
        int alphaIssueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var now = DateTime.UtcNow;
            var list = new ReadingList { Name = "Weekend Reads", Type = ReadingListType.User, CreatedAt = now, UpdatedAt = now };
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;

            alphaIssueId = context.Issues.Single(i => i.SeriesId == seriesA).Id;
            // Alpha is already in the list - the bulk add must skip it, not duplicate it.
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = alphaIssueId, SortOrder = 0 });
            context.SaveChanges();
        }

        (string Title, string Message)? toast = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (title, message) => toast = (title, message));
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.IssueList.Rows[1], isShiftHeld: false);

        vm.AddSelectionToReadingListCommand.Execute(listId);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(2, verifyContext.ReadingListItems.Count(i => i.ReadingListId == listId));
        Assert.Equal(1, verifyContext.ReadingListItems.Count(i => i.ReadingListId == listId && i.IssueId == alphaIssueId));
        Assert.NotNull(toast);
        Assert.Contains("1 already in list", toast!.Value.Message);
    }

    [Fact]
    public void AddSelectionToReadingList_EverythingAlreadyMember_ToastsWithoutInserting()
    {
        int seriesId = CreateSeriesWithIssue("Alpha One");
        int listId;
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var now = DateTime.UtcNow;
            var list = new ReadingList { Name = "Weekend Reads", Type = ReadingListType.User, CreatedAt = now, UpdatedAt = now };
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;

            issueId = context.Issues.Single(i => i.SeriesId == seriesId).Id;
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueId, SortOrder = 0 });
            context.SaveChanges();
        }

        (string Title, string Message)? toast = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (title, message) => toast = (title, message));
        vm.ToggleIssueSelection(vm.IssueList.Rows[0], isShiftHeld: false);

        vm.AddSelectionToReadingListCommand.Execute(listId);

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(1, verifyContext.ReadingListItems.Count(i => i.ReadingListId == listId));
        Assert.NotNull(toast);
        Assert.Contains("All 1 already", toast!.Value.Message);
    }

    [Fact]
    public void LoadFromDatabase_PopulatesReadingListsInSortOrder()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var now = DateTime.UtcNow;
            context.ReadingLists.Add(new ReadingList { Name = "Second", SortOrder = 1, Type = ReadingListType.User, CreatedAt = now, UpdatedAt = now });
            context.ReadingLists.Add(new ReadingList { Name = "First", SortOrder = 0, Type = ReadingListType.User, CreatedAt = now, UpdatedAt = now });
            context.SaveChanges();
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Equal(new[] { "First", "Second" }, vm.ReadingLists.Select(l => l.Name));
    }

    // --- Slice 3: series selection + bulk delete/edit (docs/superpowers/specs/2026-08-24-library-
    //     multiselect-slice3-design.md) ---

    [Fact]
    public void ToggleSeriesSelection_PlainClick_TogglesSelection()
    {
        CreateSeriesWithIssue("Alpha One");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var card = vm.Covers[0];

        vm.ToggleSeriesSelection(card, isShiftHeld: false);
        Assert.True(vm.HasSeriesSelection);
        Assert.Equal(1, vm.SeriesSelectionCount);
        Assert.True(card.IsSelected);

        vm.ToggleSeriesSelection(card, isShiftHeld: false);
        Assert.False(vm.HasSeriesSelection);
        Assert.False(card.IsSelected);
    }

    [Fact]
    public void ToggleSeriesSelection_ShiftClick_SelectsContiguousRange()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        CreateSeriesWithIssue("Charlie Three");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.ToggleSeriesSelection(vm.Covers[0], isShiftHeld: false);
        vm.ToggleSeriesSelection(vm.Covers[2], isShiftHeld: true);

        Assert.Equal(3, vm.SeriesSelectionCount);
        Assert.All(vm.Covers, c => Assert.True(c.IsSelected));
    }

    [Fact]
    public void HasAnySelection_TrueWhenEitherGranularityHasASelection()
    {
        CreateSeriesWithIssue("Alpha One");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        Assert.False(vm.HasAnySelection);

        vm.ToggleSeriesSelection(vm.Covers[0], isShiftHeld: false);
        Assert.True(vm.HasAnySelection);

        vm.ClearSeriesSelectionCommand.Execute(null);
        Assert.False(vm.HasAnySelection);
    }

    [Fact]
    public void DeleteSeriesSelection_DeletesEverySelectedSeries()
    {
        CreateSeriesWithIssue("Alpha One");
        CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.ToggleSeriesSelection(vm.Covers[0], isShiftHeld: false);
        vm.ToggleSeriesSelection(vm.Covers[1], isShiftHeld: false);

        vm.DeleteSeriesSelectionCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.Series);
    }

    [Fact]
    public void DeleteSeries_UnselectedLoneCard_DeletesOnlyThatOne()
    {
        int keepId = CreateSeriesWithIssue("Alpha One");
        int deleteId = CreateSeriesWithIssue("Bravo Two");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.DeleteSeriesCommand.Execute(deleteId);

        using var context = PaperbunkrDb.CreateContext();
        var remaining = Assert.Single(context.Series);
        Assert.Equal(keepId, remaining.Id);
    }

    [Fact]
    public void BulkEditSeriesSelection_InvokesCallbackWithSelectedIds()
    {
        int seriesA = CreateSeriesWithIssue("Alpha One");
        int seriesB = CreateSeriesWithIssue("Bravo Two");
        IReadOnlyList<int>? receivedIds = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            goBulkSeriesProperties: ids => receivedIds = ids);
        vm.ToggleSeriesSelection(vm.Covers.Single(c => c.SeriesId == seriesA), isShiftHeld: false);
        vm.ToggleSeriesSelection(vm.Covers.Single(c => c.SeriesId == seriesB), isShiftHeld: false);

        vm.BulkEditSeriesSelectionCommand.Execute(null);

        Assert.NotNull(receivedIds);
        Assert.Equal(new[] { seriesA, seriesB }, receivedIds!.OrderBy(id => id));
    }

    // --- Search suggestions (docs/superpowers/specs/2026-08-31-library-search-suggestions-design.md) ---

    /// <summary>A minimal <c>IsSmart</c> collection - the rule's own conditions are never evaluated
    /// by these tests, only its presence/name matter for suggestion matching.</summary>
    private static int CreateSmartCollection(string name)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new SmartList
        {
            Name = name + " rule",
            TargetKind = SmartListTargetKind.Series,
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = [new SmartListCondition { Field = SmartListField.SeriesName, Operator = SmartListOperator.Is, Value = "irrelevant" }],
            },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();

        var collection = new Collection { Name = name, SeriesSmartListId = list.Id };
        context.Collections.Add(collection);
        context.SaveChanges();
        return collection.Id;
    }

    [Fact]
    public void RecentSearch_RecordedOnSettle_CappedAndDeduped()
    {
        CreateSeriesWithIssue("Batman");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        for (int i = 1; i <= 9; i++)
        {
            vm.SearchQuery = $"query{i}";
            vm.FlushSearchHistoryDebounce();
        }

        // Re-searching an already-recorded query moves it to the front instead of duplicating it.
        vm.SearchQuery = "query3";
        vm.FlushSearchHistoryDebounce();

        var recent = JsonSerializer.Deserialize<List<string>>(ReadAppSettings().LibraryRecentSearches!)!;
        Assert.Equal(8, recent.Count); // capped at MaxRecentSearchesStored
        Assert.Equal("query3", recent[0]); // most-recent-first
        Assert.DoesNotContain("query1", recent); // oldest evicted first
        Assert.Single(recent, q => q == "query3"); // not duplicated
    }

    [Fact]
    public void RecentSearch_EmptyOrWhitespaceQuery_NeverRecorded()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "   ";
        vm.FlushSearchHistoryDebounce();

        Assert.Null(ReadAppSettings().LibraryRecentSearches);
    }

    [Theory]
    [InlineData("all", SearchMode.All)]
    [InlineData("SERIES", SearchMode.Series)]
    [InlineData("Writer", SearchMode.Writer)]
    [InlineData("artists", SearchMode.Artists)]
    [InlineData("descriptive", SearchMode.Descriptive)]
    [InlineData("file", SearchMode.File)]
    [InlineData("catalog", SearchMode.Catalog)]
    public void FieldPrefix_RecognizedKeyword_SwitchesSearchMode(string keyword, SearchMode expected)
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = $"{keyword}:miller";

        Assert.Equal(expected, vm.SearchMode);
    }

    [Fact]
    public void FieldPrefix_UnrecognizedKeyword_FallsThroughToFreeTextSearch()
    {
        CreateSeriesWithIssue("wri: something");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "wri: something";

        Assert.Equal(SearchMode.All, vm.SearchMode); // "wri" isn't a recognized keyword, mode untouched
        Assert.Single(vm.IssueList.Rows); // and the whole string is matched as free text, prefix included
    }

    [Fact]
    public void FieldPrefix_NoColon_NotParsed()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "writer";

        Assert.Equal(SearchMode.All, vm.SearchMode);
    }

    [Fact]
    public void ValueSuggestions_StartsWithRankedAboveContains()
    {
        int seriesId1 = CreateSeriesWithIssue("Some Series");
        SetIssueFields(seriesId1, i => i.Writer = "Dan Abnett");
        int seriesId2 = CreateSeriesWithIssue("Other Series");
        SetIssueFields(seriesId2, i => i.Writer = "Aidan Cross"); // contains "dan", doesn't start with it
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchMode = SearchMode.Writer;

        vm.SearchQuery = "Dan";

        var values = vm.SearchSuggestions.Where(s => s.Kind == SearchSuggestionKind.Value).Select(s => s.DisplayText).ToList();
        Assert.Equal(2, values.Count);
        Assert.Equal("Dan Abnett", values[0]);
        Assert.Equal("Aidan Cross", values[1]);
    }

    [Fact]
    public void ValueSuggestions_SkippedForFileMode()
    {
        int seriesId = CreateSeriesWithIssue("Some Series");
        SetIssueFields(seriesId, i => i.FilePath = "C:\\comics\\some-series-001.cbz");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchMode = SearchMode.File;

        vm.SearchQuery = "some";

        Assert.DoesNotContain(vm.SearchSuggestions, s => s.Kind == SearchSuggestionKind.Value);
    }

    [Fact]
    public void ValueSuggestions_CappedAtSix()
    {
        for (int i = 0; i < 10; i++)
        {
            int seriesId = CreateSeriesWithIssue($"Filler Series {i}");
            SetIssueFields(seriesId, issue => issue.Writer = $"Match Writer {i}");
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchMode = SearchMode.Writer;

        vm.SearchQuery = "Match";

        Assert.Equal(6, vm.SearchSuggestions.Count(s => s.Kind == SearchSuggestionKind.Value));
    }

    [Fact]
    public void SavedSearchSuggestions_OnlySmartCollectionsMatchByName()
    {
        CreateCollectionWithSeries("Manual Favorites", CreateSeries("X", ContentType.Comic));
        CreateSmartCollection("Rule Based Favorites");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        vm.SearchQuery = "Favorites";

        var savedSearchNames = vm.SearchSuggestions.Where(s => s.Kind == SearchSuggestionKind.SavedSearch).Select(s => s.DisplayText);
        Assert.Equal(new[] { "Rule Based Favorites" }, savedSearchNames);
    }

    [Fact]
    public void SelectSuggestion_SavedSearch_ClearsQueryAndSelectsCollection()
    {
        int collectionId = CreateSmartCollection("My Smart List");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchQuery = "Smart";
        var suggestion = vm.SearchSuggestions.Single(s => s.Kind == SearchSuggestionKind.SavedSearch);

        vm.SelectSuggestionCommand.Execute(suggestion);

        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.True(vm.Collections.Single(c => c.Id == collectionId).IsActive);
    }

    [Fact]
    public void SelectSuggestion_Recent_SetsSearchQueryOnly()
    {
        CreateSeriesWithIssue("Batman");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchQuery = "Batman";
        vm.FlushSearchHistoryDebounce();
        vm.SearchQuery = string.Empty;
        vm.OnSearchBoxGotFocus();
        var recentSuggestion = vm.SearchSuggestions.Single(s => s.Kind == SearchSuggestionKind.Recent);

        vm.SelectSuggestionCommand.Execute(recentSuggestion);

        Assert.Equal("Batman", vm.SearchQuery);
    }

    [Fact]
    public void SuggestionTotal_NeverExceedsTwelve()
    {
        for (int i = 0; i < 10; i++)
        {
            int seriesId = CreateSeriesWithIssue($"Alpha Series {i}");
            SetIssueFields(seriesId, issue => issue.Writer = $"Alpha Writer {i}");
        }

        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchMode = SearchMode.All;
        for (int i = 0; i < 8; i++)
        {
            vm.SearchQuery = $"alpha{i}";
            vm.FlushSearchHistoryDebounce();
        }

        CreateSmartCollection("Alpha Rule One");
        CreateSmartCollection("Alpha Rule Two");
        vm.LoadFromDatabase(); // picks up the two collections created via a separate context above

        vm.SearchQuery = "alpha";

        // Raw sources here total 5 (Recent, capped) + 6 (Value, capped) + 2 (SavedSearch) = 13.
        Assert.Equal(12, vm.SearchSuggestions.Count);
    }

    [Fact]
    public void KeyboardNav_ClampsAtBothEnds()
    {
        int seriesId = CreateSeriesWithIssue("Some Series");
        SetIssueFields(seriesId, i => i.Writer = "Solo Writer");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchMode = SearchMode.Writer;
        vm.SearchQuery = "Solo";
        Assert.NotEmpty(vm.SearchSuggestions);

        vm.MoveSuggestionSelection(-1); // Up before ever moving down - clamps at 0, not -1
        Assert.Equal(0, vm.SelectedSuggestionIndex);

        for (int i = 0; i < vm.SearchSuggestions.Count + 3; i++)
        {
            vm.MoveSuggestionSelection(1);
        }

        Assert.Equal(vm.SearchSuggestions.Count - 1, vm.SelectedSuggestionIndex);
    }

    [Fact]
    public void CommitSearchBox_NoSelection_CommitsRawTextImmediately()
    {
        CreateSeriesWithIssue("Batman");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SearchQuery = "Batman";

        vm.CommitSearchBox(); // no arrow-key selection made - commits the raw typed text immediately, not after the 800ms debounce

        Assert.False(vm.IsSuggestionsOpen);
        var recent = JsonSerializer.Deserialize<List<string>>(ReadAppSettings().LibraryRecentSearches!)!;
        Assert.Equal("Batman", recent[0]);
    }

    [Fact]
    public void CloseSuggestions_LeavesSearchQueryUntouched()
    {
        int seriesId = CreateSeriesWithIssue("Some Series");
        SetIssueFields(seriesId, i => i.Writer = "Solo Writer");
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.OnSearchBoxGotFocus();
        vm.SearchMode = SearchMode.Writer;
        vm.SearchQuery = "Solo";
        Assert.True(vm.IsSuggestionsOpen);

        vm.CloseSuggestionsCommand.Execute(null);

        Assert.False(vm.IsSuggestionsOpen);
        Assert.Equal("Solo", vm.SearchQuery);
    }
}

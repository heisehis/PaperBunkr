using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="HomeScreenViewModel"/>'s five modules (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md) end to end - query/pick logic itself is covered by
/// <c>HomeFeedResolverTests</c> (Paperbunkr.Data.Tests); this only needs to confirm the ViewModel
/// maps results into the right card types and wires navigation correctly. Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, matching
/// <see cref="LibraryScreenViewModelTests"/>'s pattern. Runs under <see cref="AvaloniaTestCollection"/>
/// since <c>SeriesCardSample</c>/cover-brush construction needs a real Skia platform.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public HomeScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_vm_test_{Guid.NewGuid():N}.db");
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

    private static (int SeriesId, int IssueId) SeedSeriesWithIssue(string seriesName, int? lastPageRead = null,
        int? pageCount = 100, DateTime? addedTime = null, DateTime? openedTime = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue
        {
            SeriesId = series.Id,
            LastPageRead = lastPageRead,
            PageCount = pageCount,
            AddedTime = addedTime,
            OpenedTime = openedTime,
        };
        context.Issues.Add(issue);
        context.SaveChanges();

        return (series.Id, issue.Id);
    }

    [Fact]
    public void Construct_PopulatesContinueReading_WithSeriesCardAndCorrectResumeIssue()
    {
        var (_, issueId) = SeedSeriesWithIssue("In Progress", lastPageRead: 30, pageCount: 100, openedTime: DateTime.UtcNow);

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        var card = Assert.Single(vm.ContinueReading);
        Assert.Equal("In Progress", card.Series.Name);
        Assert.Equal(issueId, card.ResumeIssueId);
        Assert.True(vm.HasContinueReading);
    }

    [Fact]
    public void Construct_PopulatesContinueReading_WithResumeProgressFractionFromReadPercentage()
    {
        SeedSeriesWithIssue("In Progress", lastPageRead: 30, pageCount: 100, openedTime: DateTime.UtcNow);

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.Equal(0.30, vm.ContinueReading[0].ResumeProgressFraction, precision: 5);
    }

    [Fact]
    public void OpenContinueReadingCommand_InvokesGoReaderForIssue_WithTheResumeIssueId()
    {
        var (_, issueId) = SeedSeriesWithIssue("In Progress", lastPageRead: 30, pageCount: 100, openedTime: DateTime.UtcNow);
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { }, (_, _) => { }, (_, _) => { });

        vm.OpenContinueReadingCommand.Execute(vm.ContinueReading[0].ResumeIssueId);

        Assert.Equal(issueId, readerIssueId);
    }

    [Fact]
    public void Construct_PopulatesRecentlyAdded_SortedNewestFirst()
    {
        SeedSeriesWithIssue("Older", addedTime: new DateTime(2026, 1, 1));
        SeedSeriesWithIssue("Newer", addedTime: new DateTime(2026, 6, 1));

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.Equal(new[] { "Newer", "Older" }, vm.RecentlyAdded.Select(c => c.Name));
        Assert.True(vm.HasRecentlyAdded);
    }

    [Fact]
    public void OpenSeriesCommand_InvokesGoDetailForSeries_WithTheCardsSeriesId()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Some Series", addedTime: DateTime.UtcNow);
        int? navigatedSeriesId = null;
        var vm = new HomeScreenViewModel(id => navigatedSeriesId = id, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        vm.OpenSeriesCommand.Execute(vm.RecentlyAdded[0]);

        Assert.Equal(seriesId, navigatedSeriesId);
    }

    /// <summary>
    /// End-to-end coverage for Module 3 ("Because You Read") - only the empty-state path
    /// (<see cref="Construct_NoData_AllModulesEmpty"/>) had a test before this; the actual wiring
    /// through <c>HomeFeedResolver.GetRecentlyOpenedSeriesIds</c> then <c>RecommendationResolver.
    /// GetRecommendations</c> into a real <see cref="BecauseYouReadRow"/> had none. Needs a real
    /// <see cref="MediaRelation"/> between the two series - <see cref="RecommendationResolver"/>'s
    /// candidate pool is relationally-anchored (docs/superpowers/specs/2026-08-18-metadata-model-
    /// phase6a-recommendation-engine-design.md), so genre/character overlap alone would never surface
    /// an otherwise-unrelated series here.
    /// </summary>
    [Fact]
    public void Construct_PopulatesBecauseYouRead_FromARelatedSeriesTheUserRecentlyOpened()
    {
        var (sourceId, _) = SeedSeriesWithIssue("Source Series", openedTime: DateTime.UtcNow);
        var (targetId, _) = SeedSeriesWithIssue("Target Series");

        using (var context = PaperbunkrDb.CreateContext())
        {
            var relation = new MediaRelation { SourceSeriesId = sourceId, TargetSeriesId = targetId, RelationType = RelationType.Sequel };
            relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
            context.MediaRelations.Add(relation);
            context.SaveChanges();
        }

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.True(vm.HasBecauseYouRead);
        var row = Assert.Single(vm.BecauseYouRead);
        Assert.Equal("Source Series", row.SeedSeriesName);
        var card = Assert.Single(row.Cards);
        Assert.Equal("Target Series", card.Name);
    }

    [Fact]
    public void Construct_NoData_AllModulesEmpty()
    {
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.False(vm.HasContinueReading);
        Assert.False(vm.HasRecentlyAdded);
        Assert.False(vm.HasBecauseYouRead);
        Assert.False(vm.HasSpotlight);
        Assert.False(vm.HasReadingListSpotlight);
    }

    [Fact]
    public void Construct_PopulatesSpotlight_WhenAnUnreadIssueExists()
    {
        SeedSeriesWithIssue("Untouched Series");

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.NotEmpty(vm.SpotlightItems);
        Assert.NotNull(vm.CurrentSpotlight);
        Assert.True(vm.HasSpotlight);
    }

    [Fact]
    public void OpenSpotlightCommand_InvokesGoReaderForIssue_WithTheCurrentSpotlightIssueId()
    {
        SeedSeriesWithIssue("Untouched Series");
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { }, (_, _) => { }, (_, _) => { });

        vm.OpenSpotlightCommand.Execute(null);

        Assert.NotNull(vm.CurrentSpotlight);
        Assert.Equal(vm.CurrentSpotlight!.IssueId, readerIssueId);
    }

    [Fact]
    public void SetSpotlightItemCommand_JumpsTheCarouselToTheClickedItem()
    {
        var seriesId = SeedSeriesWithIssue("Series").SeriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            for (int i = 0; i < 4; i++)
            {
                var issue = new Issue { SeriesId = seriesId };
                issue.MergeFrom(IssueTagField.Genre, new[] { $"Genre{i}" });
                context.Issues.Add(issue);
            }
            context.SaveChanges();
        }
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
        Assert.True(vm.SpotlightItems.Count > 1); // sanity: enough picks to actually exercise dot navigation

        var target = vm.SpotlightItems[^1];
        vm.SetSpotlightItemCommand.Execute(target);

        Assert.Same(target, vm.CurrentSpotlight);
    }

    [Fact]
    public void SearchCommand_NavigatesToLibraryWithTheTypedQuery()
    {
        string? capturedQuery = null;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, q => capturedQuery = q, (_, _) => { }, (_, _) => { }) { SearchQuery = "  Batman  " };

        vm.SearchCommand.Execute(null);

        Assert.Equal("Batman", capturedQuery);
    }

    [Fact]
    public void SearchCommand_NoOps_WhenQueryIsBlank()
    {
        bool invoked = false;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => invoked = true, (_, _) => { }, (_, _) => { }) { SearchQuery = "   " };

        vm.SearchCommand.Execute(null);

        Assert.False(invoked);
    }

    [Fact]
    public void GoToLibraryCommand_NavigatesToLibraryWithNoQuery()
    {
        string? capturedQuery = null;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, q => capturedQuery = q, (_, _) => { }, (_, _) => { });

        vm.GoToLibraryCommand.Execute(null);

        Assert.Equal(string.Empty, capturedQuery);
    }

    [Fact]
    public void OpenSpotlightCommand_NoOps_WhenSpotlightIsNull()
    {
        // Every issue is fully read - candidate pool is empty, Spotlight stays null.
        SeedSeriesWithIssue("Finished Series", lastPageRead: 100, pageCount: 100);
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { }, (_, _) => { }, (_, _) => { });

        var exception = Record.Exception(() => vm.OpenSpotlightCommand.Execute(null));

        Assert.Null(exception);
        Assert.Null(readerIssueId);
    }

    [Fact]
    public void Construct_PopulatesReadingListSpotlight_WhenAnUnreadListExists()
    {
        var (_, issueId) = SeedSeriesWithIssue("List Series");
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = new ReadingList { Name = "My List" };
            list.Items.Add(new ReadingListItem { IssueId = issueId, SortOrder = 0 });
            context.ReadingLists.Add(list);
            context.SaveChanges();
        }

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.NotNull(vm.ReadingListSpotlight);
        Assert.Equal("My List", vm.ReadingListSpotlight!.Name);
        Assert.True(vm.HasReadingListSpotlight);
    }

    [Fact]
    public void OpenReadingListSpotlightCommand_InvokesGoReaderForIssueInReadingList_WithTheFirstUnreadItemAndListId()
    {
        var (_, issueId) = SeedSeriesWithIssue("List Series");
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = new ReadingList { Name = "My List" };
            list.Items.Add(new ReadingListItem { IssueId = issueId, SortOrder = 0 });
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }
        int? readerIssueId = null;
        int? readerListId = null;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (issue, list) => { readerIssueId = issue; readerListId = list; }, (_, _) => { });

        vm.OpenReadingListSpotlightCommand.Execute(null);

        Assert.Equal(issueId, readerIssueId);
        Assert.Equal(listId, readerListId);
    }

    [Fact]
    public void LoadFromDatabase_ReReadsCurrentState()
    {
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
        Assert.False(vm.HasRecentlyAdded);

        SeedSeriesWithIssue("Added After Construct", addedTime: DateTime.UtcNow);
        vm.LoadFromDatabase();

        Assert.True(vm.HasRecentlyAdded);
    }

    // --- Continue Reading — Books (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-
    // strip-design.md) ---

    private static int SeedBook(string title, DateTime? lastOpened = null, int lastChapter = 0,
        bool finished = false, int chapterCount = 10, Paperbunkr.Data.Entities.BookFormat format = Paperbunkr.Data.Entities.BookFormat.Epub)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = new Paperbunkr.Data.Entities.Book
        {
            Title = title,
            Format = format,
            FilePath = $@"C:\books\{title}.epub",
            AddedTime = DateTime.UtcNow,
            LastOpenedTime = lastOpened,
            LastChapterIndex = lastChapter,
            Finished = finished,
            ChapterCount = chapterCount,
        };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public void HasBooksLibrary_FalseWithNoBooks_TrueWithAny()
    {
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
        Assert.False(vm.HasBooksLibrary);

        SeedBook("Dune");
        vm.LoadFromDatabase();
        Assert.True(vm.HasBooksLibrary);
    }

    [Fact]
    public void ContinueReadingBooks_IncludesOnlyStartedNotFinished_NewestFirst()
    {
        SeedBook("Old Progress", lastOpened: new DateTime(2024, 1, 1), lastChapter: 3);
        SeedBook("New Progress", lastOpened: new DateTime(2024, 6, 1), lastChapter: 2);
        SeedBook("Finished One", lastOpened: new DateTime(2024, 5, 1), lastChapter: 9, finished: true);
        SeedBook("Never Opened");

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

        Assert.Equal(new[] { "New Progress", "Old Progress" }, vm.ContinueReadingBooks.Select(c => c.Title));
        Assert.True(vm.HasContinueReadingBooks);
    }

    [Fact]
    public void OpenContinueReadingBookCommand_InvokesCallback_WithIdAndFormat()
    {
        int id = SeedBook("PDF Novel", lastOpened: DateTime.UtcNow, lastChapter: 1,
            format: Paperbunkr.Data.Entities.BookFormat.Pdf);
        (int Id, Paperbunkr.Data.Entities.BookFormat Format)? captured = null;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (bookId, fmt) => captured = (bookId, fmt));

        vm.OpenContinueReadingBookCommand.Execute(vm.ContinueReadingBooks.Single());

        Assert.Equal((id, Paperbunkr.Data.Entities.BookFormat.Pdf), captured);
    }
}

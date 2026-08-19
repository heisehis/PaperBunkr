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

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });

        var card = Assert.Single(vm.ContinueReading);
        Assert.Equal("In Progress", card.Series.Name);
        Assert.Equal(issueId, card.ResumeIssueId);
        Assert.True(vm.HasContinueReading);
    }

    [Fact]
    public void OpenContinueReadingCommand_InvokesGoReaderForIssue_WithTheResumeIssueId()
    {
        var (_, issueId) = SeedSeriesWithIssue("In Progress", lastPageRead: 30, pageCount: 100, openedTime: DateTime.UtcNow);
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { });

        vm.OpenContinueReadingCommand.Execute(vm.ContinueReading[0].ResumeIssueId);

        Assert.Equal(issueId, readerIssueId);
    }

    [Fact]
    public void Construct_PopulatesRecentlyAdded_SortedNewestFirst()
    {
        SeedSeriesWithIssue("Older", addedTime: new DateTime(2026, 1, 1));
        SeedSeriesWithIssue("Newer", addedTime: new DateTime(2026, 6, 1));

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });

        Assert.Equal(new[] { "Newer", "Older" }, vm.RecentlyAdded.Select(c => c.Name));
        Assert.True(vm.HasRecentlyAdded);
    }

    [Fact]
    public void OpenSeriesCommand_InvokesGoDetailForSeries_WithTheCardsSeriesId()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Some Series", addedTime: DateTime.UtcNow);
        int? navigatedSeriesId = null;
        var vm = new HomeScreenViewModel(id => navigatedSeriesId = id, _ => { }, _ => { });

        vm.OpenSeriesCommand.Execute(vm.RecentlyAdded[0]);

        Assert.Equal(seriesId, navigatedSeriesId);
    }

    [Fact]
    public void Construct_NoData_AllModulesEmpty()
    {
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });

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

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });

        Assert.NotEmpty(vm.SpotlightItems);
        Assert.NotNull(vm.CurrentSpotlight);
        Assert.True(vm.HasSpotlight);
    }

    [Fact]
    public void OpenSpotlightCommand_InvokesGoReaderForIssue_WithTheCurrentSpotlightIssueId()
    {
        SeedSeriesWithIssue("Untouched Series");
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { });

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
                context.Issues.Add(new Issue { SeriesId = seriesId, Genre = $"Genre{i}" });
            }
            context.SaveChanges();
        }
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });
        Assert.True(vm.SpotlightItems.Count > 1); // sanity: enough picks to actually exercise dot navigation

        var target = vm.SpotlightItems[^1];
        vm.SetSpotlightItemCommand.Execute(target);

        Assert.Same(target, vm.CurrentSpotlight);
    }

    [Fact]
    public void SearchCommand_NavigatesToLibraryWithTheTypedQuery()
    {
        string? capturedQuery = null;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, q => capturedQuery = q) { SearchQuery = "  Batman  " };

        vm.SearchCommand.Execute(null);

        Assert.Equal("Batman", capturedQuery);
    }

    [Fact]
    public void SearchCommand_NoOps_WhenQueryIsBlank()
    {
        bool invoked = false;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => invoked = true) { SearchQuery = "   " };

        vm.SearchCommand.Execute(null);

        Assert.False(invoked);
    }

    [Fact]
    public void GoToLibraryCommand_NavigatesToLibraryWithNoQuery()
    {
        string? capturedQuery = null;
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, q => capturedQuery = q);

        vm.GoToLibraryCommand.Execute(null);

        Assert.Equal(string.Empty, capturedQuery);
    }

    [Fact]
    public void OpenSpotlightCommand_NoOps_WhenSpotlightIsNull()
    {
        // Every issue is fully read - candidate pool is empty, Spotlight stays null.
        SeedSeriesWithIssue("Finished Series", lastPageRead: 100, pageCount: 100);
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { });

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

        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });

        Assert.NotNull(vm.ReadingListSpotlight);
        Assert.Equal("My List", vm.ReadingListSpotlight!.Name);
        Assert.True(vm.HasReadingListSpotlight);
    }

    [Fact]
    public void OpenReadingListSpotlightCommand_InvokesGoReaderForIssue_WithTheFirstUnreadItem()
    {
        var (_, issueId) = SeedSeriesWithIssue("List Series");
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = new ReadingList { Name = "My List" };
            list.Items.Add(new ReadingListItem { IssueId = issueId, SortOrder = 0 });
            context.ReadingLists.Add(list);
            context.SaveChanges();
        }
        int? readerIssueId = null;
        var vm = new HomeScreenViewModel(_ => { }, id => readerIssueId = id, _ => { });

        vm.OpenReadingListSpotlightCommand.Execute(null);

        Assert.Equal(issueId, readerIssueId);
    }

    [Fact]
    public void LoadFromDatabase_ReReadsCurrentState()
    {
        var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { });
        Assert.False(vm.HasRecentlyAdded);

        SeedSeriesWithIssue("Added After Construct", addedTime: DateTime.UtcNow);
        vm.LoadFromDatabase();

        Assert.True(vm.HasRecentlyAdded);
    }
}

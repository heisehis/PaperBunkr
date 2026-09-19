using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First test coverage for <see cref="DetailTabsViewModel"/> - previously had none. Exercises
/// <c>ToggleReadingModeCommand</c> (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md
/// §5), added alongside its own <c>Func&lt;PaperbunkrDbContext&gt;</c> test-injection seam. Joins
/// <see cref="AvaloniaTestCollection"/> to match every other ViewModel test in this suite that
/// touches series/issue cover rendering (<c>SeriesCardSample</c>/<c>CoverImageCache</c>).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailTabsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _seriesId;

    public DetailTabsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detailtabsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        context.Issues.AddRange(
            new Issue { SeriesId = series.Id, Number = "1" },
            new Issue { SeriesId = series.Id, Number = "2" },
            new Issue { SeriesId = series.Id, Number = "3" });
        context.SaveChanges();
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

    private DetailTabsViewModel CreateViewModel(Action<int>? goToProperties = null, Action<IReadOnlyList<int>>? goToBulkProperties = null, Action? onSelectionChanged = null, IMetadataProvider? metadataProvider = null, Action<int>? navigateToCollection = null, Action<int>? openInReader = null, Action<string>? goLibraryWithSearch = null, Paperbunkr.App.Services.ITrackerAutoSyncService? trackerAutoSync = null, bool isMangaHost = false) =>
        new(goToProperties ?? (_ => { }), goToBulkProperties ?? (_ => { }), onSelectionChanged, () => new PaperbunkrDbContext(_dbOptions), metadataProvider ?? new FakeMetadataProvider(), onQuickRate: null, navigateToSeries: null, openInReader: openInReader, navigateToCollection: navigateToCollection, goLibraryWithSearch: goLibraryWithSearch, trackerAutoSync: trackerAutoSync) { IsMangaDetailHost = isMangaHost };

    private sealed class RecordingTrackerAutoSync : Paperbunkr.App.Services.ITrackerAutoSyncService
    {
        public List<IReadOnlyCollection<int>> MarkedRead { get; } = new();
        public List<int> Finished { get; } = new();
        public List<int> Pulled { get; } = new();
        public IReadOnlyList<int> PullResultIds { get; set; } = Array.Empty<int>();

        public Task OnIssueFinishedInReaderAsync(int seriesId) { Finished.Add(seriesId); return Task.CompletedTask; }

        public Task OnIssuesMarkedReadAsync(IReadOnlyCollection<int> seriesIds) { MarkedRead.Add(seriesIds); return Task.CompletedTask; }

        public Task<Paperbunkr.App.Services.TrackerPullResult> PullSeriesAsync(int seriesId)
        {
            Pulled.Add(seriesId);
            return Task.FromResult(new Paperbunkr.App.Services.TrackerPullResult(PullResultIds));
        }
    }

    /// <summary>No-network stand-in for <see cref="AniListMetadataProvider"/> - see docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-and-link-design.md.</summary>
    private sealed class FakeMetadataProvider : IMetadataProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.AniList;
        public List<MetadataSearchResult> SearchResults { get; } = new();
        public ExternalMediaMetadata? GetResult { get; set; }

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(SearchResults);

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult);
    }

    private Series LoadSeriesEntity()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Series.Include(s => s.Issues).First(s => s.Id == _seriesId);
    }

    [Fact]
    public void LoadSeries_IssueWithFormat_TileHasFormat()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        // "Omnibus" is itself a SpecialFormatCatalog value (found running this test - IsSpecial()
        // routes it into Specials, not Issues), so check both collections rather than assume Issues.
        series.Issues[0].Format = "Omnibus";

        vm.LoadSeries(series);

        var tile = vm.Issues.Concat(vm.Specials).Single(i => i.Id == series.Issues[0].Id);
        Assert.True(tile.HasFormat);
        Assert.Equal("Omnibus", tile.Format);
    }

    [Fact]
    public void LoadSeries_IssueWithBlankFormat_TileHasNoFormat()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();

        vm.LoadSeries(series);

        var tile = vm.Issues.Single(i => i.Id == series.Issues[0].Id);
        Assert.False(tile.HasFormat);
    }

    [Fact]
    public void ActiveDetailsSubTab_DefaultsToInfo_AndGoLinkingFlipsIt()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        Assert.True(vm.IsInfoSubTab);
        Assert.False(vm.IsLinkingSubTab);

        vm.GoLinkingSubTabCommand.Execute(null);

        Assert.False(vm.IsInfoSubTab);
        Assert.True(vm.IsLinkingSubTab);

        vm.GoInfoSubTabCommand.Execute(null);

        Assert.True(vm.IsInfoSubTab);
    }

    [Fact]
    public void LoadSeries_ResetsActiveDetailsSubTabToInfo()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.GoLinkingSubTabCommand.Execute(null);
        Assert.True(vm.IsLinkingSubTab);

        vm.LoadSeries(LoadSeriesEntity());

        Assert.True(vm.IsInfoSubTab);
    }

    [Fact]
    public void LoadSeries_PopulatesReadingModeLabel()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.Equal("Left to Right", vm.ReadingModeLabel);
    }

    [Fact]
    public void LoadSeries_NoReadingEvents_ActivityEmpty()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasActivity);
        Assert.Empty(vm.Activity);
    }

    [Fact]
    public void LoadSeries_PopulatesActivity_FromReadingEventsForSeriesIssues()
    {
        int issue2Id;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            issue2Id = context.Issues.First(i => i.SeriesId == _seriesId && i.Number == "2").Id;
            context.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = issue2Id,
                Kind = ReadingEventKind.Finished,
                TimestampUtc = DateTime.UtcNow,
                PagesRead = 22,
                SeriesId = _seriesId,
            });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        Assert.True(vm.HasActivity);
        Assert.Single(vm.Activity);
        Assert.Equal("Finished Issue #2 · 22 pages", vm.Activity[0].Label);
    }

    [Fact]
    public void LoadSeries_MergesSeriesActivityEventsWithReadingEvents_InTimeOrder()
    {
        // ReadingEvent explicitly backdated 10 minutes; SeriesActivityLog.Record's own
        // DateTime.UtcNow naturally lands after that with no need to override it.
        var older = DateTime.UtcNow.AddMinutes(-10);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            int issueId = context.Issues.First(i => i.SeriesId == _seriesId && i.Number == "1").Id;
            context.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = issueId,
                Kind = ReadingEventKind.Opened,
                TimestampUtc = older,
                SeriesId = _seriesId,
            });
            SeriesActivityLog.Record(context, _seriesId, SeriesActivityEventKind.TrackerLinked, "AniList");
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        Assert.Equal(2, vm.Activity.Count);
        Assert.Equal("Linked AniList tracker", vm.Activity[0].Label);
        Assert.Equal("Opened Issue #1", vm.Activity[1].Label);
    }

    [Fact]
    public void LoadSeries_ActivityCap_Is20Combined_NotPerSource()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            int issueId = context.Issues.First(i => i.SeriesId == _seriesId && i.Number == "1").Id;
            for (int i = 0; i < 15; i++)
            {
                context.ReadingEvents.Add(new ReadingEvent
                {
                    ItemType = ReadingItemType.Comic,
                    ItemId = issueId,
                    Kind = ReadingEventKind.Opened,
                    TimestampUtc = DateTime.UtcNow.AddMinutes(-i),
                    SeriesId = _seriesId,
                });
            }
            for (int i = 0; i < 15; i++)
            {
                SeriesActivityLog.Record(context, _seriesId, SeriesActivityEventKind.TrackerSynced, $"Sync {i}");
            }
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        Assert.Equal(20, vm.Activity.Count);
    }

    [Fact]
    public void ToggleReadingMode_LeftToRight_FlipsToRightToLeft_AndPersists()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleReadingModeCommand.Execute(null);

        Assert.Equal("Right to Left", vm.ReadingModeLabel);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ReadingMode.RightToLeft, context.Series.First(s => s.Id == _seriesId).ReadingMode);
    }

    [Fact]
    public void ToggleReadingMode_RightToLeft_FlipsBackToLeftToRight()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleReadingModeCommand.Execute(null);
        Assert.Equal("Right to Left", vm.ReadingModeLabel);

        vm.ToggleReadingModeCommand.Execute(null);

        Assert.Equal("Left to Right", vm.ReadingModeLabel);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ReadingMode.LeftToRight, context.Series.First(s => s.Id == _seriesId).ReadingMode);
    }

    /// <summary>docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §1/§2 - fixture issues have no FilePath, matching a fileless placeholder.</summary>
    [Fact]
    public void LoadSeries_PopulatesHasFile_FalseForFilelessIssues()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.All(vm.Issues, issue => Assert.False(issue.HasFile));
    }

    /// <summary>docs/superpowers/specs/2026-08-30-series-detail-run-separator-design.md - collapse rule.</summary>
    [Fact]
    public void LoadSeries_NoVolumeSetAnywhere_CollapsesToOneUnheadedGroup()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        var group = Assert.Single(vm.IssueGroups);
        Assert.Null(group.Header);
        Assert.Equal(new[] { "#1", "#2", "#3" }, group.Items.Select(i => i.Title));
        Assert.Equal(vm.Issues.Select(i => i.Id), group.Items.Select(i => i.Id));
    }

    [Fact]
    public void LoadSeries_MultipleVolumes_GroupsByVolumeWithMainWriterInHeader()
    {
        int seriesId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Venom" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;

            context.Issues.AddRange(
                new Issue { SeriesId = seriesId, Number = "1" }, // no Volume - "no volume set" bucket
                new Issue { SeriesId = seriesId, Number = "1", Volume = "2018", Writer = "Al Ewing" },
                new Issue { SeriesId = seriesId, Number = "2", Volume = "2018", Writer = "Al Ewing" },
                new Issue { SeriesId = seriesId, Number = "3", Volume = "2018", Writer = "Fill-In Writer" }, // one fill-in issue - main writer still wins
                new Issue { SeriesId = seriesId, Number = "1", Volume = "2022", Writer = "Donny Cates" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            vm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == seriesId));
        }

        Assert.Equal(3, vm.IssueGroups.Count);

        Assert.Null(vm.IssueGroups[0].Header);
        Assert.Equal(new[] { "#1" }, vm.IssueGroups[0].Items.Select(i => i.Title));

        Assert.Equal("Al Ewing (2018)", vm.IssueGroups[1].Header);
        Assert.Equal(new[] { "#1", "#2", "#3" }, vm.IssueGroups[1].Items.Select(i => i.Title));

        Assert.Equal("Donny Cates (2022)", vm.IssueGroups[2].Header);
        Assert.Equal(new[] { "#1" }, vm.IssueGroups[2].Items.Select(i => i.Title));

        Assert.Equal(5, vm.IssueGroups.Sum(g => g.Items.Count));
        Assert.Equal(5, vm.Issues.Count);
        Assert.Equal(vm.Issues.Select(i => i.Id), vm.IssueGroups.SelectMany(g => g.Items).Select(i => i.Id));
    }

    /// <summary>No issue in the run has a Writer set - header falls back to plain "Volume {value}".</summary>
    [Fact]
    public void LoadSeries_MultipleVolumes_NoWriterSet_HeaderFallsBackToVolumeOnly()
    {
        int seriesId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Venom" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;

            context.Issues.AddRange(
                new Issue { SeriesId = seriesId, Number = "1", Volume = "2018" },
                new Issue { SeriesId = seriesId, Number = "1", Volume = "2022" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            vm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == seriesId));
        }

        Assert.Equal(new[] { "Volume 2018", "Volume 2022" }, vm.IssueGroups.Select(g => g.Header));
    }

    /// <summary>docs/superpowers/specs/2026-08-28-series-detail-specials-tab-design.md.</summary>
    [Fact]
    public void LoadSeries_NoSpecialFormatIssues_HasSpecialsFalse()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasSpecials);
        Assert.Empty(vm.Specials);
    }

    [Fact]
    public void LoadSeries_SpecialFormatIssue_RoutesToSpecialsNotIssuesAndNotDuplicated()
    {
        int seriesId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Venom" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;

            context.Issues.AddRange(
                new Issue { SeriesId = seriesId, Number = "1" },
                new Issue { SeriesId = seriesId, Number = "2" },
                new Issue { SeriesId = seriesId, Number = "1", Format = "Annual" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            vm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == seriesId));
        }

        Assert.True(vm.HasSpecials);
        var special = Assert.Single(vm.Specials);
        Assert.Equal("#1", special.Title);

        Assert.Equal(2, vm.Issues.Count);
        Assert.DoesNotContain(vm.Issues, i => i.Id == special.Id);
        Assert.DoesNotContain(vm.IssueGroups.SelectMany(g => g.Items), i => i.Id == special.Id);
    }

    [Fact]
    public void GoSpecialsCommand_SetsActiveTabAndIsSpecialsTab()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.GoSpecialsCommand.Execute(null);

        Assert.Equal("specials", vm.ActiveTab);
        Assert.True(vm.IsSpecialsTab);
        Assert.False(vm.IsIssuesTab);
    }

    [Fact]
    public void RevealIssue_WithNoFilePath_DoesNotThrow()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var issueCard = vm.Issues.First();

        var exception = Record.Exception(() => vm.RevealIssueCommand.Execute(issueCard));

        Assert.Null(exception);
    }

    [Fact]
    public void RevealIssue_WithMultipleSelected_ResolvesUniqueFoldersAcrossSelection()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var issue1 = context.Issues.First(i => i.Number == "1");
            var issue2 = context.Issues.First(i => i.Number == "2");
            var issue3 = context.Issues.First(i => i.Number == "3");
            issue1.FilePath = @"C:\Comics\SeriesA\issue1.cbz";
            issue2.FilePath = @"C:\Comics\SeriesA\issue2.cbz"; // same folder as issue1 - should dedupe
            issue3.FilePath = @"C:\Comics\SeriesB\issue3.cbz";
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var first = vm.Issues.First(i => i.Title == "#1");
        var second = vm.Issues.First(i => i.Title == "#2");
        var third = vm.Issues.First(i => i.Title == "#3");
        vm.ToggleIssueSelection(first, isShiftHeld: false);
        vm.ToggleIssueSelection(second, isShiftHeld: false);

        var exception = Record.Exception(() => vm.RevealIssueCommand.Execute(third));

        Assert.Null(exception);
    }

    // --- Mark as Read/Unread (docs/superpowers/specs/2026-08-23-mark-as-read-design.md) ---

    [Fact]
    public void MarkIssueRead_NoSelection_MarksOnlyTheClickedIssue()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            foreach (var issue in context.Issues)
            {
                issue.PageCount = 20;
            }

            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var target = vm.Issues.First(i => i.Title == "#1");

        vm.MarkIssueReadCommand.Execute(target);

        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(19, verifyContext.Issues.First(i => i.Number == "1").LastPageRead);
        Assert.Null(verifyContext.Issues.First(i => i.Number == "2").LastPageRead);
        Assert.False(vm.Issues.First(i => i.Title == "#1").IsUnread);
    }

    /// <summary>Bug found 2026-09-04: marking an issue read/unread from a Detail tile (right-click
    /// or the Card-view checkmark) never told the host screen, so the hero's series-wide unread
    /// count went stale until the next full reload ("the unread doesn't update when i finish
    /// reading a comic"). <see cref="MarkIssuesReadState"/>-backed commands must invoke the same
    /// selection-changed callback every other read-state/selection change already routes through.</summary>
    [Fact]
    public void MarkIssueRead_InvokesOnSelectionChanged_SoTheHostCanRefreshUnreadCount()
    {
        int changed = 0;
        var vm = CreateViewModel(onSelectionChanged: () => changed++);
        vm.LoadSeries(LoadSeriesEntity());

        vm.MarkIssueReadCommand.Execute(vm.Issues.First(i => i.Title == "#1"));
        Assert.True(changed > 0);

        int afterRead = changed;
        vm.MarkIssueUnreadCommand.Execute(vm.Issues.First(i => i.Title == "#1"));
        Assert.True(changed > afterRead);
    }

    /// <summary>The tile-swap after Mark as Read only ever checked <c>Issues</c> - a special tile
    /// silently kept its stale IsRead/dim state. Found alongside the bug above.</summary>
    [Fact]
    public void MarkIssueRead_OnASpecialTile_UpdatesTheSpecialsTileToo()
    {
        int seriesId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Has Specials" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
            context.Issues.Add(new Issue { SeriesId = seriesId, Number = "1", Format = "Annual", PageCount = 10 });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            vm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == seriesId));
        }

        var special = Assert.Single(vm.Specials);
        Assert.True(special.IsUnread);

        vm.MarkIssueReadCommand.Execute(special);

        Assert.False(Assert.Single(vm.Specials).IsUnread);
    }

    [Fact]
    public void MarkIssueRead_WithSelection_MarksTheWholeUnion_NotJustTheClickedTile()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            foreach (var issue in context.Issues)
            {
                issue.PageCount = 20;
            }

            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var first = vm.Issues.First(i => i.Title == "#1");
        var second = vm.Issues.First(i => i.Title == "#2");
        var third = vm.Issues.First(i => i.Title == "#3");
        vm.ToggleIssueSelection(first, isShiftHeld: false);
        vm.ToggleIssueSelection(second, isShiftHeld: false);

        vm.MarkIssueReadCommand.Execute(third); // right-click an unselected tile - unions it in, same as EditIssueProperties/RevealIssue

        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(19, verifyContext.Issues.First(i => i.Number == "1").LastPageRead);
        Assert.Equal(19, verifyContext.Issues.First(i => i.Number == "2").LastPageRead);
        Assert.Equal(19, verifyContext.Issues.First(i => i.Number == "3").LastPageRead);
    }

    [Fact]
    public void MarkIssueUnread_ZeroesLastPageRead_AndFlipsTheTileBackToUnread()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var issue = context.Issues.First(i => i.Number == "1");
            issue.PageCount = 20;
            issue.LastPageRead = 19;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var target = vm.Issues.First(i => i.Title == "#1");
        Assert.False(target.IsUnread);

        vm.MarkIssueUnreadCommand.Execute(target);

        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(0, verifyContext.Issues.First(i => i.Number == "1").LastPageRead);
        Assert.True(vm.Issues.First(i => i.Title == "#1").IsUnread);
    }

    [Fact]
    public void EditIssueProperties_NoSelection_InvokesSinglePropertiesCallback_WithClickedIssueId()
    {
        int? capturedId = null;
        var vm = CreateViewModel(id => capturedId = id);
        vm.LoadSeries(LoadSeriesEntity());
        var issueCard = vm.Issues.First();

        vm.EditIssuePropertiesCommand.Execute(issueCard);

        Assert.Equal(issueCard.Id, capturedId);
    }

    [Fact]
    public void ToggleIssueSelection_PlainClick_TogglesSelection()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var first = vm.Issues[0];

        vm.ToggleIssueSelection(first, isShiftHeld: false);
        Assert.True(first.IsSelected);
        Assert.Contains(first.Id, vm.SelectedIssueIds);

        vm.ToggleIssueSelection(first, isShiftHeld: false);
        Assert.False(first.IsSelected);
        Assert.DoesNotContain(first.Id, vm.SelectedIssueIds);
    }

    [Fact]
    public void FocusIssue_PlainClick_ReplacesAnyExistingSelection()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleIssueSelection(vm.Issues[0], isShiftHeld: false); // ctrl-style multi-select
        vm.ToggleIssueSelection(vm.Issues[1], isShiftHeld: false);
        Assert.Equal(2, vm.SelectedIssueIds.Count);

        vm.FocusIssue(vm.Issues[2]);

        Assert.Equal(new[] { vm.Issues[2].Id }, vm.SelectedIssueIds);
        Assert.False(vm.Issues[0].IsSelected);
        Assert.False(vm.Issues[1].IsSelected);
        Assert.True(vm.Issues[2].IsSelected);
    }

    [Fact]
    public void FocusIssue_OnSpecialsTabTile_SelectsIt()
    {
        int seriesId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Has Specials" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
            context.Issues.AddRange(
                new Issue { SeriesId = seriesId, Number = "1" },
                new Issue { SeriesId = seriesId, Number = "1", Format = "Annual" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            vm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == seriesId));
        }

        var special = Assert.Single(vm.Specials);
        vm.FocusIssue(special);

        Assert.True(special.IsSelected);
        Assert.Equal(new[] { special.Id }, vm.SelectedIssueIds);
    }

    [Fact]
    public void OpenIssue_InvokesOpenInReaderCallback_ForSpecialsToo()
    {
        int seriesId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Open Me" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
            context.Issues.AddRange(
                new Issue { SeriesId = seriesId, Number = "1" },
                new Issue { SeriesId = seriesId, Number = "1", Format = "Annual" });
            context.SaveChanges();
        }

        int? opened = null;
        var vm = CreateViewModel(openInReader: id => opened = id);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            vm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == seriesId));
        }

        vm.OpenIssue(vm.Specials.Single());

        Assert.Equal(vm.Specials.Single().Id, opened);
    }

    [Fact]
    public void ToggleIssueSelection_ShiftClick_SelectsContiguousRange()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleIssueSelection(vm.Issues[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.Issues[2], isShiftHeld: true);

        Assert.True(vm.Issues[0].IsSelected);
        Assert.True(vm.Issues[1].IsSelected);
        Assert.True(vm.Issues[2].IsSelected);
        Assert.Equal(3, vm.SelectedIssueIds.Count);
    }

    [Fact]
    public void EditIssueProperties_TwoOrMoreSelected_InvokesBulkPropertiesCallback_WithUnionOfIds()
    {
        IReadOnlyList<int>? capturedIds = null;
        var vm = CreateViewModel(goToBulkProperties: ids => capturedIds = ids);
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleIssueSelection(vm.Issues[0], isShiftHeld: false);
        vm.EditIssuePropertiesCommand.Execute(vm.Issues[1]); // right-click an unselected tile - unions it in

        Assert.NotNull(capturedIds);
        Assert.Equal(2, capturedIds!.Count);
        Assert.Contains(vm.Issues[0].Id, capturedIds);
        Assert.Contains(vm.Issues[1].Id, capturedIds);
    }

    [Fact]
    public void ToggleReadingMode_FromVerticalContinuous_CollapsesToRightToLeft()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.First(s => s.Id == _seriesId).ReadingMode = ReadingMode.VerticalContinuous;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleReadingModeCommand.Execute(null);

        Assert.Equal("Right to Left", vm.ReadingModeLabel);
    }

    // --- Related tab (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-
    // design.md) ---

    private int AddOtherSeries(string name = "Other Series")
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void LoadSeries_NoRelations_HasRelatedFalse_ShowsNoneYet()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasRelated);
        Assert.Empty(vm.Related);
    }

    [Fact]
    public void ToggleAddRelation_TogglesPanelState_AndClearsSearch()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleAddRelationCommand.Execute(null);
        Assert.True(vm.IsAddingRelation);

        vm.ToggleAddRelationCommand.Execute(null);
        Assert.False(vm.IsAddingRelation);
        Assert.Equal(string.Empty, vm.RelationSearchQuery);
    }

    [Fact]
    public void RelationSearchQuery_ExcludesCurrentSeries_MatchesByName()
    {
        AddOtherSeries("Justice League");
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity()); // "Test Series"

        vm.RelationSearchQuery = "justice"; // case-insensitive substring

        var result = Assert.Single(vm.RelationSearchResults);
        Assert.Equal("Justice League", result.Name);
    }

    [Fact]
    public void AddRelation_CreatesRelation_RefreshesRelatedTab_ClosesPanel()
    {
        int otherId = AddOtherSeries("Justice League");
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.SelectedRelationType = RelationType.Crossover;
        vm.ToggleAddRelationCommand.Execute(null);
        vm.RelationSearchQuery = "justice";
        var target = Assert.Single(vm.RelationSearchResults);

        vm.AddRelationCommand.Execute(target);

        Assert.False(vm.IsAddingRelation);
        var related = Assert.Single(vm.Related);
        Assert.Equal("Justice League", related.Name);
        Assert.Equal(otherId, related.RelatedSeriesId);
        Assert.True(vm.HasRelated);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.MediaRelations);
    }

    [Fact]
    public void AddRelation_NamedInversePair_ShowsInverseLabelFromSourceSeriesOwnPage()
    {
        // Current series ("Test Series") is added as the SOURCE, RelationType=Prequel - meaning
        // "Test Series is the Prequel of Justice League Origin." Viewed from the target's OWN page
        // (Justice League Origin), the source's card correctly shows the stored type as-is
        // ("Prequel" - that literally is Test Series's role). Viewed from the SOURCE's own page
        // (Test Series, this vm), the target's card needs the inverse ("Sequel" - Justice League
        // Origin's own role, the later work).
        int otherId = AddOtherSeries("Justice League Origin");
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.SelectedRelationType = RelationType.Prequel;
        var target = new Paperbunkr.App.Models.RelationSearchResult(MediaRelationEndpointKind.Series, otherId, null, "Justice League Origin");

        vm.AddRelationCommand.Execute(target);

        var related = Assert.Single(vm.Related);
        Assert.Equal("Sequel", related.Note);

        var otherVm = CreateViewModel();
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            otherVm.LoadSeries(context.Series.Include(s => s.Issues).First(s => s.Id == otherId));
        }

        var relatedFromOther = Assert.Single(otherVm.Related);
        Assert.Equal("Prequel", relatedFromOther.Note);
    }

    [Fact]
    public void RemoveRelation_DeletesRelation_ClearsFromRelatedTab()
    {
        int otherId = AddOtherSeries("Justice League");
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var target = new Paperbunkr.App.Models.RelationSearchResult(MediaRelationEndpointKind.Series, otherId, null, "Justice League");
        vm.AddRelationCommand.Execute(target);
        var related = Assert.Single(vm.Related);

        vm.RemoveRelationCommand.Execute(related);

        Assert.Empty(vm.Related);
        Assert.False(vm.HasRelated);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.MediaRelations);
    }

    // --- Collection nodes (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-
    // design.md) ---

    private int AddCollection(string name)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var collection = new Collection { Name = name };
        context.Collections.Add(collection);
        context.SaveChanges();
        return collection.Id;
    }

    [Fact]
    public void SearchRelationCandidates_FindsBothSeriesAndCollections()
    {
        AddOtherSeries("Justice League");
        AddCollection("Justice League Omnibus");
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.RelationSearchQuery = "justice";

        Assert.Equal(2, vm.RelationSearchResults.Count);
        Assert.Contains(vm.RelationSearchResults, r => r.Kind == MediaRelationEndpointKind.Series);
        Assert.Contains(vm.RelationSearchResults, r => r.Kind == MediaRelationEndpointKind.Collection);
    }

    [Fact]
    public void AddRelation_ToCollection_PopulatesRelatedTabWithCollectionEndpoint()
    {
        int collectionId = AddCollection("Omnibus");
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.SelectedRelationType = RelationType.Crossover;
        var target = new Paperbunkr.App.Models.RelationSearchResult(MediaRelationEndpointKind.Collection, null, collectionId, "Omnibus");

        vm.AddRelationCommand.Execute(target);

        var related = Assert.Single(vm.Related);
        Assert.Equal(MediaRelationEndpointKind.Collection, related.Kind);
        Assert.Equal(collectionId, related.RelatedCollectionId);
        Assert.Null(related.RelatedSeriesId);
        Assert.Equal("Omnibus", related.Name);
    }

    [Fact]
    public void OpenRelatedSeries_CollectionPayload_RoutesToNavigateToCollection()
    {
        int collectionId = AddCollection("Omnibus");
        int? navigatedCollectionId = null;
        var vm = CreateViewModel(navigateToCollection: id => navigatedCollectionId = id);
        vm.LoadSeries(LoadSeriesEntity());
        var target = new Paperbunkr.App.Models.RelationSearchResult(MediaRelationEndpointKind.Collection, null, collectionId, "Omnibus");
        vm.AddRelationCommand.Execute(target);
        var related = Assert.Single(vm.Related);

        vm.OpenRelatedSeriesCommand.Execute(related);

        Assert.Equal(collectionId, navigatedCollectionId);
    }

    // --- Continuity membership (docs/superpowers/specs/2026-08-17-metadata-model-phase4a-
    // continuity-design.md) ---

    [Fact]
    public void LoadSeries_NoContinuities_HasSameContinuityFalse_NoChips()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasSameContinuity);
        Assert.Empty(vm.SameContinuity);
        Assert.Empty(vm.ContinuityChips);
    }

    [Fact]
    public void ToggleAddContinuity_TogglesPanelState_AndClearsSearch()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleAddContinuityCommand.Execute(null);
        Assert.True(vm.IsAddingContinuity);

        vm.ToggleAddContinuityCommand.Execute(null);
        Assert.False(vm.IsAddingContinuity);
        Assert.Equal(string.Empty, vm.ContinuitySearchQuery);
    }

    [Fact]
    public void ContinuitySearchQuery_NoExistingMatch_OffersCreateNewRow()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ContinuitySearchQuery = "Earth-616";

        var result = Assert.Single(vm.ContinuitySearchResults);
        Assert.True(result.IsNew);
        Assert.Equal("Earth-616", result.Name);
    }

    [Fact]
    public void ContinuitySearchQuery_ExistingMatch_DoesNotOfferCreateNewRow()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Paperbunkr.Data.Metadata.ContinuityResolver.GetOrCreate(context, "Earth-616");
        }
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ContinuitySearchQuery = "earth-616"; // case-insensitive substring, matches the exact-name check

        var result = Assert.Single(vm.ContinuitySearchResults);
        Assert.False(result.IsNew);
    }

    [Fact]
    public void AddContinuity_NewName_CreatesContinuityAndMembership_ClosesPanel()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleAddContinuityCommand.Execute(null);
        vm.ContinuitySearchQuery = "Earth-616";
        var target = Assert.Single(vm.ContinuitySearchResults);

        vm.AddContinuityCommand.Execute(target);

        Assert.False(vm.IsAddingContinuity);
        var chip = Assert.Single(vm.ContinuityChips);
        Assert.Equal("Earth-616", chip.Name);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Continuities);
    }

    [Fact]
    public void AddContinuity_ExistingContinuity_SameSeriesSharesItAppearsUnderSameContinuity()
    {
        int otherId = AddOtherSeries("Ultimate Spider-Man");
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var continuity = Paperbunkr.Data.Metadata.ContinuityResolver.GetOrCreate(context, "Earth-616");
            Paperbunkr.Data.Metadata.ContinuityResolver.AddSeriesToContinuity(context, otherId, continuity.Id);
        }
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleAddContinuityCommand.Execute(null);
        vm.ContinuitySearchQuery = "Earth-616";
        var target = Assert.Single(vm.ContinuitySearchResults);
        Assert.False(target.IsNew);

        vm.AddContinuityCommand.Execute(target);

        var sameContinuity = Assert.Single(vm.SameContinuity);
        Assert.Equal(otherId, sameContinuity.SeriesId);
        Assert.True(vm.HasSameContinuity);
    }

    [Fact]
    public void RemoveContinuity_ClearsChipAndSameContinuitySection()
    {
        int otherId = AddOtherSeries("Ultimate Spider-Man");
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var continuity = Paperbunkr.Data.Metadata.ContinuityResolver.GetOrCreate(context, "Earth-616");
            Paperbunkr.Data.Metadata.ContinuityResolver.AddSeriesToContinuity(context, otherId, continuity.Id);
        }
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleAddContinuityCommand.Execute(null);
        vm.ContinuitySearchQuery = "Earth-616";
        vm.AddContinuityCommand.Execute(Assert.Single(vm.ContinuitySearchResults));
        var chip = Assert.Single(vm.ContinuityChips);

        vm.RemoveContinuityCommand.Execute(chip);

        Assert.Empty(vm.ContinuityChips);
        Assert.Empty(vm.SameContinuity);
        Assert.False(vm.HasSameContinuity);
    }

    // --- Collection membership (docs/superpowers/specs/2026-08-27-collections-design.md, step 10) -
    // byte-for-byte the Continuity tests above. ---

    [Fact]
    public void LoadSeries_NoCollections_HasSameCollectionFalse_NoChips()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasSameCollection);
        Assert.Empty(vm.SameCollection);
        Assert.Empty(vm.CollectionChips);
    }

    [Fact]
    public void ToggleAddCollection_TogglesPanelState_AndClearsSearch()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleAddCollectionCommand.Execute(null);
        Assert.True(vm.IsAddingCollection);

        vm.ToggleAddCollectionCommand.Execute(null);
        Assert.False(vm.IsAddingCollection);
        Assert.Equal(string.Empty, vm.CollectionSearchQuery);
    }

    [Fact]
    public void CollectionSearchQuery_NoExistingMatch_OffersCreateNewRow()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.CollectionSearchQuery = "Favorites";

        var result = Assert.Single(vm.CollectionSearchResults);
        Assert.True(result.IsNew);
        Assert.Equal("Favorites", result.Name);
    }

    [Fact]
    public void CollectionSearchQuery_ExistingMatch_DoesNotOfferCreateNewRow()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
        }
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.CollectionSearchQuery = "favorites"; // case-insensitive substring, matches the exact-name check

        var result = Assert.Single(vm.CollectionSearchResults);
        Assert.False(result.IsNew);
    }

    [Fact]
    public void AddCollection_NewName_CreatesCollectionAndMembership_ClosesPanel()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleAddCollectionCommand.Execute(null);
        vm.CollectionSearchQuery = "Favorites";
        var target = Assert.Single(vm.CollectionSearchResults);

        vm.AddCollectionCommand.Execute(target);

        Assert.False(vm.IsAddingCollection);
        var chip = Assert.Single(vm.CollectionChips);
        Assert.Equal("Favorites", chip.Name);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Collections);
    }

    [Fact]
    public void AddCollection_ExistingCollection_SameSeriesSharesItAppearsUnderSameCollection()
    {
        int otherId = AddOtherSeries("Ultimate Spider-Man");
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var collection = Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
            Paperbunkr.Data.Collections.CollectionService.AddItems(context, collection.Id, seriesIds: new[] { otherId });
        }
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleAddCollectionCommand.Execute(null);
        vm.CollectionSearchQuery = "Favorites";
        var target = Assert.Single(vm.CollectionSearchResults);
        Assert.False(target.IsNew);

        vm.AddCollectionCommand.Execute(target);

        var sameCollection = Assert.Single(vm.SameCollection);
        Assert.Equal(otherId, sameCollection.SeriesId);
        Assert.True(vm.HasSameCollection);
        Assert.Single(vm.CollectionRail);
    }

    [Fact]
    public void RemoveCollection_ClearsChipAndSameCollectionSection()
    {
        int otherId = AddOtherSeries("Ultimate Spider-Man");
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var collection = Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
            Paperbunkr.Data.Collections.CollectionService.AddItems(context, collection.Id, seriesIds: new[] { otherId });
        }
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleAddCollectionCommand.Execute(null);
        vm.CollectionSearchQuery = "Favorites";
        vm.AddCollectionCommand.Execute(Assert.Single(vm.CollectionSearchResults));
        var chip = Assert.Single(vm.CollectionChips);

        vm.RemoveCollectionCommand.Execute(chip);

        Assert.Empty(vm.CollectionChips);
        Assert.Empty(vm.SameCollection);
        Assert.False(vm.HasSameCollection);
    }

    // --- Same Event (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-
    // design.md) - read-only derived section. ---

    [Fact]
    public void LoadSeries_NoEventMembership_HasSameEventFalse()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasSameEvent);
        Assert.Empty(vm.SameEvent);
    }

    [Fact]
    public void LoadSeries_SharedEventMembership_ShowsOtherSeriesUnderSameEvent()
    {
        int otherId = AddOtherSeries("Green Lantern Corps");
        int currentIssueId;
        int otherIssueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            currentIssueId = context.Issues.First(i => i.SeriesId == _seriesId).Id;
            var otherIssue = new Issue { SeriesId = otherId, Number = "1" };
            context.Issues.Add(otherIssue);
            context.SaveChanges();
            otherIssueId = otherIssue.Id;

            var storyEvent = new StoryEvent { Name = "Rise of the Third Army", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();

            context.EventMemberships.AddRange(
                new EventMembership { StoryEventId = storyEvent.Id, IssueId = currentIssueId, Position = 0, Role = EventMembershipRole.Prologue },
                new EventMembership { StoryEventId = storyEvent.Id, IssueId = otherIssueId, Position = 1, Role = EventMembershipRole.Core });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        var sameEvent = Assert.Single(vm.SameEvent);
        Assert.Equal(otherId, sameEvent.SeriesId);
        Assert.True(vm.HasSameEvent);
    }

    // ===================== External Metadata (docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-and-link-design.md) =====================

    [Fact]
    public void LoadSeries_NoExternalLinks_ShowsEmptyState()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.HasExternalLinks);
        Assert.Empty(vm.ExternalLinks);
    }

    [Fact]
    public void LoadSeries_ExistingExternalLink_PopulatesExternalLinks()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = _seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "30013", Url = "https://anilist.co/manga/30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        var link = Assert.Single(vm.ExternalLinks);
        Assert.Equal("AniList", link.ProviderLabel);
        Assert.Equal("30013", link.ExternalId);
        Assert.True(vm.HasExternalLinks);
    }

    [Fact]
    public async Task SearchMetadataAsync_PopulatesResultsScoredAgainstSeriesName()
    {
        var provider = new FakeMetadataProvider();
        provider.SearchResults.Add(new MetadataSearchResult("1", "Test Series", "https://example/1"));
        provider.SearchResults.Add(new MetadataSearchResult("2", "Completely Different", "https://example/2"));
        var vm = CreateViewModel(metadataProvider: provider);
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleSearchMetadataCommand.Execute(null);
        vm.MetadataSearchQuery = "test series";

        await vm.SearchMetadataCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.MetadataSearchResults.Count);
        Assert.Equal("1", vm.MetadataSearchResults[0].ExternalId);
        Assert.Equal("Best match", vm.MetadataSearchResults[0].TierLabel);
    }

    [Fact]
    public async Task LinkMetadataAsync_CreatesLinkAndClosesSearch()
    {
        var provider = new FakeMetadataProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "Test Series", "https://anilist.co/manga/30013", null, null, null, null),
        };
        var vm = CreateViewModel(metadataProvider: provider);
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleSearchMetadataCommand.Execute(null);
        var candidate = new Paperbunkr.App.Models.AniListMatchSample { ExternalId = "30013", Title = "Test Series", Confidence = 1.0, Tier = MatchTier.Auto };

        await vm.LinkMetadataCommand.ExecuteAsync(candidate);

        var link = Assert.Single(vm.ExternalLinks);
        Assert.Equal("30013", link.ExternalId);

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var activity = Assert.Single(context.SeriesActivityEvents);
            Assert.Equal(SeriesActivityEventKind.MetadataLinked, activity.Kind);
            Assert.Equal(_seriesId, activity.SeriesId);
        }

        // IsSearchingMetadata's reset is deliberately deferred via Dispatcher.UIThread.Post (see
        // LinkMetadataAsync's own comment) - pump it before asserting, same idiom as
        // ReaderScreenViewModelTests.LoadIssue_GeneratesThumbnailsForEveryPage_NoneLeftNull.
        TestDispatcher.Drain();

        Assert.False(vm.IsSearchingMetadata);
    }

    [Fact]
    public void UnlinkMetadata_RemovesTheLinkButKeepsTheSeries()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = _seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var link = Assert.Single(vm.ExternalLinks);

        vm.UnlinkMetadataCommand.Execute(link);

        Assert.Empty(vm.ExternalLinks);
        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        Assert.NotNull(verifyContext.Series.Find(_seriesId));
        Assert.Empty(verifyContext.ExternalMediaIds.Where(e => e.SeriesId == _seriesId));

        var activity = Assert.Single(verifyContext.SeriesActivityEvents);
        Assert.Equal(SeriesActivityEventKind.MetadataUnlinked, activity.Kind);
        Assert.Equal("AniList", activity.Detail);
    }

    [Fact]
    public void UnlinkMetadata_NothingToRemove_LogsNoActivity()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.UnlinkMetadataCommand.Execute(new Paperbunkr.App.Models.ExternalLinkSample { ProviderLabel = "AniList", ExternalId = "99999" });

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.SeriesActivityEvents);
    }

    [Fact]
    public void UnlinkTracker_RemovesTheLink_LogsActivity()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var link = Assert.Single(vm.TrackerLinks);

        vm.UnlinkTrackerCommand.Execute(link);

        // UnlinkTracker defers TrackerLinks.Clear() via Dispatcher.UIThread.Post (see its own doc
        // comment - the "✕" Button's Click is still routing through a chip in that same
        // ItemsControl), which a headless test never pumps on its own.
        TestDispatcher.Drain();

        Assert.Empty(vm.TrackerLinks);
        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        var activity = Assert.Single(verifyContext.SeriesActivityEvents);
        Assert.Equal(SeriesActivityEventKind.TrackerUnlinked, activity.Kind);
        Assert.Equal("AniList", activity.Detail);
    }

    [Fact]
    public async Task SyncToTrackersAsync_NoConnectedTrackers_LogsNoActivity()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            // Linked but never authorized (no CredentialStore entry) - SyncToTrackersAsync skips it.
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        await vm.SyncToTrackersCommand.ExecuteAsync(null);

        Assert.Equal("No connected trackers linked to this series.", vm.TrackerSyncStatus);
        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(verifyContext.SeriesActivityEvents);
    }

    // --- Expand-on-click per-tracker Score/Finish-date panel (docs/superpowers/specs/2026-09-18-
    // per-tracker-score-and-finish-date-design.md) - every adapter's no-stored-credentials guard
    // returns without a real network call (confirmed by each *TrackerAdapterTests.cs' own
    // "_NoStoredAccessToken_ReturnsFalse/Null_WithoutSendingRequest" test), so these exercise the
    // real ViewModel wiring offline, same "no seam to inject a fake tracker adapter" constraint this
    // file's own SyncToTrackersAsync tests already work within (see that method's doc comment). ---

    [Fact]
    public async Task ToggleTrackerLinkDetailsAsync_SelectsLink_FallsBackToSeriesRatingWhenNoRemoteScore()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            var series = context.Series.Find(_seriesId)!;
            series.Rating = 3.5f;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var link = Assert.Single(vm.TrackerLinks);

        await vm.ToggleTrackerLinkDetailsCommand.ExecuteAsync(link);

        Assert.Same(link, vm.SelectedTrackerLink);
        Assert.Equal(3.5m, link.Score); // no CredentialStore entry -> remote fetch returns null -> falls back to Series.Rating
        Assert.False(link.IsBusy);
    }

    [Fact]
    public async Task ToggleTrackerLinkDetailsAsync_ReclickingSameLink_ClosesPanel()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var link = Assert.Single(vm.TrackerLinks);

        await vm.ToggleTrackerLinkDetailsCommand.ExecuteAsync(link);
        Assert.Same(link, vm.SelectedTrackerLink);

        await vm.ToggleTrackerLinkDetailsCommand.ExecuteAsync(link);

        Assert.Null(vm.SelectedTrackerLink);
    }

    [Fact]
    public async Task PushTrackerFieldAsync_NoStoredCredentials_SetsFailedPushStatus()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var link = Assert.Single(vm.TrackerLinks);
        link.Score = 4.5m;

        await vm.PushTrackerFieldCommand.ExecuteAsync(link);

        Assert.NotNull(link.PushStatus);
        Assert.StartsWith("Failed:", link.PushStatus);
        Assert.False(link.IsBusy);
    }

    [Fact]
    public async Task UseTrackerScoreAsync_NoRemoteScore_SetsInformationalStatus_DoesNotTouchSeriesRating()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            var series = context.Series.Find(_seriesId)!;
            series.Rating = 2f;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var link = Assert.Single(vm.TrackerLinks);

        await vm.UseTrackerScoreCommand.ExecuteAsync(link);

        Assert.Equal("This tracker has no score to use.", link.PushStatus);
        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(2f, verifyContext.Series.Find(_seriesId)!.Rating);
    }

    // --- Tracker behavior hooks (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md) ---

    [Fact]
    public void MarkIssueRead_NotifiesTheAutoSyncService_ButMarkUnreadDoesNot()
    {
        var sync = new RecordingTrackerAutoSync();
        var vm = CreateViewModel(trackerAutoSync: sync);
        vm.LoadSeries(LoadSeriesEntity());

        vm.MarkIssueUnreadCommand.Execute(vm.Issues.First(i => i.Title == "#1"));
        Assert.Empty(sync.MarkedRead);

        vm.MarkIssueReadCommand.Execute(vm.Issues.First(i => i.Title == "#1"));

        Assert.Equal(new[] { _seriesId }, Assert.Single(sync.MarkedRead));
    }

    [Fact]
    public void LoadSeries_AsksTheAutoSyncServiceToPullThatSeries()
    {
        var sync = new RecordingTrackerAutoSync();
        var vm = CreateViewModel(trackerAutoSync: sync);

        vm.LoadSeries(LoadSeriesEntity());

        Assert.Equal(new[] { _seriesId }, sync.Pulled);
    }

    [Fact]
    public void LoadSeries_WhenThePullMarkedIssuesRead_RefreshesTheHostOnTheUiThread()
    {
        var sync = new RecordingTrackerAutoSync();
        int selectionChanged = 0;
        var vm = CreateViewModel(onSelectionChanged: () => selectionChanged++, trackerAutoSync: sync);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            sync.PullResultIds = context.Issues.Where(i => i.SeriesId == _seriesId).Select(i => i.Id).Take(2).ToList();
        }

        vm.LoadSeries(LoadSeriesEntity());
        int afterLoad = selectionChanged;
        TestDispatcher.Drain();

        Assert.True(selectionChanged > afterLoad);
    }

    private void SeedAutoOpenScenario(bool connected = true, bool metadataLink = true, bool alreadyLinked = false)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        if (metadataLink)
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = _seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "30013" });
        }

        if (alreadyLinked)
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
        }

        if (connected)
        {
            Paperbunkr.Data.Credentials.CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "tok");
        }

        context.SaveChanges();
    }

    private bool PromptShown()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Series.Find(_seriesId)!.TrackerPromptShown;
    }

    [Fact]
    public void AutoOpen_MangaHostWithSourceLinkAndConnectedAccount_OpensLinkPanelOnce()
    {
        SeedAutoOpenScenario();
        var vm = CreateViewModel(isMangaHost: true);

        vm.LoadSeries(LoadSeriesEntity());

        Assert.True(vm.IsLinkingTracker);
        Assert.Equal("details", vm.ActiveTab);
        Assert.Equal("linking", vm.ActiveDetailsSubTab);
        Assert.Equal(TrackingService.AniList, vm.SelectedTrackerService);
        Assert.True(PromptShown());

        vm.IsLinkingTracker = false; // user closed it; a later load must not reopen it
        vm.LoadSeries(LoadSeriesEntity());
        Assert.False(vm.IsLinkingTracker);
    }

    [Fact]
    public void AutoOpen_NeverForANonMangaHost()
    {
        SeedAutoOpenScenario();
        var vm = CreateViewModel(isMangaHost: false);

        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.IsLinkingTracker);
        Assert.False(PromptShown());
    }

    [Theory]
    [InlineData(false, true, false, true)]   // no connected account
    [InlineData(true, false, false, true)]   // no metadata source link
    [InlineData(true, true, true, true)]     // already linked to that service
    [InlineData(true, true, false, false)]   // setting off
    public void AutoOpen_RequiresBothConditions_NotAlreadyLinked_AndTheSettingOn(bool connected, bool metadataLink, bool alreadyLinked, bool settingOn)
    {
        SeedAutoOpenScenario(connected, metadataLink, alreadyLinked);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().TrackerAutoOpenLinkPanel = settingOn;
            context.SaveChanges();
        }

        var vm = CreateViewModel(isMangaHost: true);
        vm.LoadSeries(LoadSeriesEntity());

        Assert.False(vm.IsLinkingTracker);
        Assert.False(PromptShown()); // a skipped open must never burn the one-shot flag
    }

    [Fact]
    public void Pinning_LinkedMetadataSourceAppearsFirst_WithoutNetwork_AndStillNeedsConfirm()
    {
        SeedAutoOpenScenario(connected: false);
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleLinkTrackerCommand.Execute(null);

        var pinned = Assert.Single(vm.TrackerSearchResults);
        Assert.True(pinned.IsFromLinkedMetadata);
        Assert.Equal("30013", pinned.ExternalId);
        Assert.Equal("From linked metadata", pinned.TierLabel);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.TrackingLinks); // pinned, not linked - the two-step confirm still gates the write
    }

    [Fact]
    public void Pinning_SettingOff_OrAlreadyLinked_PinsNothing()
    {
        SeedAutoOpenScenario(connected: false);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().TrackerUseSourceMetadata = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleLinkTrackerCommand.Execute(null);
        Assert.Empty(vm.TrackerSearchResults);

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().TrackerUseSourceMetadata = true;
            context.TrackingLinks.Add(new TrackingLink { SeriesId = _seriesId, Service = TrackingService.AniList, ExternalId = "30013" });
            context.SaveChanges();
        }

        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleLinkTrackerCommand.Execute(null);
        Assert.Empty(vm.TrackerSearchResults);
    }

    // --- SuggestBox string projections (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md) ---

    [Fact]
    public void SelectedRelationTypeOptionText_RoundTripsThroughTheObjectProperty()
    {
        var vm = CreateViewModel();

        vm.SelectedRelationTypeOptionText = "Sequel";

        Assert.Equal(RelationType.Sequel, vm.SelectedRelationType);
        Assert.Equal("Sequel", vm.SelectedRelationTypeOptionText);
        Assert.Contains("Sequel", DetailTabsViewModel.RelationTypeNames);
    }

    [Fact]
    public void SelectedRelationTypeOptionText_IgnoresTextThatIsNotAnOption()
    {
        var vm = CreateViewModel();
        var before = vm.SelectedRelationTypeOption;

        vm.SelectedRelationTypeOptionText = "Not A Relation";

        Assert.Equal(before, vm.SelectedRelationTypeOption);
    }

    [Fact]
    public void SelectedTrackerServiceText_OnlyAcceptsTheCuratedSubset()
    {
        var vm = CreateViewModel();

        vm.SelectedTrackerServiceText = "MyAnimeList";
        Assert.Equal(TrackingService.MyAnimeList, vm.SelectedTrackerService);

        vm.SelectedTrackerServiceText = "SomethingElse";
        Assert.Equal(TrackingService.MyAnimeList, vm.SelectedTrackerService);

        Assert.Equal(DetailTabsViewModel.TrackerServiceOptions.Select(s => s.ToString()), DetailTabsViewModel.TrackerServiceNames);
    }

    [Fact]
    public void SelectedMetadataProviderText_RoundTripsAndTracksTheOptionList()
    {
        var vm = CreateViewModel();

        Assert.Equal(vm.MetadataProviderOptions.Select(o => o.Label), vm.MetadataProviderNames);

        var target = vm.MetadataProviderOptions.First(o => o.Label != vm.SelectedMetadataProvider.Label);
        vm.SelectedMetadataProviderText = target.Label;

        Assert.Equal(target.Label, vm.SelectedMetadataProvider.Label);
    }

    // --- Details tab: Publisher fallback + full credits + additional fields
    // (docs/superpowers/specs/2026-09-13-details-tab-credits-and-fields-design.md) ---

    [Fact]
    public void LoadSeries_PublisherBlankOnSeries_FallsBackToIssuePublisher()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Publisher = null;
        series.Issues[0].Publisher = "DC Comics";

        vm.LoadSeries(series);

        Assert.Equal("DC Comics", vm.Publisher);
    }

    [Fact]
    public void LoadSeries_PublisherSetOnSeries_PreferredOverIssuePublisher()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Publisher = "Marvel";
        series.Issues[0].Publisher = "DC Comics";

        vm.LoadSeries(series);

        Assert.Equal("Marvel", vm.Publisher);
    }

    [Fact]
    public void LoadSeries_CreditRoles_AggregatesDistinctValuesAcrossIssues()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Issues[0].Writer = "Alice";
        series.Issues[1].Writer = "Bob";
        series.Issues[2].Writer = "alice"; // dupe, different case

        vm.LoadSeries(series);

        var writerGroup = Assert.Single(vm.CreditRoles, g => g.Label == "Writer");
        Assert.Equal(new[] { "Alice", "Bob" }, writerGroup.Chips.Select(c => c.Value));
    }

    [Fact]
    public void LoadSeries_CreditRoles_RoleWithNoValues_NotAdded()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        // No issue has any credit fields set.

        vm.LoadSeries(series);

        Assert.False(vm.HasCreditRoles);
        Assert.Empty(vm.CreditRoles);
    }

    [Fact]
    public void LoadSeries_CreditRoles_OnlyPopulatedRolesAdded()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Issues[0].Inker = "Carl";

        vm.LoadSeries(series);

        Assert.Single(vm.CreditRoles);
        Assert.Equal("Inker", vm.CreditRoles[0].Label);
    }

    [Fact]
    public void LoadSeries_CreditChip_ClickInvokesGoLibraryWithSearch()
    {
        string? searched = null;
        var vm = CreateViewModel(goLibraryWithSearch: v => searched = v);
        var series = LoadSeriesEntity();
        series.Issues[0].Writer = "Alice";

        vm.LoadSeries(series);
        var chip = vm.CreditRoles.Single(g => g.Label == "Writer").Chips.Single();
        chip.SearchCommand.Execute(null);

        Assert.Equal("Alice", searched);
    }

    [Fact]
    public void LoadSeries_AdditionalDetails_FieldBlankEverywhere_NotAdded()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();

        vm.LoadSeries(series);

        Assert.False(vm.HasAdditionalDetails);
        Assert.Empty(vm.AdditionalDetails);
    }

    [Fact]
    public void LoadSeries_AdditionalDetails_SingleValue_ShownAsIs()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Issues[0].Imprint = "Vertigo";

        vm.LoadSeries(series);

        var row = Assert.Single(vm.AdditionalDetails, r => r.Label == "Imprint");
        Assert.Equal("Vertigo", row.Value);
    }

    [Fact]
    public void LoadSeries_AdditionalDetails_StoryArcNumber_ReadDirectlyFromIssue()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Issues[0].StoryArcNumber = "3";

        vm.LoadSeries(series);

        var row = Assert.Single(vm.AdditionalDetails, r => r.Label == "Story Arc Number");
        Assert.Equal("3", row.Value);
    }

    [Fact]
    public void LoadSeries_WebField_SingleDistinctValue_IsLinkTrue()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Issues[0].Web = "https://example.com/issue1";
        series.Issues[1].Web = "https://example.com/issue1";

        vm.LoadSeries(series);

        var row = Assert.Single(vm.AdditionalDetails, r => r.Label == "Web");
        Assert.True(row.IsLink);
        Assert.Equal("https://example.com/issue1", row.Value);
    }

    [Fact]
    public void LoadSeries_WebField_MultipleDistinctValues_IsLinkFalse_ValueJoined()
    {
        var vm = CreateViewModel();
        var series = LoadSeriesEntity();
        series.Issues[0].Web = "https://example.com/a";
        series.Issues[1].Web = "https://example.com/b";

        vm.LoadSeries(series);

        var row = Assert.Single(vm.AdditionalDetails, r => r.Label == "Web");
        Assert.False(row.IsLink);
        Assert.Equal("https://example.com/a, https://example.com/b", row.Value);
    }
}

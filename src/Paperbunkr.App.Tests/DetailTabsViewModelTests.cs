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

    private DetailTabsViewModel CreateViewModel(Action<int>? goToProperties = null, Action<IReadOnlyList<int>>? goToBulkProperties = null, Action? onSelectionChanged = null, IMetadataProvider? metadataProvider = null, Action<int>? navigateToCollection = null, Action<int>? openInReader = null) =>
        new(goToProperties ?? (_ => { }), goToBulkProperties ?? (_ => { }), onSelectionChanged, () => new PaperbunkrDbContext(_dbOptions), metadataProvider, onQuickRate: null, navigateToSeries: null, openInReader: openInReader, navigateToCollection: navigateToCollection);

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
    public void LoadSeries_PopulatesReadingModeLabel()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.Equal("Left to Right", vm.ReadingModeLabel);
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
    }
}

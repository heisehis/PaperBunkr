using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// View-model behaviour behind the Library preview panel v2 (docs/superpowers/specs/2026-09-26-library-preview-panel-v2-design.md):
/// state gating with the rail drill-in, exact-id mark-read actions, the multi-select strip, and the persisted collapsed sections.
/// The item controls themselves cannot be rendered here (the headless app loads no theme), so the visual result is checked on screen.
/// Same temp-database pattern as <see cref="LibraryScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryPreviewPanelViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryPreviewPanelViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_preview_panel_test_{Guid.NewGuid():N}.db");
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

    private static int CreateSeries(string name, int issueCount)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = ContentType.Comic };
        context.Series.Add(series);
        context.SaveChanges();
        for (int i = 1; i <= issueCount; i++)
        {
            // PageCount matters: marking an issue read is a no-op until its page count is known (IssueReadStateResolver.MarkAsRead).
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = i.ToString(), PageCount = 20 });
        }

        context.SaveChanges();
        return series.Id;
    }

    private static LibraryScreenViewModel NewViewModel() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

    [Fact]
    public void Drill_swaps_series_state_for_issue_state_and_back()
    {
        CreateSeries("Silk", 3);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.PreviewSeries = vm.Covers.Single();

        Assert.True(vm.ShowSeriesPreview);
        Assert.False(vm.ShowIssuePreview);

        var row = vm.IssueList.Rows.First();
        vm.DrillIntoIssueCommand.Execute(row);

        Assert.False(vm.ShowSeriesPreview);
        Assert.True(vm.ShowIssuePreview);
        Assert.True(vm.HasPreviewDrill);
        Assert.Same(row, vm.ActivePreviewIssue);
        Assert.Equal("← Silk", vm.PreviewBackLabel);

        vm.ClearPreviewDrillCommand.Execute(null);

        Assert.True(vm.ShowSeriesPreview);
        Assert.False(vm.ShowIssuePreview);
        Assert.False(vm.HasPreviewDrill);
    }

    [Fact]
    public void A_grid_focus_change_clears_the_drill()
    {
        CreateSeries("Silk", 2);
        CreateSeries("Saga", 2);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.PreviewSeries = vm.Covers[0];
        vm.DrillIntoIssueCommand.Execute(vm.IssueList.Rows.First());
        Assert.True(vm.HasPreviewDrill);

        vm.PreviewSeries = vm.Covers[1];

        Assert.False(vm.HasPreviewDrill);
        Assert.True(vm.ShowSeriesPreview);
    }

    [Fact]
    public void Issue_granularity_previews_the_focused_issue_without_a_drill()
    {
        CreateSeries("Silk", 2);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Issue;
        var row = vm.IssueList.Rows.First();
        vm.PreviewIssue = row;

        Assert.True(vm.ShowIssuePreview);
        Assert.False(vm.ShowSeriesPreview);
        Assert.Same(row, vm.ActivePreviewIssue);
        Assert.False(vm.HasPreviewDrill);
    }

    [Fact]
    public void Rail_items_carry_the_cover_key_and_read_state()
    {
        CreateSeries("Silk", 3);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.PreviewSeries = vm.Covers.Single();

        Assert.Equal(3, vm.PreviewSeriesIssueRail.Count);
        foreach (var item in vm.PreviewSeriesIssueRail)
        {
            var row = Assert.IsType<IssueListRow>(item.Payload);
            Assert.Equal(row.CoverKey, item.CoverKey);
            Assert.False(item.IsRead);
        }
    }

    [Fact]
    public void Rail_lists_every_issue_of_the_series_even_when_a_filter_hides_some_rows()
    {
        CreateSeries("Silk", 3);
        var vm = NewViewModel();
        vm.MarkPreviewIssueReadCommand.Execute(vm.IssueList.Rows.First(r => r.Number == "1").Id);

        vm.FilterUnreadOnly = true;
        Assert.Equal(2, vm.IssueList.Rows.Count); // the filter hides the read issue from the grid's rows

        vm.Granularity = LibraryContentGranularity.Series;
        vm.PreviewSeries = vm.Covers.Single();

        Assert.Equal(3, vm.PreviewSeriesIssueRail.Count); // ...but the panel still shows the whole series
        Assert.Contains(vm.PreviewSeriesIssueRail, i => i.IsRead);
    }

    [Fact]
    public void MarkPreviewIssueRead_marks_only_that_issue_even_with_an_unrelated_multi_selection()
    {
        CreateSeries("Silk", 1);
        CreateSeries("Saga", 2);
        var vm = NewViewModel();
        var silk = vm.IssueList.Rows.Single(r => r.SeriesName == "Silk");
        var sagaRows = vm.IssueList.Rows.Where(r => r.SeriesName == "Saga").ToList();

        vm.ToggleIssueSelection(sagaRows[0], false);
        vm.ToggleIssueSelection(sagaRows[1], false);

        vm.MarkPreviewIssueReadCommand.Execute(silk.Id);

        Assert.True(vm.IssueList.Rows.Single(r => r.Id == silk.Id).IsRead);
        Assert.All(vm.IssueList.Rows.Where(r => r.SeriesName == "Saga"), r => Assert.False(r.IsRead));
    }

    [Fact]
    public void MarkPreviewSeriesRead_marks_every_issue_of_the_series_and_no_others()
    {
        CreateSeries("Silk", 3);
        CreateSeries("Saga", 2);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.PreviewSeries = vm.Covers.Single(c => c.Name == "Silk");

        vm.MarkPreviewSeriesReadCommand.Execute(null);

        Assert.All(vm.IssueList.Rows.Where(r => r.SeriesName == "Silk"), r => Assert.True(r.IsRead));
        Assert.All(vm.IssueList.Rows.Where(r => r.SeriesName == "Saga"), r => Assert.False(r.IsRead));
        Assert.True(vm.PreviewSeries!.IsFinished);
    }

    [Fact]
    public void The_previewed_issue_is_re_pointed_at_the_reloaded_row_after_an_action()
    {
        CreateSeries("Silk", 2);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Issue;
        var original = vm.IssueList.Rows.First();
        vm.PreviewIssue = original;

        vm.MarkPreviewIssueReadCommand.Execute(original.Id);

        Assert.NotSame(original, vm.PreviewIssue);
        Assert.Equal(original.Id, vm.PreviewIssue!.Id);
        Assert.True(vm.PreviewIssue.IsRead);
    }

    [Fact]
    public void A_reload_does_not_count_as_a_focus_change_for_the_drill()
    {
        CreateSeries("Silk", 2);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.PreviewSeries = vm.Covers.Single();
        var row = vm.IssueList.Rows.First();
        vm.DrillIntoIssueCommand.Execute(row);

        vm.MarkPreviewIssueReadCommand.Execute(row.Id);

        Assert.True(vm.HasPreviewDrill);
        Assert.Equal(row.Id, vm.ActivePreviewIssue!.Id);
        Assert.True(vm.ActivePreviewIssue.IsRead);
    }

    [Fact]
    public void Selection_strip_shows_only_for_more_than_one_selected()
    {
        CreateSeries("Silk", 3);
        var vm = NewViewModel();
        vm.Granularity = LibraryContentGranularity.Issue;
        var rows = vm.IssueList.Rows.ToList();

        Assert.False(vm.ShowPreviewSelectionStrip);
        vm.ToggleIssueSelection(rows[0], false);
        Assert.False(vm.ShowPreviewSelectionStrip);
        vm.ToggleIssueSelection(rows[1], false);

        Assert.True(vm.ShowPreviewSelectionStrip);
        Assert.Equal("2 selected · showing focused", vm.PreviewSelectionStripText);
    }

    [Fact]
    public void Sections_default_to_credits_open_and_the_rest_collapsed_and_persist()
    {
        CreateSeries("Silk", 1);
        var vm = NewViewModel();

        Assert.True(vm.IsPreviewCreditsOpen);
        Assert.False(vm.IsPreviewStoryOpen);
        Assert.False(vm.IsPreviewFileOpen);
        Assert.False(vm.IsPreviewDetailsOpen);
        Assert.True(vm.IsPreviewStoryCollapsed);

        vm.TogglePreviewSectionCommand.Execute("story");
        vm.TogglePreviewSectionCommand.Execute("credits");

        Assert.True(vm.IsPreviewStoryOpen);
        Assert.False(vm.IsPreviewCreditsOpen);

        var reopened = NewViewModel();
        Assert.True(reopened.IsPreviewStoryOpen);
        Assert.False(reopened.IsPreviewCreditsOpen);
        Assert.False(reopened.IsPreviewFileOpen);
    }

    [Theory]
    [InlineData(LibraryViewMode.PosterGrid, LibraryGridCoverFit.Poster, true, true, false)]
    [InlineData(LibraryViewMode.PosterGrid, LibraryGridCoverFit.Panorama, true, true, false)]
    [InlineData(LibraryViewMode.PosterGrid, LibraryGridCoverFit.Tiles, false, true, true)]
    [InlineData(LibraryViewMode.List, LibraryGridCoverFit.Poster, false, true, true)]
    [InlineData(LibraryViewMode.DetailsTable, LibraryGridCoverFit.Poster, false, false, false)]
    public void View_and_sort_toggles_are_scoped_to_the_view_modes_that_honour_them(
        LibraryViewMode mode, LibraryGridCoverFit fit, bool coverOverlay, bool overlayBadges, bool languageBadge)
    {
        CreateSeries("Silk", 1);
        var vm = NewViewModel();
        vm.ViewMode = mode;
        vm.GridCoverFit = fit;

        Assert.Equal(coverOverlay, vm.IsCoverOverlayScope);
        Assert.Equal(coverOverlay, vm.IsGridDogEarScope);
        Assert.Equal(overlayBadges, vm.IsOverlayBadgeScope);
        Assert.Equal(languageBadge, vm.IsLanguageBadgeScope);
    }

    [Fact]
    public void Continue_reading_toggle_shows_only_for_series_in_templates_that_have_the_button()
    {
        CreateSeries("Silk", 1);
        var vm = NewViewModel();
        vm.ViewMode = LibraryViewMode.PosterGrid;
        vm.GridCoverFit = LibraryGridCoverFit.Poster;

        vm.Granularity = LibraryContentGranularity.Issue;
        Assert.False(vm.IsContinueReadingScope);

        vm.Granularity = LibraryContentGranularity.Series;
        Assert.True(vm.IsContinueReadingScope);

        vm.ViewMode = LibraryViewMode.List;
        Assert.False(vm.IsContinueReadingScope);
    }

    [Fact]
    public void Poster_card_height_covers_the_cover_its_ring_gutter_and_the_pinned_title_row()
    {
        CreateSeries("Silk", 1);
        var vm = NewViewModel();
        vm.ViewMode = LibraryViewMode.PosterGrid;
        vm.GridCoverFit = LibraryGridCoverFit.Poster;
        vm.ShowTileTitles = true;
        vm.GridDensity = 1.0;

        // Cover + Border.posterCover's Margin (10 top + 5 bottom) + title row (8 px margin + pinned StackPanel height).
        Assert.Equal(vm.PosterCoverHeight + 15 + 8 + vm.PosterTitleTextHeight, vm.PosterCardHeight);

        vm.ShowTileTitles = false;
        Assert.Equal(vm.PosterCoverHeight + 15, vm.PosterCardHeight);
    }
}

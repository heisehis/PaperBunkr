using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Detail layouts v2 (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md): the Library's field catalog on comic and manga
/// detail screens, three caption lines, card elements, sort and group, header drags, and the opt-in per-series layout.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailLayoutsV2Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _seriesId;

    public DetailLayoutsV2Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detaillayouts_v2_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        context.Issues.AddRange(
            new Issue { SeriesId = series.Id, Number = "1", Title = "Alpha", Year = 2001, StoryArc = "Origins" },
            new Issue { SeriesId = series.Id, Number = "2", Title = "Bravo", Year = 2002, StoryArc = "Origins" },
            new Issue { SeriesId = series.Id, Number = "3", Title = "Charlie", Year = 2002 });
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

    private ListLayoutService Service() => new(() => new PaperbunkrDbContext(_dbOptions));

    private Series LoadSeries()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Series.Include(s => s.Issues).First(s => s.Id == _seriesId);
    }

    private DetailTabsViewModel CreateComicScreen()
    {
        var vm = new DetailTabsViewModel(_ => { }, _ => { }, null, () => new PaperbunkrDbContext(_dbOptions));
        vm.InitializeListLayouts(Service());
        vm.LoadSeries(LoadSeries());
        return vm;
    }

    // --- JSON: v1 upgrade and normalization (Q13) ---

    [Fact]
    public void V1Json_UpgradesPosterCardColumnsAndChapterSort_WithoutLosingAnything()
    {
        const string v1 = """
            {"ViewMode":"Card","PosterFields":["Number","ReadStatus"],
             "ComicColumns":[{"Field":"FullTitle","Visible":true,"Width":200},{"Field":"CoverDate","Visible":false,"Width":90}],
             "CardFields":["FullTitle"],"ChapterSortField":"Date","ChapterSortDirection":"Descending"}
            """;

        var comic = DetailListLayoutStateJson.Deserialize(v1, manga: false);
        Assert.Equal(DetailIssueViewMode.Card, comic.ViewMode);
        Assert.Equal(new[] { IssueListSortField.Number }, comic.CaptionFields);
        Assert.True(comic.ShowReadOverlay);
        Assert.False(comic.ShowEditionOverlay);
        Assert.Equal(new[] { "Title", "Released" }, comic.ComicColumns!.Select(c => c.Field));
        Assert.Equal(new[] { TileTextElement.Title }, comic.CardElements);
        Assert.False(comic.ShowCardStatus);

        var manga = DetailListLayoutStateJson.Deserialize(v1, manga: true);
        Assert.Equal(IssueListSortField.Released, manga.ChapterSortBy);
        Assert.Equal(SortDirection.Descending, manga.ChapterSortDirection);

        string saved = DetailListLayoutStateJson.Serialize(comic);
        Assert.DoesNotContain("PosterFields", saved);
        Assert.DoesNotContain("CardFields", saved);
    }

    [Fact]
    public void Normalize_DropsUnknownAndWrongTypeColumns_AndCapsCaptionsAtThree()
    {
        var state = DetailListLayoutState.ComicDefault with
        {
            ComicColumns = new[]
            {
                new DetailListColumn("Writer", true, 5000),
                new DetailListColumn("NoSuchField"),
                new DetailListColumn(DetailColumnKeys.Bookmark), // a manga-only key on a comic layout
                new DetailListColumn("Writer"),
            },
            CaptionFields = new[] { IssueListSortField.Number, IssueListSortField.Writer, IssueListSortField.Year, IssueListSortField.Rating },
        };

        var normalized = DetailListLayoutStateJson.Normalize(state);

        var column = Assert.Single(normalized.ComicColumns!);
        Assert.Equal("Writer", column.Field);
        Assert.Equal(DetailListLayoutStateJson.MaxColumnWidth, column.Width);
        Assert.Equal(3, normalized.CaptionFields!.Count);
    }

    [Fact]
    public void BuiltInDefault_LooksAsBefore_NumberBoldThenStoryArc()
    {
        var row = LoadSeries().Issues.OrderBy(i => i.Number).Select(i => IssueListRow.FromIssue(i, LoadSeries())).First();

        Assert.Equal(new[] { "#1", "Origins" }, DetailLayoutRendering.CaptionLines(DetailListLayoutState.ComicDefault, row));
        Assert.Empty(DetailLayoutRendering.CaptionLines(DetailListLayoutState.ComicDefault with { HideCaptions = true }, row));
    }

    [Fact]
    public void CardText_UsesTitleAsHeadline_AndJoinsTheRest()
    {
        var series = LoadSeries();
        var row = IssueListRow.FromIssue(series.Issues.First(i => i.Number == "2"), series);
        var state = DetailListLayoutState.ComicDefault with { CardElements = new[] { TileTextElement.Number, TileTextElement.Title, TileTextElement.Year } };

        var (headline, secondLine) = DetailLayoutRendering.CardText(state, row, inProgress: false);

        Assert.Equal("Bravo", headline);
        Assert.Equal("#2 · 2002", secondLine);
    }

    [Fact]
    public void ColumnMove_FollowsTheLibraryRule_InFrontWhenLeft_BehindWhenRight()
    {
        var columns = new[] { new DetailListColumn("A"), new DetailListColumn("B"), new DetailListColumn("C") };

        Assert.Equal("CAB", string.Concat(DetailColumnMoves.Move(columns, "C", "A").Select(c => c.Field)));
        Assert.Equal("BCA", string.Concat(DetailColumnMoves.Move(columns, "A", "C").Select(c => c.Field)));
        Assert.Equal("ABC", string.Concat(DetailColumnMoves.Move(columns, "A", "Missing").Select(c => c.Field)));
    }

    // --- The comic screen (Q5/Q6/Q12) ---

    [Fact]
    public void ComicScreen_SortsDescending_AndGroupsByALibraryField()
    {
        var vm = CreateComicScreen();
        Assert.Equal(new[] { "#1", "#2", "#3" }, vm.Issues.Select(c => c.Title));

        vm.ApplyLayoutJson(DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with
        {
            SortDirection = SortDirection.Descending,
            GroupBy = DetailGrouping.ForField(IssueListGroupField.Year),
        }));

        Assert.Equal(2, vm.IssueGroups.Count);
        Assert.Equal(new[] { "2001", "2002" }, vm.IssueGroups.Select(g => g.Header));
        Assert.Equal(new[] { "#3", "#2" }, vm.IssueGroups[1].Items.Select(c => c.Title));
    }

    [Fact]
    public void ComicScreen_HeaderDrag_ResizesAndMovesColumns_AndRemembersThem()
    {
        var vm = CreateComicScreen();

        vm.SetIssueColumnWidth("Title", 300);
        vm.MoveIssueColumn("Title", DetailColumnKeys.ReadStatus);

        Assert.Equal("Title", vm.IssueHeaderCells[0].Field);
        Assert.Equal(300, vm.IssueHeaderCells[0].Width);
        var saved = DetailListLayoutStateJson.Deserialize(Service().GetAssignment(WorkspaceScreen.ComicDetail, ListLayoutAssignment.DefaultKey), manga: false);
        Assert.Equal("Title", saved.ComicColumns![0].Field);
    }

    // --- Per-series override (Q8/Q11) ---

    [Fact]
    public void SeriesOwnLayout_ShowsOnlyForThatSeries_AndFollowDefaultDropsIt()
    {
        var vm = CreateComicScreen();
        var cardLayout = Service().SaveNamed(WorkspaceScreen.ComicDetail, "Cards",
            DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with { ViewMode = DetailIssueViewMode.Card }));
        vm.ListLayouts.RefreshLayouts();

        vm.ListLayouts.UseForSeries(_seriesId, cardLayout.Id);

        Assert.True(vm.IsCardView);
        Assert.Equal(cardLayout.Id, vm.ListLayouts.SeriesLayoutId(_seriesId));
        Assert.Equal(ListLayoutsViewModel.SeriesKey(_seriesId), vm.LayoutSelectionKey);
        var defaultState = DetailListLayoutStateJson.Deserialize(Service().GetAssignment(WorkspaceScreen.ComicDetail, ListLayoutAssignment.DefaultKey), manga: false);
        Assert.Equal(DetailIssueViewMode.Poster, defaultState.ViewMode);

        vm.ListLayouts.FollowDefault(_seriesId);

        Assert.True(vm.IsPosterView);
        Assert.Equal(ListLayoutAssignment.DefaultKey, vm.LayoutSelectionKey);
    }

    [Fact]
    public void SetOnAllSeries_ReplacesTheDefault_AndClearsSeriesOverrides()
    {
        var vm = CreateComicScreen();
        var service = Service();
        var list = service.SaveNamed(WorkspaceScreen.ComicDetail, "List",
            DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with { ViewMode = DetailIssueViewMode.List }));
        var cards = service.SaveNamed(WorkspaceScreen.ComicDetail, "Cards",
            DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with { ViewMode = DetailIssueViewMode.Card }));
        vm.ListLayouts.RefreshLayouts();
        vm.ListLayouts.UseForSeries(_seriesId, cards.Id);

        vm.ListLayouts.SetLayoutOnAllListsCommand.Execute(list.Id);

        Assert.Null(service.GetAssignment(WorkspaceScreen.ComicDetail, ListLayoutsViewModel.SeriesKey(_seriesId)));
        Assert.True(vm.IsListView);
    }

    // --- List Options draft (Q1-Q6) ---

    [Fact]
    public void ListOptions_RoundTripsTheDefault_AndOffersEveryLibraryColumn()
    {
        var options = DetailListOptionsViewModel.ForComic(DetailListLayoutState.ComicDefault, Array.Empty<VirtualTagDefinition>(), _ => { }, () => { });

        Assert.Equal(DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault), DetailListLayoutStateJson.Serialize(options.BuildState()));
        Assert.Equal(DetailColumnKeys.CatalogColumns.Count + DetailColumnKeys.ComicOnly.Count, options.ComicColumns.Count);
    }

    [Fact]
    public void ListOptions_SetsCaptionsSortAndGroup()
    {
        var options = DetailListOptionsViewModel.ForComic(DetailListLayoutState.ComicDefault, Array.Empty<VirtualTagDefinition>(), _ => { }, () => { });
        options.Caption1 = "Writer";
        options.Caption2 = DetailListOptionsViewModel.NoCaption;
        options.SortBy = "Year";
        options.SortDescending = true;
        options.GroupBy = "No grouping";

        var state = options.BuildState();

        Assert.Equal(new[] { IssueListSortField.Writer }, state.CaptionFields);
        Assert.Equal(IssueListSortField.Year, state.SortField);
        Assert.Equal(SortDirection.Descending, state.SortDirection);
        Assert.Equal(DetailGrouping.None, state.GroupBy);
    }

    [Fact]
    public void StarterLayouts_SerializeAndReadBackUnchanged()
    {
        foreach (var (name, state) in ListLayoutTemplates.ComicDetail)
        {
            string json = DetailListLayoutStateJson.Serialize(state);
            Assert.Equal(json, DetailListLayoutStateJson.Serialize(DetailListLayoutStateJson.Deserialize(json, manga: false)));
        }

        foreach (var (name, state) in ListLayoutTemplates.MangaDetail)
        {
            string json = DetailListLayoutStateJson.Serialize(state);
            Assert.Equal(json, DetailListLayoutStateJson.Serialize(DetailListLayoutStateJson.Deserialize(json, manga: true)));
        }
    }
}

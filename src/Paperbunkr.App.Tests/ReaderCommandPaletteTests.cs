using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>Reader command palette: page-number parsing and ranking (pure) and the palette state machine (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 5).</summary>
public class ReaderCommandPaletteTests
{
    private static ReaderPaletteEntry Entry(string title, string group = "View", Action? run = null) => new(title, group, null, run ?? (() => { }));

    // --- Page number parsing ---

    [Theory]
    [InlineData("40", 40)]
    [InlineData(" 7 ", 7)]
    [InlineData("page 40", 40)]
    [InlineData("Page40", 40)]
    [InlineData("p40", 40)]
    [InlineData("p 40", 40)]
    [InlineData("pg 3", 3)]
    [InlineData("#12", 12)]
    [InlineData("go to 5", 5)]
    [InlineData("goto page 9", 9)]
    [InlineData("0", 0)]
    public void TryParsePageNumber_AcceptsTheCommonSpellings(string query, int expected)
    {
        Assert.True(ReaderPaletteCatalog.TryParsePageNumber(query, out int page));
        Assert.Equal(expected, page);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fullscreen")]
    [InlineData("40 pages")]
    [InlineData("page")]
    [InlineData("fit 3")]
    [InlineData("-4")]
    public void TryParsePageNumber_RejectsEverythingElse(string query) =>
        Assert.False(ReaderPaletteCatalog.TryParsePageNumber(query, out _));

    // --- Ranking ---

    [Fact]
    public void Rank_EmptyQuery_KeepsTheCatalogOrder()
    {
        var entries = new[] { Entry("Zeta"), Entry("Alpha"), Entry("Mid") };

        Assert.Equal(["Zeta", "Alpha", "Mid"], ReaderPaletteCatalog.Rank(entries, "").Select(e => e.Title));
    }

    [Fact]
    public void Rank_FiltersToSubsequenceMatches_BestFirst()
    {
        var entries = new[]
        {
            Entry("Toggle fullscreen"),
            Entry("Fit: Fit width"),
            Entry("Zoom in"),
        };

        var ranked = ReaderPaletteCatalog.Rank(entries, "fit").Select(e => e.Title).ToList();

        Assert.Equal("Fit: Fit width", ranked[0]);
        Assert.DoesNotContain("Zoom in", ranked);
    }

    [Fact]
    public void Rank_MatchesOnTheGroupToo()
    {
        var entries = new[] { Entry("Toggle fullscreen", "View"), Entry("Next page", "Navigate") };

        Assert.Equal(["Toggle fullscreen"], ReaderPaletteCatalog.Rank(entries, "view full").Select(e => e.Title));
    }

    [Fact]
    public void Rank_NoMatch_IsEmpty() =>
        Assert.Empty(ReaderPaletteCatalog.Rank([Entry("Zoom in")], "qqq"));

    [Fact]
    public void Rank_CapsTheResultCount()
    {
        var entries = Enumerable.Range(0, 40).Select(i => Entry($"Command {i}")).ToList();

        Assert.Equal(ReaderPaletteCatalog.MaxResults, ReaderPaletteCatalog.Rank(entries, "command").Count);
        Assert.Equal(ReaderPaletteCatalog.MaxUntyped, ReaderPaletteCatalog.Rank(entries, "").Count);
    }

    // --- The palette state machine ---

    private static ReaderCommandPaletteViewModel Palette(IReadOnlyList<ReaderPaletteEntry> entries, int pageCount, Action<int>? goTo = null, List<Action>? posted = null) =>
        new(() => entries, () => pageCount, goTo ?? (_ => { }), posted is null ? a => a() : posted.Add);

    [Fact]
    public void Open_ListsTheCatalog_WithTheFirstRowSelected()
    {
        var palette = Palette([Entry("A"), Entry("B")], 10);

        palette.Open();

        Assert.True(palette.IsOpen);
        Assert.False(palette.IsGoToPageMode);
        Assert.Equal(["A", "B"], palette.Results.Select(r => r.Title));
        Assert.Equal(0, palette.SelectedIndex);
        Assert.True(palette.Results[0].IsSelected);
        Assert.False(palette.Results[1].IsSelected);
    }

    [Fact]
    public void TypingAPageNumber_AddsAGoToPageRowFirst()
    {
        var palette = Palette([Entry("Zoom in")], 120);
        palette.Open();

        palette.Query = "40";

        Assert.Equal("Go to page 40 of 120", palette.Results[0].Title);
    }

    [Fact]
    public void GoToPageRow_ClampsAPageBeyondTheEnd_AndRunsWithAZeroBasedIndex()
    {
        int? target = null;
        var palette = Palette([], 12, goTo: i => target = i);
        palette.Open();

        palette.Query = "99";
        palette.ExecuteSelectedCommand.Execute(null);

        Assert.Equal("Go to page 12 of 12", palette.Results[0].Title);
        Assert.Equal(11, target);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void GoToPageMode_OffersOnlyThePageRow()
    {
        var palette = Palette([Entry("Zoom in")], 30);

        palette.OpenGoToPage();
        Assert.True(palette.IsGoToPageMode);
        Assert.Empty(palette.Results);

        palette.Query = "p7";
        Assert.Equal(["Go to page 7 of 30"], palette.Results.Select(r => r.Title));

        palette.Query = "zoom";
        Assert.Empty(palette.Results);
    }

    [Fact]
    public void Execute_ClosesFirst_ThenRunsTheEntryOneTickLater()
    {
        int runs = 0;
        var posted = new List<Action>();
        var palette = Palette([Entry("Do it", run: () => runs++)], 5, posted: posted);
        palette.Open();

        palette.ExecuteSelectedCommand.Execute(null);

        Assert.False(palette.IsOpen);
        Assert.Equal(0, runs);
        Assert.Single(posted);
        posted[0]();
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ClickingARow_RunsThatRow()
    {
        int chosen = -1;
        var palette = Palette([Entry("A", run: () => chosen = 0), Entry("B", run: () => chosen = 1)], 5);
        palette.Open();

        palette.Results[1].ExecuteCommand.Execute(null);

        Assert.Equal(1, chosen);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void MoveSelection_WrapsAtBothEnds()
    {
        var palette = Palette([Entry("A"), Entry("B"), Entry("C")], 5);
        palette.Open();

        palette.MoveSelection(-1);
        Assert.Equal(2, palette.SelectedIndex);
        palette.MoveSelection(1);
        Assert.Equal(0, palette.SelectedIndex);
        Assert.True(palette.Results[0].IsSelected);
        Assert.False(palette.Results[2].IsSelected);
    }

    [Fact]
    public void Toggle_OpensThenCloses()
    {
        var palette = Palette([Entry("A")], 5);

        palette.Toggle();
        Assert.True(palette.IsOpen);
        palette.Toggle();
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void Reopening_ResetsTheQueryAndRebuildsTheCatalog()
    {
        int builds = 0;
        var palette = new ReaderCommandPaletteViewModel(() => { builds++; return [Entry("A")]; }, () => 5, _ => { }, a => a());

        palette.Open();
        palette.Query = "zzz";
        palette.Close();
        palette.Open();

        Assert.Equal(2, builds);
        Assert.Equal(string.Empty, palette.Query);
        Assert.Single(palette.Results);
    }

    [Fact]
    public void NoLoadedIssue_MeansNoGoToPageRow()
    {
        var palette = Palette([Entry("Zoom in")], 0);
        palette.Open();

        palette.Query = "5";

        Assert.DoesNotContain(palette.Results, r => r.Title.StartsWith("Go to page"));
    }
}

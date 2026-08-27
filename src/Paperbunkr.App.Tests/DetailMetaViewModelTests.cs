using System.Linq;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First test coverage for <see cref="DetailMetaViewModel"/> - previously had none. Exercises both
/// the unchanged series-aggregate shape and the new single-issue mode (docs/superpowers/specs/
/// 2026-08-07-detail-screen-issue-focus-design.md §3), including the new Cover Artist row.
///
/// Each credit became a <see cref="TagPillViewModel"/> collection (docs/superpowers/specs/
/// 2026-08-23-weighted-categorized-tags-design.md's click-to-search, extended past Genre/Tags) -
/// assertions compare against <c>.Select(p => p.Value)</c> rather than a joined string.
/// </summary>
public class DetailMetaViewModelTests
{
    private static Series MakeSeries()
    {
        var series = new Series { Name = "Test Series" };
        series.Issues.Add(new Issue { Writer = "Alan Moore", Penciller = "Dave Gibbons", CoverArtist = "Dave Gibbons", Colorist = "John Higgins", Letterer = "Todd Klein" });
        series.Issues.Add(new Issue { Writer = "Alan Moore, Frank Miller", Penciller = "Brian Bolland" });
        return series;
    }

    [Fact]
    public void LoadSeries_AggregatesDistinctCreditsAcrossIssues()
    {
        var vm = new DetailMetaViewModel();

        vm.LoadSeries(MakeSeries());

        Assert.Equal(new[] { "Alan Moore", "Frank Miller" }, vm.Writer.Select(p => p.Value));
        Assert.Equal(new[] { "Dave Gibbons", "Brian Bolland" }, vm.Artist.Select(p => p.Value));
        Assert.Equal(new[] { "Dave Gibbons" }, vm.CoverArtist.Select(p => p.Value));
        Assert.Equal(new[] { "John Higgins" }, vm.Colorist.Select(p => p.Value));
        Assert.Equal(new[] { "Todd Klein" }, vm.Letterer.Select(p => p.Value));
    }

    [Fact]
    public void LoadIssue_ShowsOnlyThatIssuesOwnCredits()
    {
        var vm = new DetailMetaViewModel();
        var issue = new Issue { Writer = "Alan Moore", Penciller = "Dave Gibbons", CoverArtist = "Dave Gibbons", Colorist = "John Higgins", Letterer = "Todd Klein" };

        vm.LoadIssue(issue);

        Assert.Equal(new[] { "Alan Moore" }, vm.Writer.Select(p => p.Value));
        Assert.Equal(new[] { "Dave Gibbons" }, vm.Artist.Select(p => p.Value));
        Assert.Equal(new[] { "Dave Gibbons" }, vm.CoverArtist.Select(p => p.Value));
        Assert.Equal(new[] { "John Higgins" }, vm.Colorist.Select(p => p.Value));
        Assert.Equal(new[] { "Todd Klein" }, vm.Letterer.Select(p => p.Value));
    }

    [Fact]
    public void LoadIssue_MissingFields_ProduceEmptyCollections()
    {
        var vm = new DetailMetaViewModel();

        vm.LoadIssue(new Issue());

        Assert.Empty(vm.Writer);
        Assert.Empty(vm.CoverArtist);
    }

    [Fact]
    public void LoadIssue_WriterPill_SearchCommand_InvokesCallbackWithTheCreditName()
    {
        string? searched = null;
        var vm = new DetailMetaViewModel(goLibraryWithSearch: q => searched = q);

        vm.LoadIssue(new Issue { Writer = "Alan Moore" });
        var pill = Assert.Single(vm.Writer);
        pill.SearchCommand.Execute(null);

        Assert.Equal("Alan Moore", searched);
    }
}

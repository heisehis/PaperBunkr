using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First test coverage for <see cref="DetailMetaViewModel"/> - previously had none. Exercises both
/// the unchanged series-aggregate shape and the new single-issue mode (docs/superpowers/specs/
/// 2026-08-07-detail-screen-issue-focus-design.md §3), including the new Cover Artist row.
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

        Assert.Equal("Alan Moore, Frank Miller", vm.Writer);
        Assert.Equal("Dave Gibbons, Brian Bolland", vm.Artist);
        Assert.Equal("Dave Gibbons", vm.CoverArtist);
        Assert.Equal("John Higgins", vm.Colorist);
        Assert.Equal("Todd Klein", vm.Letterer);
    }

    [Fact]
    public void LoadIssue_ShowsOnlyThatIssuesOwnCredits()
    {
        var vm = new DetailMetaViewModel();
        var issue = new Issue { Writer = "Alan Moore", Penciller = "Dave Gibbons", CoverArtist = "Dave Gibbons", Colorist = "John Higgins", Letterer = "Todd Klein" };

        vm.LoadIssue(issue);

        Assert.Equal("Alan Moore", vm.Writer);
        Assert.Equal("Dave Gibbons", vm.Artist);
        Assert.Equal("Dave Gibbons", vm.CoverArtist);
        Assert.Equal("John Higgins", vm.Colorist);
        Assert.Equal("Todd Klein", vm.Letterer);
    }

    [Fact]
    public void LoadIssue_MissingFields_FallBackToUnknown()
    {
        var vm = new DetailMetaViewModel();

        vm.LoadIssue(new Issue());

        Assert.Equal("Unknown", vm.Writer);
        Assert.Equal("Unknown", vm.CoverArtist);
    }
}

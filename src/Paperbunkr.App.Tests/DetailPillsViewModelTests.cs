using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First test coverage for <see cref="DetailPillsViewModel"/> - previously had none. Includes a
/// dedicated regression test for the Genre bug found during bulk-edit manual verification: this
/// row used to read the separate <see cref="Series.Genre"/> field instead of aggregating per-issue
/// <see cref="Issue.Genre"/>, so editing an issue's Genre via the bulk editor never showed up here
/// (docs/superpowers/specs/2026-08-07-detail-screen-issue-focus-design.md §3).
/// </summary>
public class DetailPillsViewModelTests
{
    [Fact]
    public void LoadSeries_Genres_AggregatesPerIssueGenre_NotSeriesGenre()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Test Series", Genre = "This Should Never Appear" };
        series.Issues.Add(new Issue { Genre = "Superhero" });
        series.Issues.Add(new Issue { Genre = "Superhero, Crime" });

        vm.LoadSeries(series);

        Assert.Equal(new[] { "Superhero", "Crime" }, vm.Genres);
        Assert.DoesNotContain("This Should Never Appear", vm.Genres);
    }

    [Fact]
    public void LoadSeries_AggregatesDistinctTeamsAndLocations()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Test Series" };
        series.Issues.Add(new Issue { Teams = "Justice League", Locations = "Gotham City" });
        series.Issues.Add(new Issue { Teams = "Justice League, Suicide Squad", Locations = "Metropolis" });

        vm.LoadSeries(series);

        Assert.Equal(new[] { "Justice League", "Suicide Squad" }, vm.Teams);
        Assert.Equal(new[] { "Gotham City", "Metropolis" }, vm.Locations);
    }

    [Fact]
    public void LoadIssue_ShowsOnlyThatIssuesOwnTags()
    {
        var vm = new DetailPillsViewModel();
        var issue = new Issue { Genre = "Superhero, Crime", Teams = "Justice League", Locations = "Gotham City" };

        vm.LoadIssue(issue);

        Assert.Equal(new[] { "Superhero", "Crime" }, vm.Genres);
        Assert.Equal(new[] { "Justice League" }, vm.Teams);
        Assert.Equal(new[] { "Gotham City" }, vm.Locations);
    }
}

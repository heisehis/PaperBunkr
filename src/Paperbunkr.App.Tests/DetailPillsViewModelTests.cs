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

    [Fact]
    public void LoadSeries_NoVirtualTagsPassed_HasVirtualTagsIsFalse()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Test Series" };
        series.Issues.Add(new Issue());

        vm.LoadSeries(series);

        Assert.Empty(vm.VirtualTags);
        Assert.False(vm.HasVirtualTags);
    }

    [Fact]
    public void LoadSeries_EvaluatesVirtualTagsAcrossIssues_DedupedWholeCaption()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Vandal Savage" };
        series.Issues.Add(new Issue { Number = "1", Writer = "Alan Moore" });
        series.Issues.Add(new Issue { Number = "2", Writer = "Alan Moore" }); // same writer -> same caption, deduped

        var tags = new[]
        {
            new VirtualTagDefinition { Id = 1, Name = "By Writer", CaptionFormat = "By {Writer}", IsEnabled = true },
        };

        vm.LoadSeries(series, tags);

        Assert.Equal(new[] { "By Alan Moore" }, vm.VirtualTags);
        Assert.True(vm.HasVirtualTags);
    }

    [Fact]
    public void LoadIssue_EvaluatesVirtualTagsAgainstOwnSeriesAndIssue()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Kilo Station" };
        var issue = new Issue { Number = "12", Series = series };

        var tags = new[]
        {
            new VirtualTagDefinition { Id = 1, Name = "Series+Number", CaptionFormat = "{Series} #{Number}", IsEnabled = true },
        };

        vm.LoadIssue(issue, tags);

        Assert.Equal(new[] { "Kilo Station #12" }, vm.VirtualTags);
    }

    [Fact]
    public void LoadIssue_VirtualTagEvaluatesToEmpty_IsSkipped()
    {
        var vm = new DetailPillsViewModel();
        var issue = new Issue { Number = "1" }; // no Series, no Writer

        var tags = new[]
        {
            new VirtualTagDefinition { Id = 1, Name = "By Writer", CaptionFormat = "{Writer}", IsEnabled = true },
        };

        vm.LoadIssue(issue, tags);

        Assert.Empty(vm.VirtualTags);
        Assert.False(vm.HasVirtualTags);
    }
}

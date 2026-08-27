using System.Linq;
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
        var issue1 = new Issue();
        issue1.MergeFrom(IssueTagField.Genre, new[] { "Superhero" });
        series.Issues.Add(issue1);
        var issue2 = new Issue();
        issue2.MergeFrom(IssueTagField.Genre, new[] { "Superhero, Crime" });
        series.Issues.Add(issue2);

        vm.LoadSeries(series);

        Assert.Equal(new[] { "Crime", "Superhero" }, vm.Genres.Select(g => g.Value)); // alphabetical, not insertion order
        Assert.DoesNotContain("This Should Never Appear", vm.Genres.Select(g => g.Value));
    }

    [Fact]
    public void LoadSeries_AggregatesDistinctTeamsAndLocations()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Test Series" };
        series.Issues.Add(new Issue { Teams = "Justice League", Locations = "Gotham City" });
        series.Issues.Add(new Issue { Teams = "Justice League, Suicide Squad", Locations = "Metropolis" });

        vm.LoadSeries(series);

        Assert.Equal(new[] { "Justice League", "Suicide Squad" }, vm.Teams.Select(p => p.Value));
        Assert.Equal(new[] { "Gotham City", "Metropolis" }, vm.Locations.Select(p => p.Value));
    }

    [Fact]
    public void LoadIssue_ShowsOnlyThatIssuesOwnTags()
    {
        var vm = new DetailPillsViewModel();
        var issue = new Issue { Teams = "Justice League", Locations = "Gotham City" };
        issue.MergeFrom(IssueTagField.Genre, new[] { "Superhero, Crime" });

        vm.LoadIssue(issue);

        Assert.Equal(new[] { "Crime", "Superhero" }, vm.Genres.Select(g => g.Value)); // alphabetical, not insertion order
        Assert.Equal(new[] { "Justice League" }, vm.Teams.Select(p => p.Value));
        Assert.Equal(new[] { "Gotham City" }, vm.Locations.Select(p => p.Value));
    }

    [Fact]
    public void LoadSeries_SameGenreDifferentWeightsAcrossIssues_TakesTheHighestWeight()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Test Series" };
        var issue1 = new Issue();
        issue1.MergeFrom(IssueTagField.Genre, new[] { "Time Skip" });
        issue1.Tags.Single().Weight = IssueTagWeight.Incidental;
        var issue2 = new Issue();
        issue2.MergeFrom(IssueTagField.Genre, new[] { "Time Skip" });
        issue2.Tags.Single().Weight = IssueTagWeight.Core;
        series.Issues.Add(issue1);
        series.Issues.Add(issue2);

        vm.LoadSeries(series);

        var pill = Assert.Single(vm.Genres);
        Assert.Equal(IssueTagWeight.Core, pill.Weight);
    }

    [Fact]
    public void LoadSeries_ReweightIsDisabled_NoSingleIssueToWriteTo()
    {
        var vm = new DetailPillsViewModel();
        var series = new Series { Name = "Test Series" };
        var issue = new Issue();
        issue.MergeFrom(IssueTagField.Genre, new[] { "Horror" });
        series.Issues.Add(issue);

        vm.LoadSeries(series);

        Assert.False(Assert.Single(vm.Genres).CanReweight);
    }

    [Fact]
    public void LoadIssue_SearchCommand_InvokesCallbackWithTheTagValue()
    {
        string? searched = null;
        var vm = new DetailPillsViewModel(goLibraryWithSearch: q => searched = q);
        var issue = new Issue();
        issue.MergeFrom(IssueTagField.Genre, new[] { "Horror" });

        vm.LoadIssue(issue);
        var pill = Assert.Single(vm.Genres);
        pill.SearchCommand.Execute(null);

        Assert.Equal("Horror", searched);
    }

    [Fact]
    public void LoadIssue_ReweightCommand_InvokesCallbackWithIssueFieldValueWeight_AndUpdatesLocalWeight()
    {
        (int IssueId, IssueTagField Field, string Value, IssueTagWeight Weight)? reweighted = null;
        var vm = new DetailPillsViewModel(reweightTag: (issueId, field, value, weight) => reweighted = (issueId, field, value, weight));
        var issue = new Issue { Id = 42 };
        issue.MergeFrom(IssueTagField.Genre, new[] { "Horror" });

        vm.LoadIssue(issue);
        var pill = Assert.Single(vm.Genres);
        Assert.True(pill.CanReweight);
        pill.SetWeightCommand.Execute(IssueTagWeight.Core);

        Assert.Equal((42, IssueTagField.Genre, "Horror", IssueTagWeight.Core), reweighted);
        Assert.Equal(IssueTagWeight.Core, pill.Weight);
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

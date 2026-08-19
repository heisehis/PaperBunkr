using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Exercises <see cref="IssueListScreenViewModel"/> (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-group-design.md).
/// Since the merge into Library as a view mode, this VM has no DB access of its own - <see
/// cref="IssueListScreenViewModel.SetRows"/> is fed directly with in-memory <see cref="Issue"/>
/// entities, matching how <see cref="LibraryScreenViewModel"/> calls it in production.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class IssueListScreenViewModelTests
{
    private static Issue MakeIssue(string seriesName, string title, string? writer = null, string? publisher = null, DateTime? added = null)
    {
        var series = new Series { Name = seriesName };
        return new Issue { Series = series, SeriesId = 0, Title = title, Writer = writer, Publisher = publisher, AddedTime = added ?? DateTime.UtcNow };
    }

    [Fact]
    public void SetRows_LoadsIssuesAcrossMultipleSeries()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });

        vm.SetRows(new[] { MakeIssue("Series A", "Issue A1"), MakeIssue("Series B", "Issue B1") });

        Assert.Equal(2, vm.Rows.Count);
        Assert.Contains(vm.Rows, r => r.SeriesName == "Series A");
        Assert.Contains(vm.Rows, r => r.SeriesName == "Series B");
    }

    [Fact]
    public void SetRows_CalledAgain_ReplacesRatherThanAppends()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });
        vm.SetRows(new[] { MakeIssue("Series A", "Issue A1") });

        vm.SetRows(new[] { MakeIssue("Series B", "Issue B1") });

        Assert.Single(vm.Rows);
        Assert.Equal("Series B", vm.Rows[0].SeriesName);
    }

    [Fact]
    public void SortField_ChangedToTitle_ReordersRows()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });
        vm.SetRows(new[] { MakeIssue("Series A", "Zebra"), MakeIssue("Series A", "Apple") });

        vm.SortField = IssueListSortField.Title;
        vm.SortDirection = SortDirection.Ascending;

        Assert.Equal("Apple", vm.Rows[0].Title);
        Assert.Equal("Zebra", vm.Rows[1].Title);
    }

    /// <summary>
    /// Real bug found via on-screen verification: the toolbar pill's label is a plain computed
    /// property, not an [ObservableProperty] - changing SortField/GroupField reordered the data
    /// correctly (the automated test above already covered that) but the pill kept showing its
    /// stale default label, since nothing raised PropertyChanged for it.
    /// </summary>
    [Fact]
    public void SortFieldLabel_UpdatesWhenSortFieldChanges()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });
        string before = vm.SortFieldLabel;

        vm.SortField = IssueListSortField.Title;

        Assert.NotEqual(before, vm.SortFieldLabel);
        Assert.Equal("Title", vm.SortFieldLabel);
    }

    [Fact]
    public void GroupFieldLabel_UpdatesWhenGroupFieldChanges()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });
        Assert.Equal("None", vm.GroupFieldLabel);

        vm.GroupField = IssueListGroupField.Publisher;

        Assert.Equal("Publisher", vm.GroupFieldLabel);
    }

    [Fact]
    public void ToggleSortDirection_ReversesOrder()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });
        vm.SetRows(new[] { MakeIssue("Series A", "Apple"), MakeIssue("Series A", "Zebra") });
        vm.SortField = IssueListSortField.Title;
        vm.SortDirection = SortDirection.Ascending;

        vm.ToggleSortDirectionCommand.Execute(null);

        Assert.Equal(SortDirection.Descending, vm.SortDirection);
        Assert.Equal("Zebra", vm.Rows[0].Title);
    }

    [Fact]
    public void GroupField_SetToPublisher_ProducesBuckets_ClearsFlatRows()
    {
        var vm = new IssueListScreenViewModel(goReaderForIssue: _ => { });
        vm.SetRows(new[]
        {
            MakeIssue("Series A", "One", publisher: "Marvel"),
            MakeIssue("Series A", "Two", publisher: "DC"),
        });

        vm.GroupField = IssueListGroupField.Publisher;

        Assert.True(vm.IsGrouped);
        Assert.Empty(vm.Rows);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Contains(vm.Groups, g => g.Header == "Marvel");
        Assert.Contains(vm.Groups, g => g.Header == "DC");
    }

    [Fact]
    public void OpenIssue_InvokesGoReaderForIssue_WithTheRowsIssueId()
    {
        var issue = MakeIssue("Series A", "Issue A1");
        issue.Id = 42;
        int? openedId = null;
        var vm = new IssueListScreenViewModel(goReaderForIssue: id => openedId = id);
        vm.SetRows(new[] { issue });

        vm.OpenIssueCommand.Execute(vm.Rows[0]);

        Assert.Equal(42, openedId);
    }
}

using System.Collections.Specialized;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The pieces <see cref="IssueListScreenViewModel"/> gained for the Library view pipeline
/// (docs/superpowers/specs/2026-09-19-library-search-perf-design.md §4/§5): the resolved sort/group
/// snapshot, <c>AutoRender</c>, and publishing precomputed rows with one <c>Reset</c> per collection.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class IssueListSortGroupSpecTests
{
    private static IssueListRow Row(int id, string series, string number = "1", string? publisher = null)
    {
        var owner = new Series { Id = id, Name = series };
        return IssueListRow.FromIssue(new Issue { Id = id, SeriesId = id, Number = number, Publisher = publisher }, owner);
    }

    [Fact]
    public void CaptureSortGroupSpec_ResolvesTheCurrentFields()
    {
        var list = new IssueListScreenViewModel(_ => { })
        {
            SortField = IssueListSortField.Series,
            SortDirection = SortDirection.Ascending,
            GroupField = IssueListGroupField.Publisher,
        };

        var spec = list.CaptureSortGroupSpec();

        Assert.Equal(IssueListSortField.Series, spec.RowSort.Field);
        Assert.Equal(IssueListSortField.Series, spec.CardSort.Field);
        Assert.Equal(IssueListGroupField.Publisher, spec.RowGroup!.Field);
        Assert.Equal(IssueListGroupField.Publisher, spec.CardGroup!.Field);
        Assert.Equal(SortDirection.Ascending, spec.Direction);
        Assert.True(spec.IsGrouped);
    }

    [Fact]
    public void CaptureSortGroupSpec_UnresolvableVirtualTag_KeepsTheOldFallbacks()
    {
        var list = new IssueListScreenViewModel(_ => { })
        {
            SortField = IssueListSortField.VirtualTag,
            SortVirtualTagId = 99,
            GroupField = IssueListGroupField.VirtualTag,
            GroupVirtualTagId = 99,
        };

        var spec = list.CaptureSortGroupSpec();

        // Rows: a deleted sort tag falls back to Added; a deleted group tag resolves to no group.
        Assert.Equal(IssueListSortField.Added, spec.RowSort.Field);
        Assert.Null(spec.RowGroup);
        // Cards only ever use the static catalog: unknown sort field -> Series, unknown group -> none.
        Assert.Equal(IssueListSortField.Series, spec.CardSort.Field);
        Assert.Null(spec.CardGroup);
    }

    [Fact]
    public void SpecSortRows_HonoursDirection()
    {
        var list = new IssueListScreenViewModel(_ => { }) { SortField = IssueListSortField.Series, SortDirection = SortDirection.Descending };
        var spec = list.CaptureSortGroupSpec();

        var sorted = spec.SortRows(new List<IssueListRow> { Row(1, "Alpha"), Row(2, "Charlie"), Row(3, "Bravo") });

        Assert.Equal(new[] { "Charlie", "Bravo", "Alpha" }, sorted.Select(r => r.SeriesName));
    }

    [Fact]
    public void AutoRenderFalse_MakesSetRowsAndSortChangesNoOps()
    {
        var list = new IssueListScreenViewModel(_ => { }) { AutoRender = false };
        var issue = new Issue { Id = 1, SeriesId = 1, Number = "1", Series = new Series { Id = 1, Name = "S" } };

        list.SetRows(new[] { issue });
        list.SortField = IssueListSortField.Series;
        list.GroupField = IssueListGroupField.Publisher;

        Assert.Empty(list.Rows);
        Assert.Empty(list.Groups);
        Assert.Empty(list.FlatRows);
    }

    [Fact]
    public void ApplyPrecomputed_Ungrouped_FillsRowsAndFlat_WithOneResetEach()
    {
        var list = new IssueListScreenViewModel(_ => { });
        var rowEvents = new List<NotifyCollectionChangedAction>();
        var flatEvents = new List<NotifyCollectionChangedAction>();
        list.Rows.CollectionChanged += (_, e) => rowEvents.Add(e.Action);
        list.FlatRows.CollectionChanged += (_, e) => flatEvents.Add(e.Action);
        var rows = new List<IssueListRow> { Row(1, "A"), Row(2, "B"), Row(3, "C") };

        list.ApplyPrecomputed(rows, Array.Empty<ViewGroup<IssueListRow>>(), isGrouped: false);

        Assert.Equal(rows, list.Rows);
        Assert.Equal(rows.Cast<object>(), list.FlatRows);
        Assert.Empty(list.Groups);
        Assert.True(list.HasAnyResults);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, rowEvents);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, flatEvents);
    }

    [Fact]
    public void ApplyPrecomputed_Grouped_InterleavesHeaders_AndLeavesRowsEmpty()
    {
        var list = new IssueListScreenViewModel(_ => { });
        var groups = new List<ViewGroup<IssueListRow>>
        {
            new("DC", new[] { Row(1, "Batman", publisher: "DC"), Row(2, "Superman", publisher: "DC") }),
            new("Image", new[] { Row(3, "Saga", publisher: "Image") }),
        };

        list.ApplyPrecomputed(Array.Empty<IssueListRow>(), groups, isGrouped: true);

        Assert.Empty(list.Rows);
        Assert.Equal(new[] { "DC", "Image" }, list.Groups.Select(g => g.Header));
        Assert.Equal(new[] { 2, 1 }, list.Groups.Select(g => g.Items.Count));
        Assert.Equal(5, list.FlatRows.Count);
        Assert.Equal(new GridSectionHeader("DC", 2), list.FlatRows[0]);
        Assert.IsType<IssueListRow>(list.FlatRows[1]);
        Assert.Equal(new GridSectionHeader("Image", 1), list.FlatRows[3]);
        Assert.True(list.HasAnyResults);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One header plus its already-sorted items - the thread-safe (plain list) shape a background
/// view computation returns before the UI thread wraps it into <c>SeriesCardGroup</c>/<c>IssueListRowGroup</c>.</summary>
public sealed record ViewGroup<T>(string Header, IReadOnlyList<T> Items);

/// <summary>
/// Immutable snapshot of Library's single sort/group pool (docs/superpowers/specs/
/// 2026-09-19-library-search-perf-design.md §4/§7), with every descriptor already resolved on the UI
/// thread. <see cref="IssueListScreenViewModel.CaptureSortGroupSpec"/> builds it; the view pipeline then
/// sorts and groups on a worker thread without touching any view-model state. Descriptors are pure
/// delegates over <see cref="IssueListRow"/>, so applying them off-thread is safe.
///
/// <para>
/// The sort/group logic itself is moved here verbatim from <c>IssueListScreenViewModel</c> (rows) and
/// <c>LibraryScreenViewModel</c> (cards) so both call sites share one implementation:
/// <list type="bullet">
///   <item>Rows use the resolved sort/group descriptor, virtual-tag aware (a group field whose tag was
///   deleted resolves to <see langword="null"/> and yields no groups, as before).</item>
///   <item>Series cards use only the static catalog: an unknown sort field (incl. a dynamic virtual-tag
///   sort) falls back to Series, an unknown group field yields no groups - the pre-existing behavior.</item>
/// </list>
/// </para>
/// </summary>
public sealed record IssueListSortGroupSpec(
    IssueListSortFieldDescriptor RowSort,
    IssueListGroupFieldDescriptor? RowGroup,
    IssueListSortFieldDescriptor CardSort,
    IssueListGroupFieldDescriptor? CardGroup,
    SortDirection Direction,
    bool IsGrouped)
{
    public List<IssueListRow> SortRows(List<IssueListRow> rows)
    {
        var result = rows.ToList();
        result.Sort(RowSort.Compare);
        if (Direction == SortDirection.Descending)
        {
            result.Reverse();
        }

        return result;
    }

    public IReadOnlyList<ViewGroup<IssueListRow>> GroupRows(List<IssueListRow> sortedRows)
    {
        if (RowGroup is null)
        {
            return Array.Empty<ViewGroup<IssueListRow>>();
        }

        return sortedRows
            .GroupBy(RowGroup.GroupKey)
            .OrderBy(g => g.Key, Comparer<string>.Create(RowGroup.GroupOrder))
            .Select(g => new ViewGroup<IssueListRow>(g.Key, g.ToList()))
            .ToList();
    }

    public List<SeriesCardSample> SortCards(List<SeriesCardSample> cards)
    {
        var result = cards.ToList();
        result.Sort((a, b) => CardSort.Compare(a.RepresentativeRow, b.RepresentativeRow));
        if (Direction == SortDirection.Descending)
        {
            result.Reverse();
        }

        return result;
    }

    public IReadOnlyList<ViewGroup<SeriesCardSample>> GroupCards(List<SeriesCardSample> sortedCards)
    {
        if (CardGroup is null)
        {
            return Array.Empty<ViewGroup<SeriesCardSample>>();
        }

        return sortedCards
            .GroupBy(c => CardGroup.GroupKey(c.RepresentativeRow))
            .OrderBy(g => g.Key, Comparer<string>.Create(CardGroup.GroupOrder))
            .Select(g => new ViewGroup<SeriesCardSample>(g.Key, g.ToList()))
            .ToList();
    }
}

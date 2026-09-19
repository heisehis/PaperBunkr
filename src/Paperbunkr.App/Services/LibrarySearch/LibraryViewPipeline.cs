using System;
using System.Collections.Generic;
using System.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.LibrarySearch;

/// <summary>Everything a view computation reads, captured on the UI thread so the worker never touches view-model state.</summary>
internal sealed record LibraryViewInputs(
    ContentType? ActiveContentType,
    IReadOnlySet<int>? CollectionSeriesIds,
    string EffectiveQuery,
    SearchMode SearchMode,
    bool FilterTrackedOnly,
    bool FilterUnreadOnly,
    bool FilterMissingIssues,
    IssueListSortGroupSpec Spec);

/// <summary>
/// Plain-list output of <see cref="LibraryViewPipeline.Compute"/>. No <c>ObservableCollection</c> inside,
/// nothing bound to the UI - the UI thread wraps and swaps it in (design doc §5/§7). Exactly one of
/// <see cref="Cards"/>/<see cref="CardGroups"/> is populated, and likewise for rows, matching how the
/// old <c>RebuildView</c> filled <c>Covers</c> vs <c>Groups</c>.
/// </summary>
internal sealed class LibraryViewResult
{
    public required bool IsGrouped { get; init; }
    public required IReadOnlyList<SeriesCardSample> Cards { get; init; }
    public required IReadOnlyList<ViewGroup<SeriesCardSample>> CardGroups { get; init; }
    public required IReadOnlyList<IssueListRow> Rows { get; init; }
    public required IReadOnlyList<ViewGroup<IssueListRow>> RowGroups { get; init; }

    /// <summary>Set only when the computation had to build the projection itself (none was cached); the view-model adopts it.</summary>
    public LibraryProjection? BuiltProjection { get; init; }
}

/// <summary>
/// The filter / sort / group half of the old <c>LibraryScreenViewModel.RebuildView</c>, as a pure function
/// of an immutable <see cref="LibraryProjection"/> and <see cref="LibraryViewInputs"/>
/// (docs/superpowers/specs/2026-09-19-library-search-perf-design.md §1-§4). Safe to run on any thread.
///
/// Semantics, unchanged from before except where noted:
/// <list type="bullet">
///   <item>content type / collection / tracked filters are series-level;</item>
///   <item><b>series cards</b>: search matches on series-level fields or any issue; Unread/Missing mean
///   "series containing at least one such issue";</item>
///   <item><b>issue rows</b> (changed, CE parity): an issue is listed when the series-level text matches
///   (so a series-name search still lists every issue of that series) <b>or</b> its own per-mode bundle
///   matches - not merely because a sibling issue matched. Unread/Missing apply per issue.</item>
/// </list>
/// </summary>
internal static class LibraryViewPipeline
{
    private const int CancellationCheckInterval = 512;

    public static LibraryViewResult Compute(
        LibraryViewInputs inputs,
        LibraryProjection projection,
        LibraryProjection? builtProjection,
        CancellationToken cancellationToken)
    {
        string? query = inputs.EffectiveQuery.Length == 0 ? null : LibrarySearchIndex.Normalize(inputs.EffectiveQuery);
        var index = projection.Index;

        var cards = new List<SeriesCardSample>();
        var rows = new List<IssueListRow>();

        int visited = 0;
        foreach (var entry in projection.Entries)
        {
            if ((++visited % CancellationCheckInterval) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var series = entry.Series;

            if (inputs.ActiveContentType is ContentType contentType)
            {
                if (series.ContentType != contentType)
                {
                    continue;
                }
            }
            else if (inputs.CollectionSeriesIds is { } memberSeriesIds && !memberSeriesIds.Contains(series.Id))
            {
                continue;
            }

            bool seriesLevelMatch = false;
            if (query is not null)
            {
                seriesLevelMatch = index.SeriesLevelMatches(series.Id, inputs.SearchMode, query);
                if (!seriesLevelMatch && !index.SeriesMatches(series.Id, inputs.SearchMode, query))
                {
                    continue;
                }
            }

            if (inputs.FilterTrackedOnly && series.TrackingLinks.Count == 0)
            {
                continue;
            }

            // Series card: Unread / Missing at the series level.
            if ((!inputs.FilterUnreadOnly || HasUnreadIssue(series))
                && (!inputs.FilterMissingIssues || HasMissingIssue(series)))
            {
                cards.Add(entry.Card);
            }

            // Issue rows: per-issue matching and per-issue Unread / Missing.
            var issues = series.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (query is not null && !seriesLevelMatch && !index.IssueMatches(issue.Id, inputs.SearchMode, query))
                {
                    continue;
                }

                if (inputs.FilterUnreadOnly && !IsUnread(issue))
                {
                    continue;
                }

                if (inputs.FilterMissingIssues && !issue.FileIsMissing)
                {
                    continue;
                }

                rows.Add(entry.IssueRows[i]);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var spec = inputs.Spec;
        var sortedCards = spec.SortCards(cards);
        cancellationToken.ThrowIfCancellationRequested();
        var sortedRows = spec.SortRows(rows);
        cancellationToken.ThrowIfCancellationRequested();

        if (spec.IsGrouped)
        {
            return new LibraryViewResult
            {
                IsGrouped = true,
                Cards = Array.Empty<SeriesCardSample>(),
                CardGroups = spec.GroupCards(sortedCards),
                Rows = Array.Empty<IssueListRow>(),
                RowGroups = spec.GroupRows(sortedRows),
                BuiltProjection = builtProjection,
            };
        }

        return new LibraryViewResult
        {
            IsGrouped = false,
            Cards = sortedCards,
            CardGroups = Array.Empty<ViewGroup<SeriesCardSample>>(),
            Rows = sortedRows,
            RowGroups = Array.Empty<ViewGroup<IssueListRow>>(),
            BuiltProjection = builtProjection,
        };
    }

    private static bool IsUnread(Issue issue) => issue.LastPageRead is null or 0;

    private static bool HasUnreadIssue(Series series)
    {
        foreach (var issue in series.Issues)
        {
            if (IsUnread(issue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMissingIssue(Series series)
    {
        foreach (var issue in series.Issues)
        {
            if (issue.FileIsMissing)
            {
                return true;
            }
        }

        return false;
    }
}

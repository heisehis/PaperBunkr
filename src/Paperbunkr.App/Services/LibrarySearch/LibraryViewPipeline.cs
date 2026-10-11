using System;
using System.Collections.Generic;
using System.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

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
    IssueListSortGroupSpec Spec,
    int? SourceFilter = null,   // null = every library, 0 = this computer only, n = the remote library with source id n
    LibraryLens Lens = LibraryLens.All,
    string? PublisherFilter = null);   // null = every publisher; compared with the card's aggregated publisher

/// <summary>
/// How many items each <see cref="LibraryLens"/> tab would show: everything that passed the scope, the filter chips and
/// the search, counted before the lens itself is applied, so a tab's number answers "what would I see if I clicked it"
/// (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 1). <see cref="All"/> is the sum of the other three.
/// </summary>
internal readonly record struct LensCounts(int Reading, int Unread, int Read)
{
    public int All => Reading + Unread + Read;

    public int Of(LibraryLens lens) => lens switch
    {
        LibraryLens.Reading => Reading,
        LibraryLens.Unread => Unread,
        LibraryLens.Read => Read,
        _ => All,
    };
}

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

    /// <summary>Lens tallies for series cards and for issue rows, taken in the same pass as the filter.</summary>
    public LensCounts SeriesLensCounts { get; init; }
    public LensCounts IssueLensCounts { get; init; }

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
///   <item>the <see cref="LibraryLens"/> is applied last, per series for cards and per issue for rows, after both have
///   been counted for every lens.</item>
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
        var lens = inputs.Lens;
        int seriesReading = 0, seriesUnread = 0, seriesRead = 0;
        int issueReading = 0, issueUnread = 0, issueRead = 0;

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

            if (inputs.SourceFilter is int source && (source == 0 ? series.RemoteSourceId is not null : series.RemoteSourceId != source))
            {
                continue;
            }

            if (inputs.PublisherFilter is { } publisher && !string.Equals(entry.Card.Publisher, publisher, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Series card: Unread / Missing at the series level.
            if ((!inputs.FilterUnreadOnly || HasUnreadIssue(series))
                && (!inputs.FilterMissingIssues || HasMissingIssue(series)))
            {
                var seriesLens = LensOf(series);
                switch (seriesLens)
                {
                    case LibraryLens.Reading: seriesReading++; break;
                    case LibraryLens.Unread: seriesUnread++; break;
                    default: seriesRead++; break;
                }

                if (lens == LibraryLens.All || lens == seriesLens)
                {
                    cards.Add(entry.Card);
                }
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

                var issueLens = LensOf(issue);
                switch (issueLens)
                {
                    case LibraryLens.Reading: issueReading++; break;
                    case LibraryLens.Unread: issueUnread++; break;
                    default: issueRead++; break;
                }

                if (lens == LibraryLens.All || lens == issueLens)
                {
                    rows.Add(entry.IssueRows[i]);
                }
            }
        }

        var seriesCounts = new LensCounts(seriesReading, seriesUnread, seriesRead);
        var issueCounts = new LensCounts(issueReading, issueUnread, issueRead);

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
                SeriesLensCounts = seriesCounts,
                IssueLensCounts = issueCounts,
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
            SeriesLensCounts = seriesCounts,
            IssueLensCounts = issueCounts,
        };
    }

    private static bool IsUnread(Issue issue) => issue.LastPageRead is null or 0;

    /// <summary>One issue's lens: unread (never opened), read (past CE's read threshold), otherwise reading. Never <see cref="LibraryLens.All"/>.</summary>
    internal static LibraryLens LensOf(Issue issue) =>
        IsUnread(issue) ? LibraryLens.Unread : issue.HasBeenRead() ? LibraryLens.Read : LibraryLens.Reading;

    /// <summary>
    /// A series' lens, mutually exclusive so the tab counts add up: unread when no issue has been opened (or it has no
    /// issues), read when every issue is read, otherwise reading. Never <see cref="LibraryLens.All"/>.
    /// </summary>
    internal static LibraryLens LensOf(Series series)
    {
        bool anyOpened = false;
        bool allRead = true;
        foreach (var issue in series.Issues)
        {
            if (IsUnread(issue))
            {
                allRead = false;
            }
            else
            {
                anyOpened = true;
                if (!issue.HasBeenRead())
                {
                    allRead = false;
                }
            }
        }

        if (!anyOpened)
        {
            return LibraryLens.Unread;
        }

        return allRead ? LibraryLens.Read : LibraryLens.Reading;
    }

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

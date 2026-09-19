using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.LibrarySearch;

/// <summary>One series' cached card plus one cached row per issue (parallel to <see cref="Series.Issues"/>).</summary>
internal sealed class LibraryProjectionEntry
{
    public required Series Series { get; init; }
    public required SeriesCardSample Card { get; init; }
    public required IssueListRow[] IssueRows { get; init; }
}

/// <summary>
/// Immutable, versioned cache of everything <c>RebuildView</c> used to re-project on every keystroke
/// (docs/superpowers/specs/2026-09-19-library-search-perf-design.md §4): one
/// <see cref="SeriesCardSample"/> per series, one <see cref="IssueListRow"/> per issue, and the
/// <see cref="LibrarySearchIndex"/>. Built once per data load.
///
/// Immutable on purpose: a background view job reads exactly one version and never sees it change
/// under it. <see cref="WithSeriesRebuilt"/> does not mutate; it returns a new version sharing every
/// unchanged entry. The rows/cards themselves are <c>ObservableObject</c>s, but the only thing that
/// ever writes to them afterwards is the UI thread (selection sync at swap time).
///
/// Rows are built with <c>IsSelected = false</c>; the swap syncs selection from the authoritative
/// selection set, because a cached row can be stale (see the design doc's "Selection state").
/// </summary>
internal sealed class LibraryProjection
{
    private LibraryProjection(LibraryProjectionEntry[] entries, LibrarySearchIndex index, long dataVersion)
    {
        Entries = entries;
        Index = index;
        DataVersion = dataVersion;
    }

    /// <summary>Entries in the same order as the <c>Series</c> snapshot they were built from.</summary>
    public IReadOnlyList<LibraryProjectionEntry> Entries { get; }

    public LibrarySearchIndex Index { get; }

    /// <summary>Identifies the data load this projection belongs to; a projection built for an older load is never adopted.</summary>
    public long DataVersion { get; }

    public static LibraryProjection Build(
        IReadOnlyList<Series> allSeries,
        IReadOnlyList<VirtualTagDefinition> virtualTags,
        long dataVersion,
        CancellationToken cancellationToken = default)
    {
        var entries = new LibraryProjectionEntry[allSeries.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            if ((i & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            entries[i] = BuildEntry(allSeries[i], virtualTags);
        }

        var index = LibrarySearchIndex.Build(allSeries, cancellationToken);
        return new LibraryProjection(entries, index, dataVersion);
    }

    private static LibraryProjectionEntry BuildEntry(Series series, IReadOnlyList<VirtualTagDefinition> virtualTags)
    {
        var rows = new IssueListRow[series.Issues.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = IssueListRow.FromIssue(series.Issues[i], series, isSelected: null, virtualTags);
        }

        return new LibraryProjectionEntry
        {
            Series = series,
            Card = SeriesCardSample.FromSeries(series),
            IssueRows = rows,
        };
    }

    /// <summary>New version with the entry for <paramref name="series"/>' id rebuilt (used after an in-place
    /// series-field change, e.g. status). The search index is shared: none of those fields is searchable.
    /// Returns <see langword="this"/> when the series is not part of this projection.</summary>
    public LibraryProjection WithSeriesRebuilt(Series series, IReadOnlyList<VirtualTagDefinition> virtualTags)
    {
        int position = -1;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Series.Id == series.Id)
            {
                position = i;
                break;
            }
        }

        if (position < 0)
        {
            return this;
        }

        var copy = Entries.ToArray();
        copy[position] = BuildEntry(series, virtualTags);
        return new LibraryProjection(copy, Index, DataVersion);
    }
}

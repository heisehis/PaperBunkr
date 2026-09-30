using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.History;

/// <summary>Which Insights History type chip a row falls under.</summary>
public enum ReadingHistoryContentKind
{
    Comic,
    Manga,
    Book,
}

/// <summary>What a History row's ▶ does (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §2, ▶ table).</summary>
public enum ReadingHistoryAction
{
    /// <summary>No button: greyed row, finished book, or caught up on a comic series.</summary>
    None,

    /// <summary>Resume the last-read issue/book (<see cref="ReadingHistoryRow.TargetId"/>).</summary>
    Resume,

    /// <summary>The last-read issue was finished; open the next one in series order.</summary>
    ReadNext,

    /// <summary>A PDF - the PDF reader saves no position, so this opens rather than resumes.</summary>
    Open,
}

/// <summary>One Insights History row: one comic series, book series or standalone book, at its most recent read.</summary>
/// <param name="ItemLabel">The last-read issue/book label (<c>#12</c>, a book title), or null.</param>
/// <param name="LastReadUtc">The group's newest non-hidden reading event.</param>
/// <param name="CoverKey">A <c>CoverFingerprint.Stem</c> - an issue thumbnail key for comics, a book cover key for books; null when greyed.</param>
/// <param name="IsInLibrary">False = the whole series/book is gone; the row is greyed and named from the log's snapshot.</param>
/// <param name="IsFinished">Live read state of the last-read item (or, for a greyed row, whether its newest event was a finish).</param>
/// <param name="IsCaughtUp">Finished the last issue of a comic series with nothing after it.</param>
/// <param name="ProgressText"><c>page 6 of 17</c> / <c>38%</c> for an in-progress item, else null.</param>
/// <param name="TargetId">Issue id (comics) or book id (books) that <see cref="Action"/> opens; null for <see cref="ReadingHistoryAction.None"/>.</param>
/// <param name="TargetLabel">Label of the issue <see cref="Action"/> opens - differs from <paramref name="ItemLabel"/> for Read next.</param>
/// <param name="DetailSeriesId">Comic series to open on row click, when it exists.</param>
/// <param name="DetailBookId">Book to open on row click, when it exists.</param>
public sealed record ReadingHistoryRow(
    ReadingHistoryGroupKey GroupKey,
    string Title,
    string? ItemLabel,
    DateTime LastReadUtc,
    string? CoverKey,
    ReadingHistoryContentKind ContentKind,
    bool IsInLibrary,
    bool IsFinished,
    bool IsCaughtUp,
    string? ProgressText,
    ReadingHistoryAction Action,
    int? TargetId,
    string? TargetLabel,
    BookFormat? BookFormat,
    int? DetailSeriesId,
    int? DetailBookId);

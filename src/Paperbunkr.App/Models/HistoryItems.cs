using System;
using System.Collections.Generic;
using System.Globalization;
using Paperbunkr.App.Services.History;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>Which type chip is active on the Insights History tab.</summary>
public enum HistoryTypeFilter
{
    All,
    Comics,
    Manga,
    Books,
}

/// <summary>A day header in the History list (TODAY / YESTERDAY / TUESDAY / SEP 14) - a flat list item, since
/// Avalonia's virtualizing lists have no native grouped/sticky headers (design §3).</summary>
public sealed record HistoryDayHeader(string Label);

/// <summary>The tint of a History row's tag chip.</summary>
public enum HistoryTagKind
{
    None,
    Success,
    Warning,
}

/// <summary>
/// One History row as the view shows it (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §4):
/// a <see cref="ReadingHistoryRow"/> plus its display strings. Pure presentation - the commands live on
/// <c>HistoryTabViewModel</c> and take this as their parameter.
/// </summary>
public sealed class HistoryRowItem
{
    public HistoryRowItem(ReadingHistoryRow row, TimeZoneInfo timeZone)
    {
        Row = row;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.LastReadUtc, DateTimeKind.Utc), timeZone);
        LocalDate = localTime.Date;

        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(row.ItemLabel))
        {
            parts.Add(row.ItemLabel);
        }

        if (!string.IsNullOrWhiteSpace(row.ProgressText))
        {
            parts.Add(row.ProgressText);
        }

        parts.Add(localTime.ToString("h:mm tt", CultureInfo.InvariantCulture));
        if (row.BookFormat == BookFormat.Pdf)
        {
            parts.Add("PDF");
        }

        Subtitle = string.Join(" · ", parts);

        (TagText, TagKind) = row switch
        {
            { IsInLibrary: false } => ("NO LONGER IN LIBRARY", HistoryTagKind.Warning),
            { IsCaughtUp: true } => ("CAUGHT UP", HistoryTagKind.Success),
            { IsFinished: true } => ("FINISHED", HistoryTagKind.Success),
            _ => ((string?)null, HistoryTagKind.None),
        };

        PlayTooltip = row.Action switch
        {
            ReadingHistoryAction.ReadNext => $"Read next: {row.TargetLabel}",
            ReadingHistoryAction.Resume when !string.IsNullOrWhiteSpace(row.TargetLabel) => $"Resume {row.TargetLabel}",
            ReadingHistoryAction.Resume => "Resume",
            _ => null,
        };
    }

    public ReadingHistoryRow Row { get; }

    /// <summary>Local calendar day of <see cref="ReadingHistoryRow.LastReadUtc"/> - the day-header grouping key.</summary>
    public DateTime LocalDate { get; }

    public string Title => Row.Title;

    /// <summary><c>#4 · page 6 of 17 · 4:52 PM</c> (plus <c>· PDF</c> for PDFs).</summary>
    public string Subtitle { get; }

    public string? TagText { get; }

    public HistoryTagKind TagKind { get; }

    public bool HasTag => TagText is not null;

    public bool IsSuccessTag => TagKind == HistoryTagKind.Success;

    public bool IsWarningTag => TagKind == HistoryTagKind.Warning;

    public string? CoverKey => Row.CoverKey;

    /// <summary>A live comic/manga row - cover through <c>CoverThumb</c>'s issue-thumbnail pipeline.</summary>
    public bool ShowIssueCover => Row.IsInLibrary && Row.ContentKind != ReadingHistoryContentKind.Book;

    /// <summary>A live book row - cover through the separate book-cover cache.</summary>
    public bool ShowBookCover => Row.IsInLibrary && Row.ContentKind == ReadingHistoryContentKind.Book;

    /// <summary>Greyed "no longer in library" row: hatched placeholder, no actions.</summary>
    public bool IsGone => !Row.IsInLibrary;

    public bool ShowPlay => Row.Action is ReadingHistoryAction.Resume or ReadingHistoryAction.ReadNext;

    public bool ShowOpen => Row.Action == ReadingHistoryAction.Open;

    public string? PlayTooltip { get; }
}

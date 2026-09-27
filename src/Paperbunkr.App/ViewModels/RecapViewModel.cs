using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Insights screen's "Recap" tab (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-
/// design.md) - a calendar-year-scoped, 9-slide "Wrapped"-style summary. Same shape as
/// <see cref="StatsScreenViewModel"/>: holds one <see cref="RecapSnapshot"/> per year for the session,
/// rebuilt on first view of a year and dropped whenever a reading event fires while the app is running.
/// All computation is in <see cref="RecapResolver"/>; this class is presentation glue + the session cache.
/// No <see cref="IFilePickerService"/> dependency here - exporting needs a live Avalonia control for
/// <c>RenderTargetBitmap</c>, which a view-model can't hold, so <c>InsightsScreen</c>'s code-behind owns
/// the export flow directly (same reasoning as <see cref="StatsScreenViewModel.ChartsChanged"/> existing
/// because ScottPlot rendering can't be data-bound either).
/// </summary>
public partial class RecapViewModel : ViewModelBase
{
    private readonly Func<DateTime> _nowUtc;
    private readonly Dictionary<int, RecapSnapshot> _cache = new();
    private bool _yearInitialized;
    private bool _isRefreshing;

    public RecapViewModel(IReadingEventRecorder? readingEventRecorder = null, Func<DateTime>? nowUtc = null)
    {
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        if (readingEventRecorder is not null)
        {
            readingEventRecorder.ReadingEventRecorded += () =>
            {
                _cache.Clear();
                if (IsActive)
                {
                    Refresh();
                }
            };
        }
    }

    /// <summary>Set by the shell while the Recap tab is the visible tab.</summary>
    public bool IsActive { get; set; }

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private RecapSnapshot? _snapshot;

    public IReadOnlyList<YearOption> YearOptions { get; private set; } = Array.Empty<YearOption>();

    public IReadOnlyList<RecapTile> Tiles { get; private set; } = Array.Empty<RecapTile>();

    /// <summary>Named per-tile accessors (docs/superpowers/specs/2026-09-23-insights-redesign-design.md's
    /// Recap layout revision) for the grid layout's fixed positions - replaces the old slide-by-slide
    /// paging (<c>CurrentSlideIndex</c>/dots/prev-next), which this all-tiles-visible-at-once layout has
    /// no use for. Index order matches <see cref="BuildTiles"/>'s fixed 9-tile arc.</summary>
    public RecapTile ItemsFinishedTile => TileAt(0);

    public RecapTile PagesReadTile => TileAt(1);

    public RecapTile LongestStreakTile => TileAt(2);

    public RecapTile BusiestDayTile => TileAt(3);

    public RecapTile TopSeriesTile => TileAt(4);

    public RecapTile TopWriterTile => TileAt(5);

    public RecapTile TopArtistTile => TileAt(6);

    public RecapTile HighestRatedTile => TileAt(7);

    public RecapTile MostRereadTile => TileAt(8);

    private RecapTile TileAt(int index) => index < Tiles.Count ? Tiles[index] : new RecapTile(string.Empty, string.Empty);

    partial void OnSelectedYearChanged(int value)
    {
        if (!_isRefreshing)
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void SetSelectedYear(int value) => SelectedYear = value;

    public void Refresh()
    {
        _isRefreshing = true;
        try
        {
            var availableYears = RecapResolver.AvailableYears(PaperbunkrDb.CreateContext(), _nowUtc());
            int currentYear = _nowUtc().ToLocalTime().Year;

            if (!_yearInitialized)
            {
                _yearInitialized = true;
                SelectedYear = availableYears.Count > 0 ? availableYears[0] : currentYear;
            }

            YearOptions = availableYears.Select(y => new YearOption(y, y == currentYear ? $"{y} (so far)" : y.ToString())
            {
                IsActive = y == SelectedYear,
            }).ToList();
            OnPropertyChanged(nameof(YearOptions));

            RefreshSnapshotForSelectedYear();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshSnapshotForSelectedYear()
    {
        if (!_cache.TryGetValue(SelectedYear, out var snap))
        {
            using var context = PaperbunkrDb.CreateContext();
            snap = RecapResolver.Build(context, SelectedYear, _nowUtc());
            _cache[SelectedYear] = snap;
        }

        Snapshot = snap;
        using (var coverContext = PaperbunkrDb.CreateContext())
        {
            Tiles = BuildTiles(snap, coverContext);
        }

        OnPropertyChanged(nameof(Tiles));
        foreach (var name in new[]
        {
            nameof(ItemsFinishedTile), nameof(PagesReadTile), nameof(LongestStreakTile), nameof(BusiestDayTile),
            nameof(TopSeriesTile), nameof(TopWriterTile), nameof(TopArtistTile), nameof(HighestRatedTile), nameof(MostRereadTile),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>The fixed 9-tile narrative order (design doc's "broad totals → habits → favorites →
    /// superlatives" arc). Shared by the in-app grid layout (via the named <c>*Tile</c> accessors above)
    /// and <c>RecapPosterView</c> (tile 0 as the headline, the rest in its grid) so formatting lives in
    /// exactly one place. Takes a live <paramref name="context"/> only to resolve cover art (docs/
    /// superpowers/specs/2026-09-23-insights-redesign-design.md) for the three tiles that name a
    /// coverable item - <see cref="RecapResolver"/> itself stays untouched otherwise, it just now also
    /// hands back the series/issue id a <see cref="HighlightGroup"/> came from.</summary>
    private static IReadOnlyList<RecapTile> BuildTiles(RecapSnapshot snap, PaperbunkrDbContext context)
    {
        string? topSeriesCover = ResolveSeriesCoverKey(context, snap.TopSeries?.SeriesId);
        string? highestRatedCover = ResolveSeriesCoverKey(context, snap.HighestRatedSeries?.SeriesId);
        string? mostRereadCover = ResolveIssueCoverKey(context, snap.MostRereadItem?.IssueId);

        return new[]
        {
            new RecapTile("ITEMS FINISHED", snap.ItemsFinished.ToString("N0")),
            new RecapTile("PAGES READ", snap.PagesRead.ToString("N0")),
            new RecapTile("LONGEST STREAK", snap.LongestStreakDays == 1 ? "1 day" : $"{snap.LongestStreakDays} days"),
            new RecapTile("BUSIEST DAY", snap.BusiestDay is { } d
                ? $"{d.Date:MMM d} · {d.Pages:N0} pages"
                : "Not enough history yet"),
            new RecapTile("TOP SERIES", snap.TopSeries?.DisplayTitle ?? "No series finished this year", topSeriesCover),
            new RecapTile("TOP WRITER", snap.TopWriter?.DisplayTitle ?? "No writer info recorded"),
            new RecapTile("TOP ARTIST", snap.TopArtist?.DisplayTitle ?? "No artist info recorded"),
            new RecapTile("HIGHEST RATED", snap.HighestRatedSeries?.DisplayTitle ?? "No rated series this year", highestRatedCover),
            new RecapTile("MOST REREAD", snap.MostRereadItem?.DisplayTitle ?? "No rereads this year", mostRereadCover),
        };
    }

    private static string? ResolveSeriesCoverKey(PaperbunkrDbContext context, int? seriesId)
    {
        if (seriesId is not { } id)
        {
            return null;
        }

        var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == id);
        return series is null ? null : SeriesCardSample.FromSeries(series).CoverKey;
    }

    private static string? ResolveIssueCoverKey(PaperbunkrDbContext context, int? issueId)
    {
        if (issueId is not { } id)
        {
            return null;
        }

        var issue = context.Issues.Find(id);
        return issue is null ? null : CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize);
    }
}

/// <summary>One Recap slide/poster-cell's label + formatted value (docs/superpowers/specs/2026-09-23-
/// insights-year-in-review-recap-design.md). <see cref="Value"/> is already the tile's own empty-state
/// message when the underlying <see cref="RecapSnapshot"/> field is null - the view never needs to branch
/// on nullability itself. <see cref="CoverKey"/> (docs/superpowers/specs/2026-09-23-insights-redesign-
/// design.md) is non-null only for the three tiles that name a coverable item (Top Series, Highest
/// Rated, Most Reread).</summary>
public sealed record RecapTile(string Label, string Value, string? CoverKey = null);

/// <summary>DataContext for <c>RecapPosterView</c> - split from the same 9-<see cref="RecapTile"/> list
/// <see cref="RecapViewModel"/> already built for the in-app slides (<see cref="Headline"/> is tile 0,
/// <see cref="GridTiles"/> the remaining 8) so the poster's XAML never needs a list-skipping converter,
/// plus the heading fields the poster shows that the slide viewer doesn't need.</summary>
public sealed record RecapPosterDisplayModel(int Year, bool IsCurrentYear, RecapTile Headline, IReadOnlyList<RecapTile> GridTiles)
{
    public static RecapPosterDisplayModel From(int year, bool isCurrentYear, IReadOnlyList<RecapTile> tiles)
        => new(year, isCurrentYear, tiles[0], tiles.Skip(1).ToList());
}

/// <summary>One entry in the Recap tab's year picker - same shape as <see cref="RangeOption"/> on
/// <see cref="StatsScreenViewModel"/>. <see cref="Label"/> is "{year} (so far)" for the current year,
/// otherwise the plain year.</summary>
public sealed partial class YearOption : ObservableObject
{
    public YearOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

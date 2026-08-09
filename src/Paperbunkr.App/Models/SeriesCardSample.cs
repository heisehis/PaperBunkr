using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Library grid card. Originally sample data mirroring the "covers" array from the "Paperbunkr
/// App" Claude Design wireframe (project 43c40b25); now also buildable from a real
/// <see cref="Series"/> record (docs/onboarding.md §5-6) via <see cref="FromSeries"/>.
/// </summary>
public sealed class SeriesCardSample
{
    public const double PanoramaHeight = 146;
    public const double PanoramaMinWidth = 110;
    public const double PanoramaMaxWidth = 320;
    public const double DefaultCoverAspectRatio = 2.0 / 3.0; // standard portrait comic cover

    public int SeriesId { get; init; }
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Sub { get; init; }
    public string? Publisher { get; init; }
    public required string ContentTypeLabel { get; init; }
    public int IssueCount { get; init; }
    public int UnreadCount { get; init; }
    public bool HasUnread => UnreadCount > 0;
    public bool Missing { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Series-level sort aggregates (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase C) - computed once here, not recomputed per sort click.</summary>
    public DateTime? LastAddedTime { get; init; }
    public DateTime? LastOpenedTime { get; init; }
    public long TotalFileSize { get; init; }

    /// <summary>Cover issue's <c>LanguageISO</c> (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase D overlay toggle) - raw ISO code, e.g. "en"/"ja".</summary>
    public string? LanguageIso { get; init; }

    /// <summary>First unread issue in reading order, or <see langword="null"/> if every issue is read - backs the Continue Reading overlay button.</summary>
    public int? ContinueReadingIssueId { get; init; }

    public bool HasContinueReading => ContinueReadingIssueId is not null;
    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);
    public bool HasLanguage => !string.IsNullOrWhiteSpace(LanguageIso);

    /// <summary>
    /// Panorama grid's per-series tile width (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase A) - computed from the real cover bitmap's
    /// aspect ratio at a fixed <see cref="PanoramaHeight"/>, clamped so no cover renders absurdly
    /// thin or wide. Landscape covers render wide, portrait covers render narrow, side by side in
    /// the same row - not a single fixed crop box. Falls back to a standard portrait ratio before
    /// a real cover's been generated; re-tunes automatically once one exists.
    /// </summary>
    public double PanoramaWidth { get; init; }

    /// <summary>
    /// Real decoded cover art (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §3),
    /// null until "Generate Covers" has processed this series' cover issue. <see cref="CoverBrush"/>
    /// is the fallback the UI shows underneath while this is null.
    /// </summary>
    public Bitmap? CoverImage { get; init; }

    public static IBrush Gradient(string fromHex, string toHex) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(fromHex), 0),
            new GradientStop(Color.Parse(toHex), 1),
        },
    };

    // Same palette used throughout the wireframe's own sample covers - picked deterministically
    // per series (by name hash) since there's no real cover-art decode pipeline yet (that's the
    // reader canvas work in docs/onboarding.md §8), just to keep the grid visually varied.
    private static readonly (string From, string To)[] s_palette =
    {
        ("#3a2f45", "#8a4a2e"),
        ("#1e3a3f", "#2f7d6a"),
        ("#442a1c", "#c9803f"),
        ("#26313f", "#4a6b8a"),
        ("#3f2130", "#a34a5c"),
        ("#1f2a1c", "#5c8a4a"),
        ("#2a2333", "#6a5ca3"),
        ("#332118", "#8a5a2e"),
    };

    // string.GetHashCode() is randomized per process in .NET Core - not stable across app
    // restarts, which would make the "same series, same color" property this palette pick
    // relies on flip every launch. FNV-1a is a plain, stable, non-cryptographic hash.
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    /// <summary>The same deterministic per-series-name cover gradient <see cref="FromSeries"/> uses,
    /// exposed standalone for screens (e.g. Reader) that need just the color, not a full card.</summary>
    public static IBrush CoverBrushFor(string seriesName)
    {
        var (from, to) = s_palette[StableHash(seriesName) % (uint)s_palette.Length];
        return Gradient(from, to);
    }

    /// <summary>
    /// Pure clamp math backing <see cref="PanoramaWidth"/> (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase A), extracted for direct testing the same way
    /// <c>ZoomPanMath</c>/<c>GridKeyboardNavigation</c> already separate their pure geometry from
    /// the Avalonia-bitmap-touching caller.
    /// </summary>
    public static double ComputePanoramaWidth(double aspectRatio) =>
        Math.Clamp(aspectRatio * PanoramaHeight, PanoramaMinWidth, PanoramaMaxWidth);

    public static SeriesCardSample FromSeries(Series series)
    {
        int unreadCount = series.Issues.Count(i => i.LastPageRead is null or 0);

        var coverIssue = series.Issues.FirstOrDefault(i => i.Id == series.CoverIssueId)
            ?? series.Issues.OrderByNumber().FirstOrDefault();
        var coverImage = coverIssue is not null ? CoverImageCache.Get(coverIssue.Id) : null;

        double aspectRatio = coverImage is { } bmp && bmp.PixelSize.Height > 0
            ? (double)bmp.PixelSize.Width / bmp.PixelSize.Height
            : DefaultCoverAspectRatio;

        return new SeriesCardSample
        {
            SeriesId = series.Id,
            Title = series.Name.ToUpperInvariant(),
            Name = series.Name,
            Sub = $"{series.ContentType} · {series.Issues.Count} issues",
            Publisher = series.Publisher,
            ContentTypeLabel = series.ContentType.ToString(),
            IssueCount = series.Issues.Count,
            UnreadCount = unreadCount,
            Missing = series.Issues.Any(i => i.FileIsMissing),
            CoverBrush = CoverBrushFor(series.Name),
            CoverImage = coverImage,
            PanoramaWidth = ComputePanoramaWidth(aspectRatio),
            // LINQ's nullable Max() returns null for an all-null or empty sequence rather than throwing.
            LastAddedTime = series.Issues.Select(i => i.AddedTime).Max(),
            LastOpenedTime = series.Issues.Select(i => i.OpenedTime).Max(),
            TotalFileSize = series.Issues.Sum(i => i.FileSize ?? 0),
            LanguageIso = coverIssue?.LanguageISO,
            ContinueReadingIssueId = series.Issues.OrderByNumber().FirstOrDefault(i => i.LastPageRead is null or 0)?.Id,
        };
    }
}

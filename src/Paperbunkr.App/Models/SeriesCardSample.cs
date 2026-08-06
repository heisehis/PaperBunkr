using System.Linq;
using Avalonia;
using Avalonia.Media;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Library grid card. Originally sample data mirroring the "covers" array from the "Paperbunkr
/// App" Claude Design wireframe (project 43c40b25); now also buildable from a real
/// <see cref="Series"/> record (docs/onboarding.md §5-6) via <see cref="FromSeries"/>.
/// </summary>
public sealed class SeriesCardSample
{
    public int SeriesId { get; init; }
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Sub { get; init; }
    public int UnreadCount { get; init; }
    public bool HasUnread => UnreadCount > 0;
    public bool Missing { get; init; }
    public required IBrush CoverBrush { get; init; }

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

    public static SeriesCardSample FromSeries(Series series)
    {
        int unreadCount = series.Issues.Count(i => i.LastPageRead is null or 0);

        return new SeriesCardSample
        {
            SeriesId = series.Id,
            Title = series.Name.ToUpperInvariant(),
            Name = series.Name,
            Sub = $"{series.ContentType} · {series.Issues.Count} issues",
            UnreadCount = unreadCount,
            Missing = series.Issues.Any(i => i.FileIsMissing),
            CoverBrush = CoverBrushFor(series.Name),
        };
    }
}

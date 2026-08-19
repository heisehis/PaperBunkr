using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>Home screen's Spotlight card (docs/superpowers/specs/2026-08-18-home-screen-design.md
/// Module 4) - one issue, not a whole series, unlike <see cref="SeriesCardSample"/>.</summary>
public sealed class SpotlightIssueSample
{
    public int IssueId { get; init; }
    public int SeriesId { get; init; }
    public required string SeriesName { get; init; }
    public required string Title { get; init; }
    public required IBrush CoverBrush { get; init; }
    public Bitmap? CoverImage { get; init; }

    public static SpotlightIssueSample FromIssue(Issue issue)
    {
        string seriesName = issue.Series?.Name ?? string.Empty;
        return new SpotlightIssueSample
        {
            IssueId = issue.Id,
            SeriesId = issue.SeriesId,
            SeriesName = seriesName,
            // "#?" when even Number is missing - a bare "#" (found via an actual on-screen check,
            // not just reasoning about it) reads as broken, not "untitled". Same fallback
            // DetailTabsViewModel already uses for the identical gap.
            Title = issue.EffectiveTitle() ?? (string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}"),
            CoverBrush = SeriesCardSample.CoverBrushFor(seriesName),
            CoverImage = CoverImageCache.Get(issue.Id),
        };
    }
}

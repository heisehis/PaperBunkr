using System.Collections.Generic;
using Avalonia;
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

    /// <summary>"#12 · 24 pages · New" - real per-issue metadata, not decorative filler. "New" is
    /// always accurate here (not a guess): <see cref="HomeFeedResolver.GetSpotlightPicks"/> only ever
    /// selects from <see cref="IssueMetadataExtensions.IsUnread"/> issues, so every Spotlight pick
    /// genuinely is new-to-the-reader. Segments with no real data (no page count yet) are omitted
    /// rather than shown as "0 pages" or similar.</summary>
    public required string Meta { get; init; }

    /// <summary>Written blurb shown under the meta line on the Home spotlight hero (docs/superpowers/
    /// specs/2026-08-28-home-screen-redesign-design.md §3) - the issue's own <c>Summary</c>, or a
    /// short generated line when it has none.</summary>
    public required string Synopsis { get; init; }

    /// <summary>Pre-rendered blurred backdrop for the hero card (docs/superpowers/specs/
    /// 2026-08-24-home-screen-design.md) - same <see cref="BackdropBlurRenderer"/> technique
    /// MangaDetailScreenViewModel already uses for its own header, not a live Avalonia Effect
    /// (which only blurs a small square instead of filling the banner - a known Avalonia bug,
    /// AvaloniaUI/Avalonia#11416, already worked around once in this codebase). The hero shows this
    /// as pure atmosphere behind the real, undistorted <see cref="CoverImage"/> - the first attempt
    /// at this card stretched the raw cover art directly, which is why it looked broken.</summary>
    public Bitmap? BackdropImage { get; init; }

    public static SpotlightIssueSample FromIssue(Issue issue)
    {
        string seriesName = issue.Series?.Name ?? string.Empty;
        var coverImage = CoverImageCache.Get(issue.Id, issue.FilePath, issue.FileSize);

        string numberSegment = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}";
        string synopsis = !string.IsNullOrWhiteSpace(issue.Summary)
            ? issue.Summary!.Trim()
            : $"{(string.IsNullOrWhiteSpace(seriesName) ? "This issue" : seriesName)} {numberSegment} — pulled from your unread shelf. Start reading.";
        var metaSegments = new List<string> { numberSegment };
        if (issue.PageCount is > 0)
        {
            metaSegments.Add($"{issue.PageCount} pages");
        }
        metaSegments.Add("New");

        return new SpotlightIssueSample
        {
            IssueId = issue.Id,
            SeriesId = issue.SeriesId,
            SeriesName = seriesName,
            // "#?" when even Number is missing - a bare "#" (found via an actual on-screen check,
            // not just reasoning about it) reads as broken, not "untitled". Same fallback
            // DetailTabsViewModel already uses for the identical gap.
            Title = issue.EffectiveTitle() ?? numberSegment,
            CoverBrush = SeriesCardSample.CoverBrushFor(seriesName),
            CoverImage = coverImage,
            BackdropImage = coverImage is not null ? BackdropBlurRenderer.Render(coverImage, new PixelSize(1600, 360)) : null,
            Meta = string.Join(" · ", metaSegments),
            Synopsis = synopsis,
        };
    }
}

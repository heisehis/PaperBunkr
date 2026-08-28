using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Models;

/// <summary>
/// One issue tile in the Story Events screen's Timeline mode (docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4g-age-progression-design.md) - cover thumbnail, read/unread state, and
/// (for an issue placed in the disputed 1980-84 window) a reduced-confidence indicator carrying
/// its <see cref="ConfidenceReason"/>. Clicking opens the issue in the reader.
/// </summary>
public sealed class TimelineIssueCard
{
    public required int IssueId { get; init; }
    public required string Title { get; init; }
    public required string SeriesName { get; init; }
    public required bool IsUnread { get; init; }
    public required bool IsReducedConfidence { get; init; }
    public string? ConfidenceReason { get; init; }
    public required IBrush CoverBrush { get; init; }
    public Bitmap? CoverImage { get; init; }
    public string? YearLabel { get; init; }
}

using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sample data for an issue cover tile (Detail screen's Issues tab). An <see cref="ObservableObject"/>
/// (was a plain init-only POCO) since <see cref="IsSelected"/> (docs/superpowers/specs/
/// 2026-08-07-bulk-issue-editing-design.md §1) needs live change notification for the tile's
/// selected-border visual state.
/// </summary>
public sealed partial class IssueCardSample : ObservableObject, ISelectableCard
{
    public int Id { get; init; }

    /// <summary>
    /// Owning series id. Unused by Detail's own Issues tab (already scoped to a single series when
    /// it constructs these), but needed by the Smart Lists results grid (docs/superpowers/specs/
    /// 2026-08-09-smart-lists-results-view-design.md), whose results span many series.
    /// </summary>
    public int SeriesId { get; init; }

    /// <summary>Issue number label, e.g. "#5" (or "#?" when unnumbered).</summary>
    public required string Title { get; init; }
    public bool IsUnread { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Real decoded cover art, null until "Generate Covers" has processed this issue.</summary>
    public Bitmap? CoverImage { get; init; }

    /// <summary>
    /// Cover issue id for the lazy <c>views:AsyncCoverImage.SourceId</c> binding path - set instead
    /// of <see cref="CoverImage"/> where the grid can be large and virtualized (Smart Lists results:
    /// a list matching hundreds of issues would otherwise decode every JPEG synchronously on the UI
    /// thread while building these). Detail's own Issues tab keeps using <see cref="CoverImage"/> -
    /// it's always scoped to one series' worth of issues.
    /// </summary>
    public int? CoverIssueId { get; init; }

    /// <summary>Null/empty for a fileless placeholder entry (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md).</summary>
    public string? FilePath { get; init; }

    /// <summary>Drives "Show in Explorer"'s IsEnabled without a converter - see FilePath's own doc comment.</summary>
    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    // --- Detail-screen Issues-tab List / Card view-mode columns (docs/superpowers/specs/
    //     2026-08-28-detail-screens-streaming-redesign-design.md). Unused by the Smart Lists grid. ---

    /// <summary>The issue's own title (not the number) - Card view's headline.</summary>
    public string? FullTitle { get; init; }

    public string? ArcTitle { get; init; }

    public System.DateTime? CoverDate { get; init; }

    public float? Rating { get; init; }

    /// <summary>Fully read - dims the tile.</summary>
    public bool IsRead { get; init; }

    /// <summary>0..1 read progress - drives the thin in-progress bar.</summary>
    public double ReadFraction { get; init; }

    public bool IsInProgress => ReadFraction is > 0 and < 1;

    /// <summary>Read-state badge for the detail-screen tile (docs/superpowers/specs/2026-09-04-
    /// detail-screen-icons-and-glyphs-design.md §4).</summary>
    public IssueTileGlyph TileGlyph => IsRead ? IssueTileGlyph.Read : IsInProgress ? IssueTileGlyph.InProgress : IssueTileGlyph.None;

    public string CoverDateLabel => CoverDate?.ToString("MMM yyyy") ?? string.Empty;

    public string RatingLabel => Rating is > 0 ? new string('★', System.Math.Clamp((int)System.Math.Round(Rating.Value), 1, 5)) : "—";

    public string CardProgressLabel => IsInProgress ? $"{Title} · {(int)System.Math.Round(ReadFraction * 100)}%" : Title;

    public string CardActionLabel => IsInProgress ? "Continue" : IsRead ? "Re-read" : "Read";

    [ObservableProperty]
    private bool _isSelected;
}

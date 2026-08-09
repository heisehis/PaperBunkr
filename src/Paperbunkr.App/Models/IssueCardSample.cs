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
public sealed partial class IssueCardSample : ObservableObject
{
    public int Id { get; init; }

    /// <summary>
    /// Owning series id. Unused by Detail's own Issues tab (already scoped to a single series when
    /// it constructs these), but needed by the Smart Lists results grid (docs/superpowers/specs/
    /// 2026-08-09-smart-lists-results-view-design.md), whose results span many series.
    /// </summary>
    public int SeriesId { get; init; }

    public required string Title { get; init; }
    public bool IsUnread { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Real decoded cover art, null until "Generate Covers" has processed this issue.</summary>
    public Bitmap? CoverImage { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

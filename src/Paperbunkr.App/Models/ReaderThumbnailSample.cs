using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for a page thumbnail in the Reader screen's left rail.</summary>
public sealed class ReaderThumbnailSample
{
    public bool IsSelected { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Real decoded page thumbnail, null until the background generation pass reaches this page.</summary>
    public Bitmap? CoverImage { get; init; }

    /// <summary>Drives the corner ribbon indicator (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-and-bookmarks-design.md) - CE draws the same kind of marker on its own thumbnail rail.</summary>
    public bool IsBookmarked { get; init; }

    /// <summary>Per-page type tagging (docs/ce-feature-inventory.md §A) - null/<see cref="Paperbunkr.Data.Entities.PageType.Story"/> pages show no badge at all, matching the bookmark ribbon's "only show when set" precedent.</summary>
    public Paperbunkr.Data.Entities.PageType PageType { get; init; }

    public bool HasPageTypeBadge => PageType != Paperbunkr.Data.Entities.PageType.Story;

    /// <summary>Single-letter badge text - "C"/"A"/"D" for Cover/Advertisement/Deleted.</summary>
    public string PageTypeBadgeText => PageType switch
    {
        Paperbunkr.Data.Entities.PageType.Cover => "C",
        Paperbunkr.Data.Entities.PageType.Advertisement => "A",
        Paperbunkr.Data.Entities.PageType.Deleted => "D",
        _ => string.Empty,
    };

    /// <summary>Persisted per-page rotation override (docs/ce-feature-inventory.md §A) - drives a small rotated-corner indicator so a rotated page is visible in the rail, not just when you turn to it.</summary>
    public bool IsRotated { get; init; }
}

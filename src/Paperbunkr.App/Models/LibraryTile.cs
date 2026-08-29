using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Models;

/// <summary>Which entity a <see cref="LibraryTile"/> points at.</summary>
public enum LibraryTileKind
{
    Series,
    Issue,
    Book,
}

/// <summary>
/// One kind-agnostic tile for a mixed-membership Collection's grid (docs/superpowers/specs/
/// 2026-08-27-collections-design.md, step 9) - a Collection can hold Series, standalone Issue, and
/// Book rows together in one manual order, and the grid needs a single item type to render them.
/// Series/Issue tiles use the lazy <c>views:AsyncCoverImage.SourceId</c> path via
/// <see cref="CoverIssueId"/> (same as <see cref="IssueCardSample"/>); Book tiles have no comic
/// cover-issue concept, so they carry an already-resolved <see cref="CoverImage"/> instead (same
/// idiom <see cref="BookCardSample"/> uses) - a collection's member count is small and curated, not
/// a library-wide virtualized grid, so the synchronous decode this implies is cheap.
/// </summary>
public sealed class LibraryTile
{
    public required LibraryTileKind Kind { get; init; }

    /// <summary>SeriesId / IssueId / BookId depending on <see cref="Kind"/> - what <see cref="LibraryScreenViewModel.SelectCollectionMemberCommand"/> dispatches on.</summary>
    public required int TargetId { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public required IBrush CoverBrush { get; init; }

    /// <summary>Series (its resolved cover issue) / Issue tiles only - see the type doc comment.</summary>
    public int? CoverIssueId { get; init; }

    /// <summary>Book tiles only - see the type doc comment.</summary>
    public Bitmap? CoverImage { get; init; }
}

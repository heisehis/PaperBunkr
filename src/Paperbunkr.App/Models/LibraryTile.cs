using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Collections;

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

    /// <summary>Resolves one <see cref="CollectionMember"/> into a tile - shared by
    /// <see cref="LibraryScreenViewModel"/>'s mixed grid and <see cref="HomeCollectionCard"/>'s cover
    /// resolution, same factory-method convention as <see cref="SeriesCardSample.FromSeries"/>/
    /// <see cref="BookCardSample.FromBook"/>.</summary>
    public static LibraryTile FromMember(CollectionMember member) => member.Kind switch
    {
        CollectionMemberKind.Series when member.Series is { } series => new LibraryTile
        {
            Kind = LibraryTileKind.Series,
            TargetId = series.Id,
            Title = series.Name,
            Subtitle = $"{series.ContentType} · {series.Issues.Count} issues",
            CoverBrush = SeriesCardSample.CoverBrushFor(series.Name),
            CoverIssueId = (series.Issues.FirstOrDefault(i => i.Id == series.CoverIssueId) ?? series.Issues.OrderByNumber().FirstOrDefault())?.Id,
        },
        CollectionMemberKind.Issue when member.Issue is { } issue => new LibraryTile
        {
            Kind = LibraryTileKind.Issue,
            TargetId = issue.Id,
            Title = member.DisplayTitle,
            Subtitle = issue.Series?.Name,
            CoverBrush = SeriesCardSample.CoverBrushFor(member.DisplayTitle),
            CoverIssueId = issue.Id,
        },
        CollectionMemberKind.Book when member.Book is { } book => new LibraryTile
        {
            Kind = LibraryTileKind.Book,
            TargetId = book.Id,
            Title = book.Title,
            Subtitle = "Book",
            CoverBrush = SeriesCardSample.CoverBrushFor(book.Title),
            CoverImage = BookCoverImageCache.Get(book.Id),
        },
        // A member whose target row was deleted out from under it without the CollectionItem being
        // cleaned up (shouldn't happen - FK cascade handles that - but a display-layer fallback is
        // cheaper than letting a null-ref take the whole grid down).
        _ => new LibraryTile
        {
            Kind = member.Kind switch { CollectionMemberKind.Issue => LibraryTileKind.Issue, CollectionMemberKind.Book => LibraryTileKind.Book, _ => LibraryTileKind.Series },
            TargetId = member.TargetId,
            Title = member.DisplayTitle,
            CoverBrush = SeriesCardSample.CoverBrushFor(member.DisplayTitle),
        },
    };
}

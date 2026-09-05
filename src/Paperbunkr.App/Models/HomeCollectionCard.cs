using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Home screen's "Collections" shelf card (docs/superpowers/specs/2026-08-27-collections-design.md's
/// own deferred "Home-feed shelf" follow-on) - name, member count, and a single already-resolved
/// cover (unlike <see cref="LibraryTile"/>'s lazy <c>CoverIssueId</c> path: this shelf is capped at a
/// handful of cards, not a virtualized grid, so eagerly resolving via <see cref="CoverImageCache"/>
/// here - same as <see cref="LibraryTile.FromMember"/> already does for a Book member - is cheap and
/// lets <c>views:PosterTile.CoverSource</c> bind to one property instead of two mutually-exclusive
/// ones). Manual cover takes priority; otherwise the first member's own cover, same rule
/// <see cref="CollectionResolver.GetCoverHint"/> already documents.
/// </summary>
public sealed class HomeCollectionCard
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required int Count { get; init; }

    public string? AccentColor { get; init; }

    public required IBrush CoverBrush { get; init; }

    public Bitmap? CoverImage { get; init; }

    public static HomeCollectionCard FromCollection(Collection collection, CollectionCoverHint hint)
    {
        IBrush coverBrush = SeriesCardSample.CoverBrushFor(collection.Name);
        Bitmap? coverImage = null;

        if (hint.ManualPath is { } path && File.Exists(path))
        {
            try
            {
                coverImage = new Bitmap(path);
            }
            catch
            {
                coverImage = null;
            }
        }
        else if (hint.FirstMember is { } member)
        {
            var tile = LibraryTile.FromMember(member);
            coverBrush = tile.CoverBrush;
            coverImage = tile.CoverImage ?? (tile.CoverKey is string coverKey ? CoverImageCache.Get(coverKey) : null);
        }

        return new HomeCollectionCard
        {
            Id = collection.Id,
            Name = collection.Name,
            Count = collection.Items.Count,
            AccentColor = collection.AccentColor,
            CoverBrush = coverBrush,
            CoverImage = coverImage,
        };
    }
}

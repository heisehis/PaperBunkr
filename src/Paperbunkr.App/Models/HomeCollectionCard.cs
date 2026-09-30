using System.Collections.Generic;
using System.Linq;
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

    /// <summary>2x2 collage covers (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C6), exactly four entries, or null
    /// to show the single <see cref="CoverImage"/>. Same fill rule as reading lists (<see cref="ReadingListCoverMosaic"/>).</summary>
    public IReadOnlyList<IImage?>? MosaicCovers { get; init; }

    public bool HasMosaic => MosaicCovers is not null;

    public static HomeCollectionCard FromCollection(Collection collection, CollectionCoverHint hint, IReadOnlyList<CollectionMember>? members = null)
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
            MosaicCovers = hint.ManualPath is null && members is not null ? BuildMosaic(members) : null,
        };
    }

    /// <summary>A manual cover always wins (checked by the caller); otherwise the members' covers through the shared fill rule.
    /// Null when the rule says "single cover" (fewer than three distinct covers).</summary>
    private static IReadOnlyList<IImage?>? BuildMosaic(IReadOnlyList<CollectionMember> members)
    {
        var imagesByKey = new Dictionary<string, IImage?>();
        var keys = new List<string>();
        foreach (var member in members)
        {
            var tile = LibraryTile.FromMember(member);
            if (tile.CoverKey is not string key || imagesByKey.ContainsKey(key))
            {
                continue;
            }

            imagesByKey[key] = tile.CoverImage ?? CoverImageCache.Get(key);
            keys.Add(key);
            if (keys.Count == 4)
            {
                break;
            }
        }

        var picked = ReadingListCoverMosaic.PickCoverKeys(keys);
        return picked.Count == 4 ? picked.Select(k => imagesByKey[k]).ToList() : null;
    }
}

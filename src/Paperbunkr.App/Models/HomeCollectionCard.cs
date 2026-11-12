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

    /// <summary>Two or three covers for the Fan style (front card first), or null.</summary>
    public IReadOnlyList<IImage?>? FanCovers { get; init; }

    /// <summary>0..1 read progress for the opt-in corner ring; null hides it (setting off, or no units).</summary>
    public double? RingFraction { get; init; }

    /// <summary>The collection you read from most recently (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md): drawn as the larger hero card, first on the shelf.</summary>
    public bool IsHero { get; set; }

    /// <summary>The issue the hero's Continue chip opens; null when everything is read or there is no hero.</summary>
    public int? ContinueIssueId { get; set; }

    public string? ContinueLabel { get; set; }

    public bool HasContinue => ContinueIssueId is not null;

    public string? RingText { get; init; }

    public static HomeCollectionCard FromCollection(
        Collection collection,
        CollectionCoverHint hint,
        IReadOnlyList<CollectionMember>? members = null,
        CollectionProgress? progress = null)
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

        // A manual cover file always wins over the style (the caller passes no members then).
        var style = hint.ManualPath is null ? collection.CoverStyle : CollectionCoverStyle.Single;
        var (mosaic, fan) = BuildStyled(style, hint.FirstMember, members);

        return new HomeCollectionCard
        {
            Id = collection.Id,
            Name = collection.Name,
            Count = collection.Items.Count,
            AccentColor = collection.AccentColor,
            CoverBrush = coverBrush,
            CoverImage = coverImage,
            MosaicCovers = mosaic,
            FanCovers = fan,
            RingFraction = progress is { Total: > 0 } p ? p.Fraction : null,
            RingText = progress is { Total: > 0 } q ? $"{System.Math.Round(q.Fraction * 100):0}% read ({q.Read} of {q.Total})" : null,
        };
    }

    /// <summary>
    /// The mosaic / fan covers for a style. Distinct covers are gathered with the chosen (or first) member first, so it leads the stack. Auto and Mosaic
    /// use the shared fill rule (<see cref="ReadingListCoverMosaic"/>: three or more distinct covers); Fan needs two or more; Single and every
    /// fallback return (null, null) so the single cover shows.
    /// </summary>
    internal static (IReadOnlyList<IImage?>? Mosaic, IReadOnlyList<IImage?>? Fan) BuildStyled(
        CollectionCoverStyle style, CollectionMember? lead, IReadOnlyList<CollectionMember>? members)
    {
        if (style == CollectionCoverStyle.Single || members is null)
        {
            return (null, null);
        }

        var ordered = new List<CollectionMember>(members.Count);
        if (lead is not null)
        {
            ordered.Add(lead);
        }

        ordered.AddRange(members.Where(m => lead is null || !ReferenceEquals(m, lead)));

        var imagesByKey = new Dictionary<string, IImage?>();
        var keys = new List<string>();
        foreach (var member in ordered)
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

        if (style == CollectionCoverStyle.Fan)
        {
            return keys.Count >= 2 ? (null, keys.Take(3).Select(k => imagesByKey[k]).ToList()) : (null, null);
        }

        // Auto and Mosaic: the lead cover is kept first by the fill rule's input order.
        var picked = ReadingListCoverMosaic.PickCoverKeys(keys);
        return picked.Count == 4 ? (picked.Select(k => imagesByKey[k]).ToList(), null) : (null, null);
    }
}

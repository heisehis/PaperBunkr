using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>One collection a tile's series or issue belongs to: its accent (null = the generic accent) and its name.</summary>
public sealed record CollectionBadge(Color? Accent, string Name);

/// <summary>
/// What <c>TileCosmeticsOverlay</c> reads (from its DataContext, like <see cref="ITileProgressSource"/>) to draw the membership dots
/// (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md). The list is prebuilt once per Library load and shared, empty by default,
/// so a tile with no memberships or the setting off costs nothing.
/// </summary>
public interface ITileCollectionSource
{
    IReadOnlyList<CollectionBadge> CollectionBadges { get; }

    /// <summary>The collection names for the tile's tooltip, or null when there are none.</summary>
    string? CollectionBadgeTooltip { get; }
}

/// <summary>Shared helpers for the card samples.</summary>
public static class CollectionBadgeText
{
    public static string? Tooltip(IReadOnlyList<CollectionBadge> badges) =>
        badges.Count == 0 ? null : "In: " + string.Join(", ", badges.Select(b => b.Name));
}

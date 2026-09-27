using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// One bundled reader canvas background texture (docs/superpowers/specs/2026-09-10-reader-backlog-
/// batch-b-design.md Item 1) - an id (stored in <see cref="Data.Entities.AppSettings.BackgroundTexture"/>,
/// never a file path - CE stores a path, deliberate deviation), a display name for the Preferences
/// swatch row, and the bundled asset's <c>avares://</c> URI.
/// </summary>
public sealed record ReaderBackgroundTexture(string Id, string DisplayName, Uri AssetUri);

/// <summary>
/// Catalog + decoded-bitmap cache for the bundled reader background textures (docs/superpowers/
/// specs/2026-09-10-reader-backlog-batch-b-design.md Item 1; grown from 3 to 16 by docs/superpowers/
/// specs/2026-09-25-publisher-icons-and-reader-textures-design.md §B). The single source of truth for both
/// the Preferences swatch picker and <see cref="ViewModels.ReaderScreenViewModel"/>'s brush builder.
/// Bundled-only in v1 - no user file picker (a named deviation from CE, which also supports one).
/// </summary>
public static class ReaderBackgroundTextures
{
    private static ReaderBackgroundTexture Entry(string id, string name, string file) =>
        new(id, name, new Uri("avares://Paperbunkr.App/Assets/Textures/" + file));

    /// <summary>The 3 generated seamless textures first (<c>neutral-dark</c> stays the fallback),
    /// then CE's <c>Backgrounds</c> set. CE's <c>Black [S]</c> is deliberately absent - it is one
    /// spotlight vignette, not a tile (tiled it shows four hot spots) - and CE's <c>Papers</c> are
    /// its separate paper-overlay feature, still deferred.</summary>
    public static readonly IReadOnlyList<ReaderBackgroundTexture> All =
    [
        Entry("neutral-dark",    "Neutral dark",    "neutral-dark.png"),
        Entry("carbon",          "Carbon",          "carbon.png"),
        Entry("linen",           "Linen",           "linen.png"),
        Entry("brick-wall",      "Brick wall",      "BrickWall.jpg"),
        Entry("brushed-metal",   "Brushed metal",   "BrushedMetal.jpg"),
        Entry("brushed-metal-2", "Brushed metal 2", "BrushedMetal2.jpg"),
        Entry("ceramic",         "Ceramic",         "Ceramic.jpg"),
        Entry("ceramic-2",       "Ceramic 2",       "Ceramic2.jpg"),
        Entry("chalkboard",      "Chalkboard",      "ChalkBoard.jpg"),
        Entry("circles",         "Circles",         "Circles.jpg"),
        Entry("glass",           "Glass",           "Glass.jpg"),
        Entry("grass",           "Grass",           "Grass.jpg"),
        Entry("light-wood",      "Light wood",      "LightWood.jpg"),
        Entry("orange-metal",    "Orange metal",    "OrangeMetal.jpg"),
        Entry("plank-wood",      "Plank wood",      "PlankWood.jpg"),
        Entry("sketch",          "Sketch",          "Sketch.jpg"),
    ];

    /// <summary>Matches an id to its catalog entry; null, empty or unknown ids fall back to <see cref="All"/>'s first entry.</summary>
    public static ReaderBackgroundTexture Resolve(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All[0];

    private static readonly ConcurrentDictionary<string, Bitmap> BitmapCache = new();

    /// <summary>
    /// Decodes (or returns from cache) the bitmap for <paramref name="id"/> (or the fallback texture
    /// if unresolved). Bitmaps are decoded once and never mutated, so the cached instance is safe to
    /// share across brushes/threads - same reasoning <see cref="ViewModels.ReaderScreenViewModel"/>'s
    /// <c>DefaultCanvasBackgroundBrush</c> already documents for <c>ImmutableSolidColorBrush</c>.
    /// </summary>
    public static Bitmap LoadBitmap(string? id)
    {
        var texture = Resolve(id);
        return BitmapCache.GetOrAdd(texture.Id, _ =>
        {
            using var stream = AssetLoader.Open(texture.AssetUri);
            return new Bitmap(stream);
        });
    }
}

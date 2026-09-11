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
/// Catalog + decoded-bitmap cache for the 3 bundled reader background textures (docs/superpowers/
/// specs/2026-09-10-reader-backlog-batch-b-design.md Item 1). The single source of truth for both
/// the Preferences swatch picker and <see cref="ViewModels.ReaderScreenViewModel"/>'s brush builder.
/// Bundled-only in v1 - no user file picker (a named deviation from CE, which also supports one).
/// </summary>
public static class ReaderBackgroundTextures
{
    public static readonly IReadOnlyList<ReaderBackgroundTexture> All =
    [
        new("neutral-dark", "Neutral dark", new Uri("avares://Paperbunkr.App/Assets/Textures/neutral-dark.png")),
        new("carbon",       "Carbon",       new Uri("avares://Paperbunkr.App/Assets/Textures/carbon.png")),
        new("linen",        "Linen",        new Uri("avares://Paperbunkr.App/Assets/Textures/linen.png")),
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

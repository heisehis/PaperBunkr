using System;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Tells the garbage collector how much native memory a decoded <see cref="Bitmap"/> really holds (docs/superpowers/specs/
/// 2026-10-07-performance-and-memory-design.md §4.2). A bitmap is a small managed object around a large native pixel buffer, so
/// the collector sees almost nothing to gain from collecting one and lets unreferenced bitmaps sit until some later full
/// collection. The caches that hand a shared bitmap to long-lived view models cannot dispose on eviction (see
/// <see cref="LruCache{TKey,TValue}"/>), so this is what gets their dropped bitmaps reclaimed promptly: the pressure is
/// registered when the bitmap is tracked and withdrawn when the collector finalizes its companion token.
/// </summary>
internal static class NativeBitmapPressure
{
    private sealed class Token
    {
        private readonly long _bytes;

        public Token(long bytes)
        {
            _bytes = bytes;
            GC.AddMemoryPressure(bytes);
        }

        ~Token()
        {
            GC.RemoveMemoryPressure(_bytes);
        }
    }

    private static readonly ConditionalWeakTable<Bitmap, Token> s_tokens = new();

    /// <summary>Registers <paramref name="bitmap"/>'s pixel bytes once; tracking the same bitmap again does nothing.</summary>
    public static void Track(Bitmap bitmap)
    {
        long bytes = GridCoverCache.EstimateBytes(bitmap);
        if (bytes <= 0)
        {
            return;
        }

        s_tokens.GetValue(bitmap, _ => new Token(bytes));
    }
}

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;

namespace Paperbunkr.App.Services;

/// <summary>
/// Session cache of each cover's dominant colour for the bookshelf view (Cosmetics pitch #19). A spine asks for its colour by cover key; a miss decodes
/// the cover thumbnail on a pool thread (at most two at a time, so a fast scroll can't flood the pool), takes the first <see cref="CoverPalette"/> colour,
/// disposes the bitmap, and tells the asker when it is ready. The decoded bitmap is never kept: only a 4-byte colour per cover stays in memory.
/// </summary>
public static class SpineColorCache
{
    /// <summary>Prefix of a Books cover key (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §5).</summary>
    public const string BookKeyPrefix = "book:";

    private static readonly ConcurrentDictionary<string, Color?> s_colors = new();
    private static readonly ConcurrentDictionary<string, bool> s_inflight = new();
    private static readonly SemaphoreSlim s_gate = new(2);

    /// <summary>True when the colour for <paramref name="coverKey"/> is known; <paramref name="color"/> is null for a cover with no usable colour.</summary>
    public static bool TryGet(string coverKey, out Color? color) => s_colors.TryGetValue(coverKey, out color);

    /// <summary>Starts (once per key) extracting the colour; <paramref name="onReady"/> runs on the UI thread when it is known.</summary>
    public static void Request(string coverKey, Action onReady)
    {
        if (s_colors.ContainsKey(coverKey) || !s_inflight.TryAdd(coverKey, true))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            Color? result = null;
            await s_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // A book's key is "book:{id}" so it never reads the comic cover that happens to share its number.
                using var bitmap = coverKey.StartsWith(BookKeyPrefix, StringComparison.Ordinal)
                    ? BookCoverImageCache.DecodeFromDisk(coverKey[BookKeyPrefix.Length..])
                    : CoverImageCache.DecodeFromDisk(coverKey);
                var palette = CoverPalette.FromBitmap(bitmap);
                result = palette.Count > 0 ? palette.First() : null;
            }
            catch (Exception)
            {
                // No usable cover: the spine keeps its neutral look.
            }
            finally
            {
                s_gate.Release();
            }

            s_colors[coverKey] = result;
            s_inflight.TryRemove(coverKey, out _);
            Dispatcher.UIThread.Post(onReady, DispatcherPriority.Background);
        });
    }

    /// <summary>Forget everything (a library rebuild reassigns cover keys).</summary>
    public static void Clear() => s_colors.Clear();
}

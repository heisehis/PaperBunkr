using System.IO;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Decodes a cover thumbnail at a display-size bucket and stores it in <see cref="GridCoverCache"/>
/// (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §3.1). Runs on
/// <see cref="CoverDecodeQueue"/> worker threads; touches no UI state.
/// </summary>
internal static class GridCoverDecoder
{
    public static Bitmap? DecodeAndCache(string stem, int bucket)
    {
        if (GridCoverCache.Shared.TryGet(stem, bucket, out var cached))
        {
            return cached;
        }

        if (CoverPipelineStats.SimulatedDecodeDelayMs > 0)
        {
            System.Threading.Thread.Sleep(CoverPipelineStats.SimulatedDecodeDelayMs);
        }

        string path = CoverImageCache.ResolveCoverFile(stem);
        if (path.Length == 0)
        {
            return null;
        }

        try
        {
            // DecodeToWidth scales while decoding (a fraction of a full decode's time and memory) and keeps the aspect ratio.
            using var stream = File.OpenRead(path);
            var decoded = Bitmap.DecodeToWidth(stream, bucket, BitmapInterpolationMode.HighQuality);
            var stored = GridCoverCache.Shared.Add(stem, bucket, decoded);
            if (!ReferenceEquals(stored, decoded))
            {
                // Another worker cached this cover first: nobody else has seen ours, so free it now.
                decoded.Dispose();
            }

            Performance.MemoryPressureTrimmer.Shared.Check();
            return stored;
        }
        catch
        {
            return null;
        }
    }
}

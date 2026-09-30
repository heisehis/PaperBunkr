using Paperbunkr.ShellThumbnails.Interop;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// File type → cover → scaled BGRA pixels. Returns null whenever there's nothing to show (no image in the
/// file, encrypted/DRM, damaged, unsupported codec, out of time) - Windows then keeps its normal icon
/// (design decisions 9, 14, 15). Never throws for a bad file; only programming errors escape.
/// </summary>
internal static class ThumbnailRenderer
{
    /// <summary>The extensions this handler draws, as the design lists them (decision 2, 11).</summary>
    public static readonly IReadOnlyList<string> Extensions = new[]
    {
        ".cbz", ".cbr", ".cb7", ".cbt", ".pdf", ".djvu", ".epub", ".mobi", ".azw", ".azw3",
    };

    public static BgraImage? Render(Stream source, string extension, int maxSide, Deadline deadline)
    {
        try
        {
            var stream = new DeadlineStream(source, deadline);
            switch (extension.ToLowerInvariant())
            {
                case ".cbz" or ".cbr" or ".cb7" or ".cbt":
                    return Decode(ArchiveCoverFinder.FindCover(stream), maxSide);
                case ".epub":
                    return Decode(EpubCoverFinder.FindCover(stream), maxSide);
                case ".mobi" or ".azw" or ".azw3":
                    return Decode(MobiCoverFinder.FindCover(stream), maxSide);
                case ".pdf":
                    return Pdfium.RenderFirstPage(stream, maxSide);
                case ".djvu":
                    return DjvuRenderer.RenderFirstPage(stream, maxSide, deadline);
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            HandlerTrace.Write($"{extension}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static BgraImage? Decode(byte[]? encoded, int maxSide) =>
        encoded is { Length: > 0 } ? Wic.DecodeScaled(encoded, maxSide) : null;
}

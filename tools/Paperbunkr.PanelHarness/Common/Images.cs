using SkiaSharp;

namespace Paperbunkr.PanelHarness.Common;

internal static class Images
{
    public static bool IsImage(string name) => name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    /// <summary>The image files directly inside <paramref name="dir"/>, sorted by name.</summary>
    public static List<string> InFolder(string dir) =>
        Directory.EnumerateFiles(dir).Where(f => IsImage(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

    public static SKBitmap? Load(string path)
    {
        using var stream = File.OpenRead(path);
        return SKBitmap.Decode(stream);
    }
}

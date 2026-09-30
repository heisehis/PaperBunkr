using cYo.Common.Text;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// The library's own "which entry is page 0" rule, restated for archives read through SharpCompress
/// (docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md, decision 5): the engine's image
/// whitelist and <c>__MACOSX</c>/<c>.DS_Store</c> exclusion (<c>ComicProvider.IsSupportedImage</c>), then
/// <see cref="ExtendedStringComparer"/> case-insensitive natural sort on the full entry path
/// (<c>ArchiveComicProvider.OnParse</c>). Entry names are normalised to <c>\</c> first, because the engine's
/// 7-Zip accessor reports Windows separators and SharpCompress reports <c>/</c> - the sort compares the
/// separator character like any other.
/// </summary>
internal static class CoverImageRules
{
    // Same list, same order as ComicProvider.supportedTypes. "djvu" pages inside an archive are kept so the
    // chosen index matches the engine even though they can't be decoded here (that archive gets no thumbnail).
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".jif", ".jiff", ".gif", ".png", ".tif", ".tiff", ".bmp", ".djvu",
        ".webp", ".heic", ".heif", ".avif", ".jp2", ".j2k", ".jxl",
    };

    private static readonly string[] IgnoredFolders = { ".DS_Store\\", "__MACOSX\\" };

    public static string Normalize(string entryName) => entryName.Replace('/', '\\');

    public static bool IsSupportedImage(string entryName)
    {
        string name = Normalize(entryName);
        if (IgnoredFolders.Any(name.Contains))
        {
            return false;
        }

        string ext = Path.GetExtension(name);
        return SupportedExtensions.Any(e => string.Equals(ext, e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The entry the library would show as the cover, or null when there's no image entry.</summary>
    public static string? PickCover(IEnumerable<string> entryNames)
    {
        string? best = null;
        foreach (string entry in entryNames)
        {
            if (!IsSupportedImage(entry))
            {
                continue;
            }

            if (best is null || ExtendedStringComparer.Compare(Normalize(entry), Normalize(best), ExtendedStringComparison.IgnoreCase) < 0)
            {
                best = entry;
            }
        }

        return best;
    }
}

using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Naming;

/// <summary>
/// CE's exact filename/path-sanitization replace-map (design doc §5, `losettings.py:68`,
/// `lobookmover.py:1965-1970`, grilling Q5=A) plus its whitespace-collapse rule and dot stripping
/// (trailing on every segment here; both ends on folder segments, as the plugin does for folders). Applied per path segment (folder name or file name), not to a whole path string at once, so
/// a legitimate path separator in the *template's own* literal text isn't touched.
/// </summary>
public static partial class Sanitizer
{
    // Order matters for a couple of entries below only in that longest-first avoids partial
    // double-replacement concerns for a naive scan - here every key is a single char, so a simple
    // dictionary walk is sufficient and matches CE's own per-character replace loop.
    private static readonly IReadOnlyDictionary<char, string> ReplaceMap = new Dictionary<char, string>
    {
        ['?'] = string.Empty,
        ['/'] = string.Empty,
        ['\\'] = string.Empty,
        ['*'] = string.Empty,
        [':'] = " - ",
        ['<'] = "[",
        ['>'] = "]",
        ['|'] = "!",
        ['"'] = "'",
    };

    [GeneratedRegex(@"\s\s+")]
    private static partial Regex MultipleSpacesRegex();

    /// <summary>Sanitizes one path segment: character replace-map, trailing-period strip ("Fix for
    /// illegal periods at the end of folder names" - CE's own comment, verified), whitespace
    /// collapse, then an overall trim.</summary>
    public static string SanitizeSegment(string segment)
    {
        var builder = new System.Text.StringBuilder(segment.Length);
        foreach (char c in segment)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            builder.Append(ReplaceMap.TryGetValue(c, out string? replacement) ? replacement : c.ToString());
        }

        string collapsed = MultipleSpacesRegex().Replace(builder.ToString(), " ");
        return collapsed.Trim().TrimEnd('.');
    }

    /// <summary>A folder segment: <see cref="SanitizeSegment"/>, then leading and trailing dots and spaces are
    /// stripped too (Library Organizer 2.1.13 strips both ends of folder names only - `lobookmover.py:1281`; a
    /// leading dot would hide the folder, and `.`/`..` must never survive as a path segment).</summary>
    public static string SanitizeFolderSegment(string segment) => SanitizeSegment(segment).Trim('.', ' ');

    /// <summary>Sanitizes every segment of a relative path independently, rejoining with
    /// <see cref="Path.DirectorySeparatorChar"/> - the entry point <c>LibraryOrganizerService</c> uses
    /// once a template evaluates to a relative folder path. Empty segments (a missing publisher or imprint
    /// leaves the template's own literal separators behind) are dropped: kept, they made the path rooted
    /// (a leading backslash), which makes <c>Path.Combine</c> discard the base folder, or produced a doubled separator.
    /// The plugin gets the same result by joining an empty "empty folder" name (`lobookmover.py:1274-1282`).</summary>
    public static string SanitizePath(string relativePath, string emptyFolder = "")
    {
        string[] rawSegments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        IEnumerable<string> sanitized = rawSegments
            .Select(segment => SanitizeFolderSegment(string.IsNullOrWhiteSpace(segment) ? emptyFolder : segment))
            .Where(segment => segment.Length > 0);
        return string.Join(Path.DirectorySeparatorChar, sanitized);
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>True for a name Windows reserves for a device ("CON", "nul.txt" - the part before the first dot decides). A file or
    /// folder called this cannot be created on Windows, and in a library shared between machines it should not exist anywhere.</summary>
    public static bool IsReservedDeviceName(string segment) => ReservedDeviceNames.Contains(segment.Split('.')[0].Trim());
}

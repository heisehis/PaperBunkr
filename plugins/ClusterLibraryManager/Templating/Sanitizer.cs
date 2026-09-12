using System.Text.RegularExpressions;

namespace ClusterLibraryManager.Templating;

/// <summary>
/// CE's exact filename/path-sanitization replace-map (design doc §5, `losettings.py:68`,
/// `lobookmover.py:1965-1970`, grilling Q5=A) plus its trailing-period-strip and whitespace-collapse
/// rules. Applied per path segment (folder name or file name), not to a whole path string at once, so
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
            builder.Append(ReplaceMap.TryGetValue(c, out string? replacement) ? replacement : c.ToString());
        }

        string collapsed = MultipleSpacesRegex().Replace(builder.ToString(), " ");
        return collapsed.Trim().TrimEnd('.');
    }

    /// <summary>Sanitizes every segment of a relative path independently, rejoining with
    /// <see cref="Path.DirectorySeparatorChar"/> - the entry point <c>LibraryOrganizerService</c> uses
    /// once a template evaluates to a full relative folder+file path.</summary>
    public static string SanitizePath(string relativePath)
    {
        string[] rawSegments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        IEnumerable<string> sanitized = rawSegments.Select(SanitizeSegment);
        return string.Join(Path.DirectorySeparatorChar, sanitized);
    }
}

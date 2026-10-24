using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>Reads and writes <see cref="Entities.Series.LockedFields"/>, the comma-separated field keys a user set by hand and providers may not overwrite.</summary>
public static class MangaFieldLocks
{
    public const string SummaryKey = "Summary";
    public const string StatusKey = "Status";

    public static HashSet<string> Parse(string? csv) =>
        new((csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);

    public static string? Join(IEnumerable<string> keys)
    {
        string joined = string.Join(",", keys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal));
        return joined.Length == 0 ? null : joined;
    }

    public static bool IsLocked(Entities.Series series, string key) => Parse(series.LockedFields).Contains(key);
}

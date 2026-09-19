using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.LibrarySearch;

/// <summary>
/// Pre-normalized search text for the Library's in-memory snapshot (docs/superpowers/specs/
/// 2026-09-19-library-search-perf-design.md §2). Built once per data load from the same
/// <see cref="SearchFieldBundleCatalog"/> field lists <c>MatchesSearch</c> always used, so
/// matching is unchanged: case-insensitive <b>substring</b> per field, never across a field
/// boundary, never a token index.
///
/// Every field of a bundle is upper-cased once (<see cref="string.ToUpperInvariant"/>) and joined
/// with <see cref="Separator"/> (U+001F, which a user cannot type into the search box), so a query
/// cannot match across two fields. The query is normalized the same way once per keystroke and the
/// scan is <see cref="StringComparison.Ordinal"/>. Upper-case rather than lower-case on purpose:
/// <see cref="StringComparison.OrdinalIgnoreCase"/> is defined by invariant <i>upper</i>-casing, and
/// lower-casing diverges from it on a few characters (U+212A KELVIN SIGN lower-cases to <c>k</c> but
/// is not equal to <c>k</c> under OrdinalIgnoreCase), which would silently break CE parity.
///
/// The instance is immutable after <see cref="Build"/> - safe to read from any thread. It is never
/// updated in place: every data mutation already reloads the snapshot, which builds a new index.
/// </summary>
internal sealed class LibrarySearchIndex
{
    /// <summary>Field separator inside one bundle string. U+001F (unit separator) cannot be typed into a TextBox.</summary>
    internal const char Separator = '';

    private static readonly int s_modeCount = Enum.GetValues<SearchMode>().Length;

    // Indexed by (int)SearchMode. A null/absent entry means "no text for this mode".
    private readonly Dictionary<int, string>[] _issueText;
    private readonly Dictionary<int, string>[] _seriesText;

    // series id -> ids of the issues it owns, for the series-granularity "any issue matches" rule.
    private readonly Dictionary<int, int[]> _issueIdsBySeries;

    private LibrarySearchIndex(
        Dictionary<int, string>[] issueText,
        Dictionary<int, string>[] seriesText,
        Dictionary<int, int[]> issueIdsBySeries)
    {
        _issueText = issueText;
        _seriesText = seriesText;
        _issueIdsBySeries = issueIdsBySeries;
    }

    /// <summary>The normalization applied to both haystack text and the query.</summary>
    public static string Normalize(string text) => text.ToUpperInvariant();

    public static LibrarySearchIndex Build(IReadOnlyList<Series> allSeries, CancellationToken cancellationToken = default)
    {
        var issueText = new Dictionary<int, string>[s_modeCount];
        var seriesText = new Dictionary<int, string>[s_modeCount];
        for (int m = 0; m < s_modeCount; m++)
        {
            issueText[m] = new Dictionary<int, string>();
            seriesText[m] = new Dictionary<int, string>();
        }

        var issueIdsBySeries = new Dictionary<int, int[]>(allSeries.Count);
        var builder = new StringBuilder(256);
        var modes = Enum.GetValues<SearchMode>();

        foreach (var series in allSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Series-level fields, mirroring the hand-written half of the old MatchesSearch:
            // Series mode = name + alternate titles, All mode additionally publisher + genre. The
            // other modes have no series-level field.
            string seriesModeText = SeriesLevelText(series, includePublisherAndGenre: false, builder);
            if (seriesModeText.Length > 0)
            {
                seriesText[(int)SearchMode.Series][series.Id] = seriesModeText;
            }

            string allModeText = SeriesLevelText(series, includePublisherAndGenre: true, builder);
            if (allModeText.Length > 0)
            {
                seriesText[(int)SearchMode.All][series.Id] = allModeText;
            }

            var ids = new int[series.Issues.Count];
            int position = 0;
            foreach (var issue in series.Issues)
            {
                ids[position++] = issue.Id;
                foreach (var mode in modes)
                {
                    string text = JoinBundle(SearchFieldBundleCatalog.IssueFieldSelectors[mode](issue), builder);
                    if (text.Length > 0)
                    {
                        issueText[(int)mode][issue.Id] = text;
                    }
                }
            }

            issueIdsBySeries[series.Id] = ids;
        }

        return new LibrarySearchIndex(issueText, seriesText, issueIdsBySeries);
    }

    private static string SeriesLevelText(Series series, bool includePublisherAndGenre, StringBuilder builder)
    {
        builder.Clear();
        Append(builder, series.Name);
        foreach (var title in series.Titles)
        {
            Append(builder, title.Value);
        }

        if (includePublisherAndGenre)
        {
            Append(builder, series.Publisher);
            Append(builder, series.Genre);
        }

        return builder.ToString();
    }

    private static string JoinBundle(IEnumerable<string?> fields, StringBuilder builder)
    {
        builder.Clear();
        foreach (string? field in fields)
        {
            Append(builder, field);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(Separator);
        }

        builder.Append(Normalize(field));
    }

    /// <summary>True when a <b>series-level</b> field of <paramref name="seriesId"/> contains the
    /// (already <see cref="Normalize"/>d) query under <paramref name="mode"/>. Modes without
    /// series-level fields (Writer, Artists, Descriptive, File, Catalog) never match here.</summary>
    public bool SeriesLevelMatches(int seriesId, SearchMode mode, string normalizedQuery) =>
        _seriesText[(int)ResolveMode(mode)].TryGetValue(seriesId, out string? text)
        && text.Contains(normalizedQuery, StringComparison.Ordinal);

    /// <summary>True when the issue's own per-mode bundle contains the normalized query.</summary>
    public bool IssueMatches(int issueId, SearchMode mode, string normalizedQuery) =>
        _issueText[(int)ResolveMode(mode)].TryGetValue(issueId, out string? text)
        && text.Contains(normalizedQuery, StringComparison.Ordinal);

    /// <summary>Series-granularity rule (unchanged from the old <c>MatchesSearch</c>): a series matches
    /// when its own series-level fields match or <b>any</b> of its issues matches.</summary>
    public bool SeriesMatches(int seriesId, SearchMode mode, string normalizedQuery)
    {
        if (SeriesLevelMatches(seriesId, mode, normalizedQuery))
        {
            return true;
        }

        if (!_issueIdsBySeries.TryGetValue(seriesId, out int[]? ids))
        {
            return false;
        }

        foreach (int issueId in ids)
        {
            if (IssueMatches(issueId, mode, normalizedQuery))
            {
                return true;
            }
        }

        return false;
    }

    // An unknown/out-of-range mode falls back to All, same as the old MatchesSearch default arm.
    private static SearchMode ResolveMode(SearchMode mode) =>
        (int)mode >= 0 && (int)mode < s_modeCount ? mode : SearchMode.All;
}

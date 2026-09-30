using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The match keys a story event is known by (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §2). ComicVine and
/// Metron spell the same arc differently - "Hulk: Planet Hulk" vs "Planet Hulk" - and <see cref="TitleNormalizer.NamesMatch"/>
/// can't see through that (<c>HulkPlanetHulk</c> ≠ <c>PlanetHulk</c>). Two events "match by name" when their key sets overlap.
/// </summary>
public static class EventNameKeys
{
    private static readonly Regex YearSuffix = new(@"\s*\((?:19|20)\d{2}\)\s*$", RegexOptions.Compiled);

    /// <summary>"X: rest", "X - rest", "X – rest", "X — rest": the prefix candidate and the remainder.</summary>
    private static readonly Regex PrefixForm = new(@"^\s*(?<prefix>[^:–—]+?)\s*(?::|\s[-–—]\s)\s*(?<rest>.+?)\s*$", RegexOptions.Compiled);

    /// <summary>The single matching key for one name: <see cref="TitleNormalizer.StripDown"/>, lower-cased.</summary>
    public static string Key(string name) => TitleNormalizer.StripDown(name).ToLowerInvariant();

    /// <summary>
    /// Every key for an event: its name; the name without a trailing "(2015)"; the name with a leading series prefix removed when the
    /// prefix names one of the event's own <paramref name="memberSeriesNames"/> (so "Cataclysm: The Ultimates' Last Stand" keeps its
    /// colon unless a member series is called "Cataclysm"); and every alias key.
    /// </summary>
    public static HashSet<string> For(string name, IEnumerable<string> memberSeriesNames, IEnumerable<string>? aliasKeys = null)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddVariants(keys, name, memberSeriesNames as IReadOnlyCollection<string> ?? memberSeriesNames.ToList());
        foreach (string alias in aliasKeys ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(alias))
            {
                keys.Add(alias);
            }
        }

        keys.Remove(string.Empty);
        return keys;
    }

    /// <summary>The name with a member-series prefix removed ("Planet Hulk" for "Hulk: Planet Hulk" with a Hulk member), or null when the name has none.</summary>
    public static string? WithoutSeriesPrefix(string name, IEnumerable<string> memberSeriesNames)
    {
        var match = PrefixForm.Match(name);
        if (!match.Success)
        {
            return null;
        }

        string prefix = YearSuffix.Replace(match.Groups["prefix"].Value, string.Empty).Trim();
        foreach (string series in memberSeriesNames)
        {
            string seriesName = YearSuffix.Replace(series, string.Empty).Trim();
            if (seriesName.Length > 0 && TitleNormalizer.NamesMatch(prefix, seriesName, ignoreVolume: true))
            {
                return match.Groups["rest"].Value.Trim();
            }
        }

        return null;
    }

    private static void AddVariants(HashSet<string> keys, string name, IReadOnlyCollection<string> memberSeriesNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        keys.Add(Key(name));
        string withoutYear = YearSuffix.Replace(name, string.Empty).Trim();
        keys.Add(Key(withoutYear));
        if (WithoutSeriesPrefix(withoutYear, memberSeriesNames) is { Length: > 0 } rest)
        {
            keys.Add(Key(rest));
            keys.Add(Key(YearSuffix.Replace(rest, string.Empty).Trim()));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// L2, companion series (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.2): "Batman Annual", "Superman: Rebirth",
/// "Giant-Size X-Men" are a base title plus a companion word. Each links to the volume of the base title it sits beside (most shared years,
/// then the nearest start), same publisher, as a Side Story. The reason is the companion word, so the card reads "Annual".
/// </summary>
public static class CompanionSuffixInference
{
    public const decimal Confidence = 0.85m;

    /// <summary>Companion words that follow the base title, with the label the card shows.</summary>
    private static readonly (Regex Pattern, string Label)[] Suffixes =
    {
        (Suffix(@"annual"), "Annual"),
        (Suffix(@"special"), "Special"),
        (Suffix(@"rebirth"), "Rebirth"),
        (Suffix(@"secret\s+files(?:\s+(?:and|&)\s+origins)?"), "Secret Files"),
        (Suffix(@"80[\s-]?page\s+giant"), "80-Page Giant"),
        (Suffix(@"sketch\s?book"), "Sketchbook"),
        (Suffix(@"director'?s\s+cut"), "Director's Cut"),
        (Suffix(@"(?:fcbd|free\s+comic\s+book\s+day)(?:\s+(?:special|edition))?"), "Free Comic Book Day"),
    };

    private static readonly Regex GiantSizePrefix = new(@"^\s*giant[\s-]size\s+(?<base>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Batman Annual", "Batman: Annual", "Batman - Annual 2019", "Batman Annual #1": the base before an optional separator, the word, then an
    // optional year/number. Year suffixes "(2016)" are stripped before matching.
    private static Regex Suffix(string word) =>
        new(@"^\s*(?<base>.+?)\s*(?::|[-–—])?\s*\b(?:" + word + @")\b(?:\s+(?:#?\d+|\d{4}))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The base title and the companion label, when <paramref name="name"/> is a companion title.</summary>
    public static (string BaseTitle, string Label)? Parse(string name)
    {
        string bare = EventSpineResolver.StripYear(name);
        var giant = GiantSizePrefix.Match(bare);
        if (giant.Success)
        {
            return (giant.Groups["base"].Value, "Giant-Size");
        }

        foreach (var (pattern, label) in Suffixes)
        {
            var match = pattern.Match(bare);
            if (match.Success && match.Groups["base"].Value.Trim().Length > 0)
            {
                return (match.Groups["base"].Value.Trim(), label);
            }
        }

        return null;
    }

    public static IReadOnlyList<InferredSeriesRelation> Infer(SeriesConnectorSnapshot snapshot)
    {
        var byKey = snapshot.Series.Values.GroupBy(s => s.TitleKey).ToDictionary(g => g.Key, g => g.ToList());
        var results = new List<InferredSeriesRelation>();
        foreach (var companion in snapshot.Series.Values)
        {
            if (Parse(companion.Name) is not var (baseTitle, label))
            {
                continue;
            }

            string baseKey = SeriesConnectorSnapshot.TitleKey(baseTitle);
            if (baseKey.Length == 0 || baseKey == companion.TitleKey || !byKey.TryGetValue(baseKey, out var candidates))
            {
                continue;
            }

            var eligible = candidates
                .Where(c => c.Id != companion.Id && SeriesConnectorGuards.SamePublisher(c, companion) && SeriesConnectorGuards.SameContentFamily(c, companion))
                .ToList();
            if (PickBase(companion, eligible) is not { } target || SeriesConnectorGuards.IsBlocked(snapshot, companion.Id, target.Id))
            {
                continue;
            }

            results.Add(new InferredSeriesRelation(companion.Id, target.Id, RelationType.SideStory, Confidence, label));
        }

        return results;
    }

    /// <summary>The base volume a companion sits beside: most shared years, then the nearest start. With no years to go on, only a lone candidate.</summary>
    internal static ConnectorSeries? PickBase(ConnectorSeries companion, IReadOnlyList<ConnectorSeries> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (companion.StartYear is not int start)
        {
            return candidates.Count == 1 ? candidates[0] : null;
        }

        var overlapping = candidates
            .Select(c => (Series: c, Overlap: SeriesConnectorGuards.OverlapYears(c, companion)))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ThenBy(x => Math.Abs((x.Series.StartYear ?? start) - start))
            .ThenBy(x => x.Series.Id)
            .ToList();
        if (overlapping.Count > 0)
        {
            return overlapping[0].Series;
        }

        // No shared year (an Annual a year after the run ended): the latest volume that started before it.
        return candidates.Where(c => c.StartYear is int s && s <= start).OrderByDescending(c => c.StartYear).ThenBy(c => c.Id).FirstOrDefault();
    }
}

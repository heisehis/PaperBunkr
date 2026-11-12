using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public sealed record SeriesArc(string Name, string Source, IReadOnlyList<Issue> Issues)
{
    public int ReadCount => Issues.Count(i => i.HasBeenRead());
    public string Range => $"#{Issues[0].EffectiveNumber()}–#{Issues[^1].EffectiveNumber()}";
}

/// <summary>Display-only arcs. Explicit metadata wins; title inference never changes stored metadata.</summary>
public static class SeriesArcDetector
{
    private const string Numbers = @"\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty";
    private static readonly Regex Part = new($@"\b(?:Part|Book)\s+(?<n>{Numbers})\b(?:\s+of\s+(?:{Numbers}))?\s*[:.,–—-]?\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" };
    private static string Trim(string value) => value.Trim(' ', ',', ':', '-', '–', '—', '.');
    private static int PartNumber(Match match) => int.TryParse(match.Groups["n"].Value, out int n) ? n : Array.FindIndex(Words, w => w.Equals(match.Groups["n"].Value, StringComparison.OrdinalIgnoreCase));
    private static string Prefix(string title)
    {
        int colon = title.IndexOf(':');
        int part = title.IndexOf(" Part", StringComparison.OrdinalIgnoreCase);
        int at = colon < 0 ? part : part < 0 ? colon : Math.Min(colon, part);
        return at > 0 ? Trim(title[..at]) : string.Empty;
    }

    public static IReadOnlyList<SeriesArc> Find(IEnumerable<Issue> seriesIssues, ContentType contentType)
    {
        var issues = seriesIssues.Where(i => !i.IsPlaceholder).OrderBy(i => i.NumberSortKey() ?? float.MaxValue)
            .ThenBy(i => i.EffectiveNumber(), StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).ToList();
        var used = new HashSet<int>();
        var result = new List<SeriesArc>();
        var explicitArcs = issues.SelectMany(i => (i.StoryArc ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(name => (Name: name, Issue: i)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var arc in explicitArcs)
        {
            var ordered = arc.Select(x => x.Issue).OrderBy(i => Numeric(i.StoryArcNumber) ?? i.NumberSortKey() ?? float.MaxValue)
                .ThenBy(i => i.NumberSortKey() ?? float.MaxValue).ThenBy(i => i.Id).ToList();
            used.UnionWith(ordered.Select(i => i.Id));
            result.Add(new SeriesArc(arc.Key, "from the Story Arc field", ordered));
        }
        if (contentType is not (ContentType.Manga or ContentType.Manhwa or ContentType.Manhua))
        {
            // Find adjacent increasing part sequences; the final pass reunites segments with the same named arc.
            for (int start = 0; start < issues.Count; start++)
            {
                if (used.Contains(issues[start].Id)) continue;
                string title = issues[start].Title ?? string.Empty;
                var match = Part.Match(title);
                if (!match.Success) continue;
                string name = Trim(title[..match.Index]);
                string after = Trim(title[(match.Index + match.Length)..]);
                if (name.Length == 0) name = Prefix(after) is { Length: > 0 } p ? p : after;
                var sequence = new List<Issue> { issues[start] };
                int previous = PartNumber(match);
                for (int next = start + 1; next < issues.Count; next++)
                {
                    if (used.Contains(issues[next].Id)) break;
                    string nextTitle = issues[next].Title ?? string.Empty;
                    var nextMatch = Part.Match(nextTitle);
                    if (!nextMatch.Success || PartNumber(nextMatch) != previous + 1) break;
                    string nextName = Trim(nextTitle[..nextMatch.Index]);
                    string nextAfter = Trim(nextTitle[(nextMatch.Index + nextMatch.Length)..]);
                    if (nextName.Length == 0) nextName = Prefix(nextAfter) is { Length: > 0 } prefix ? prefix : nextAfter;
                    if (name.Length > 0 && nextName.Length > 0 && !name.Equals(nextName, StringComparison.OrdinalIgnoreCase)) break;
                    if (name.Length == 0) name = nextName;
                    sequence.Add(issues[next]);
                    previous++;
                }
                if (name.Length == 0)
                {
                    // Bare markers get a name only from an actual shared neighboring title prefix.
                    var neighboringPrefixes = issues.Skip(Math.Max(0, start - 1)).Take(sequence.Count + 2)
                        .Select(i => Prefix(i.Title ?? string.Empty)).Where(s => s.Length > 0).ToList();
                    name = neighboringPrefixes.GroupBy(s => s, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2).Select(g => g.Key).FirstOrDefault() ?? string.Empty;
                }
                if (name.Length == 0) continue;
                used.UnionWith(sequence.Select(i => i.Id));
                result.Add(new SeriesArc(name, "from titles", sequence));
                start += sequence.Count - 1;
            }
            for (int start = 0; start < issues.Count; start++)
            {
                if (used.Contains(issues[start].Id)) continue;
                string name = Prefix(issues[start].Title ?? string.Empty);
                if (name.Length == 0) continue;
                var sequence = new List<Issue> { issues[start] };
                for (int next = start + 1; next < issues.Count && !used.Contains(issues[next].Id) &&
                    name.Equals(Prefix(issues[next].Title ?? string.Empty), StringComparison.OrdinalIgnoreCase); next++) sequence.Add(issues[next]);
                if (sequence.Count < 2) continue;
                used.UnionWith(sequence.Select(i => i.Id));
                result.Add(new SeriesArc(name, "from titles", sequence));
                start += sequence.Count - 1;
            }
        }
        var positions = issues.Select((issue, index) => (issue.Id, index)).ToDictionary(x => x.Id, x => x.index);
        // A named arc can have explicit metadata on some issues and only a title on others. Keep one section and honest provenance;
        // missing owned parts or an unrelated issue between parts must not produce several identically named arc headers.
        return result.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var sources = group.Select(r => r.Source).Distinct().ToList();
            var members = group.SelectMany(r => r.Issues).DistinctBy(i => i.Id)
                .OrderBy(i => Numeric(i.StoryArcNumber) ?? i.NumberSortKey() ?? float.MaxValue).ThenBy(i => positions[i.Id]).ToList();
            return new SeriesArc(group.First().Name, sources.Count == 1 ? sources[0] : "from the Story Arc field and titles", members);
        }).OrderBy(r => r.Issues.Min(i => positions[i.Id])).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static float? Numeric(string? value) => float.TryParse(value, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out float n) ? n : null;
}

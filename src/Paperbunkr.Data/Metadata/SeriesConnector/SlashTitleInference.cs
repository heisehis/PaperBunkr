using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// L3, team-up titles (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.2): "Avengers/X-Men" or "Batman/Superman:
/// World's Finest" names two or three series. Each named series you own (the volume sharing most years with the team-up) is linked to it as
/// a Crossover. The only local rule allowed across publishers ("Batman/Teenage Mutant Ninja Turtles").
/// </summary>
public static class SlashTitleInference
{
    public const decimal Confidence = 0.8m;
    public const string Reason = "Team-up title";

    /// <summary>The 2-3 series names in a slash title, or empty. A trailing ": subtitle" is kept as a second try for the last name.</summary>
    public static IReadOnlyList<string> Parts(string name)
    {
        string bare = EventSpineResolver.StripYear(name);
        if (!bare.Contains('/'))
        {
            return [];
        }

        var parts = bare.Split('/').Select(p => p.Trim()).ToList();
        return parts.Count is >= 2 and <= 3 && parts.All(p => p.Length > 1) ? parts : [];
    }

    public static IReadOnlyList<InferredSeriesRelation> Infer(SeriesConnectorSnapshot snapshot)
    {
        var byKey = snapshot.Series.Values.GroupBy(s => s.TitleKey).ToDictionary(g => g.Key, g => g.ToList());
        var results = new List<InferredSeriesRelation>();
        foreach (var teamUp in snapshot.Series.Values)
        {
            var parts = Parts(teamUp.Name);
            if (parts.Count == 0)
            {
                continue;
            }

            var linked = new HashSet<int>();
            foreach (string part in parts)
            {
                var candidates = Candidates(byKey, part, teamUp) ?? (part.Contains(':') ? Candidates(byKey, part[..part.IndexOf(':')], teamUp) : null);
                if (candidates is null || CompanionSuffixInference.PickBase(teamUp, candidates) is not { } named
                    || !linked.Add(named.Id) || SeriesConnectorGuards.IsBlocked(snapshot, named.Id, teamUp.Id))
                {
                    continue;
                }

                results.Add(new InferredSeriesRelation(named.Id, teamUp.Id, RelationType.Crossover, Confidence, Reason));
            }
        }

        return results;
    }

    private static List<ConnectorSeries>? Candidates(Dictionary<string, List<ConnectorSeries>> byKey, string part, ConnectorSeries teamUp)
    {
        string key = SeriesConnectorSnapshot.TitleKey(part);
        if (key.Length == 0 || key == teamUp.TitleKey || !byKey.TryGetValue(key, out var all))
        {
            return null;
        }

        var eligible = all.Where(s => s.Id != teamUp.Id && SeriesConnectorGuards.SameContentFamily(s, teamUp)).ToList();
        return eligible.Count > 0 ? eligible : null;
    }
}

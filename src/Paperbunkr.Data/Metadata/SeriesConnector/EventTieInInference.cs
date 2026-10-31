using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// L4 and L5, story events (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.4). An event that spans two or more
/// series and has a main series (<see cref="EventSpineResolver"/>): every other member series is a Tie-in to the main series, with its
/// issue range as the reason ("Tie-in · #33–34"). No main series and 2-3 series: they're linked to each other as Crossovers. No main
/// series and 4+: nothing - the Related tab shows the event itself. The event id goes in <see cref="InferredSeriesRelation.ProviderSourceId"/>,
/// so a series that ties into two events with the same main series gets one relation with two evidence rows.
/// </summary>
public static class EventTieInInference
{
    public const decimal Confidence = 0.8m;

    /// <summary>The most series an event with no main series may span and still have its series linked to each other.</summary>
    public const int MaxCrossoverSeries = 3;

    public static IReadOnlyList<InferredSeriesRelation> Infer(SeriesConnectorSnapshot snapshot)
    {
        var results = new List<InferredSeriesRelation>();
        foreach (var storyEvent in snapshot.Events)
        {
            var seriesIds = storyEvent.Members.Select(m => m.SeriesId).Where(snapshot.Series.ContainsKey).Distinct().ToList();
            if (seriesIds.Count < 2)
            {
                continue;
            }

            string sourceId = storyEvent.Id.ToString(CultureInfo.InvariantCulture);
            int? main = MainSeries(storyEvent);
            if (main is int mainId && snapshot.Series.TryGetValue(mainId, out var mainSeries))
            {
                foreach (int tieInId in seriesIds.Where(id => id != mainId))
                {
                    if (!SeriesConnectorGuards.SameContentFamily(snapshot.Series[tieInId], mainSeries) || SeriesConnectorGuards.IsBlocked(snapshot, tieInId, mainId))
                    {
                        continue;
                    }

                    var numbers = storyEvent.Members.Where(m => m.SeriesId == tieInId).ToList();
                    results.Add(new InferredSeriesRelation(tieInId, mainId, RelationType.TieIn, Confidence, $"Tie-in · {IssueRange(numbers)}",
                        RelationEvidenceProvider.Inferred, sourceId));
                }
            }
            else if (seriesIds.Count <= MaxCrossoverSeries)
            {
                for (int i = 0; i < seriesIds.Count; i++)
                {
                    for (int j = i + 1; j < seriesIds.Count; j++)
                    {
                        var a = snapshot.Series[seriesIds[i]];
                        var b = snapshot.Series[seriesIds[j]];
                        if (SeriesConnectorGuards.SameContentFamily(a, b) && !SeriesConnectorGuards.IsBlocked(snapshot, a.Id, b.Id))
                        {
                            results.Add(new InferredSeriesRelation(a.Id, b.Id, RelationType.Crossover, Confidence, $"Crossover · {storyEvent.Name}",
                                RelationEvidenceProvider.Inferred, sourceId));
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>The event's main series, by the rule the Event Map uses.</summary>
    public static int? MainSeries(ConnectorEvent storyEvent) =>
        EventSpineResolver.Resolve(storyEvent.Name, storyEvent.SavedSpineSeriesId, storyEvent.Members.Select(m => (m.SeriesId, m.SeriesName)).ToList());

    /// <summary>"#33–34", "#33, #35", "#1–6, #8"; at most three runs, then "…". Non-numeric numbers ("Alpha") are listed as they are.</summary>
    public static string IssueRange(IReadOnlyList<ConnectorEventMember> members)
    {
        var numeric = members.Where(m => m.NumberKey is not null).Select(m => m.NumberKey!.Value).Distinct().OrderBy(n => n).ToList();
        var other = members.Where(m => m.NumberKey is null && !string.IsNullOrWhiteSpace(m.Number)).Select(m => m.Number!.Trim()).Distinct().ToList();

        var runs = new List<string>();
        int k = 0;
        while (k < numeric.Count)
        {
            int end = k;
            while (end + 1 < numeric.Count && IsWhole(numeric[end]) && IsWhole(numeric[end + 1]) && numeric[end + 1] - numeric[end] == 1)
            {
                end++;
            }

            runs.Add(end == k ? $"#{Format(numeric[k])}" : $"#{Format(numeric[k])}–{Format(numeric[end])}");
            k = end + 1;
        }

        runs.AddRange(other.Select(o => $"#{o}"));
        if (runs.Count == 0)
        {
            return members.Count == 1 ? "1 issue" : $"{members.Count} issues";
        }

        return runs.Count <= 3 ? string.Join(", ", runs) : string.Join(", ", runs.Take(3)) + ", …";
    }

    private static bool IsWhole(float n) => n == System.MathF.Floor(n);

    private static string Format(float n) => IsWhole(n) ? ((int)n).ToString(CultureInfo.InvariantCulture) : n.ToString("0.##", CultureInfo.InvariantCulture);
}

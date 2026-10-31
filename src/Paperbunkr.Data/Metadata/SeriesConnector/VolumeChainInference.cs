using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// L1, the volume chain (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.2): series with the same title and
/// publisher whose runs don't overlap are volumes of one title. Each volume links only to the one immediately before it, newer → older,
/// as a Continuation - the same direction GCD's numbering-continues bonds use. Two runs that overlap are left alone: that's one series
/// split in two (Library Health's job), not a later volume.
/// </summary>
public static class VolumeChainInference
{
    public const decimal Confidence = 0.9m;
    public const string Reason = "Later volume";

    public static IReadOnlyList<InferredSeriesRelation> Infer(SeriesConnectorSnapshot snapshot)
    {
        var results = new List<InferredSeriesRelation>();
        foreach (var group in snapshot.Series.Values.Where(s => s.StartYear is not null && s.TitleKey.Length > 0).GroupBy(s => s.TitleKey))
        {
            var volumes = group.OrderBy(s => s.StartYear).ThenBy(s => s.Id).ToList();
            for (int i = 1; i < volumes.Count; i++)
            {
                var later = volumes[i];

                // The nearest earlier volume from the same publisher and content family.
                var earlier = volumes.Take(i)
                    .Where(v => v.StartYear < later.StartYear && SeriesConnectorGuards.SamePublisher(v, later) && SeriesConnectorGuards.SameContentFamily(v, later))
                    .LastOrDefault();
                if (earlier is null || (earlier.EndYear ?? earlier.StartYear) > later.StartYear || SeriesConnectorGuards.IsBlocked(snapshot, later.Id, earlier.Id))
                {
                    continue;
                }

                results.Add(new InferredSeriesRelation(later.Id, earlier.Id, RelationType.Continuation, Confidence, Reason));
            }
        }

        return results;
    }
}

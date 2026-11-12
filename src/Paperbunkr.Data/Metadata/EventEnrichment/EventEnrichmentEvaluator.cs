using System.Text.Json;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

public static class EventEnrichmentEvaluator
{
    public static IReadOnlyList<EventEvidence> Select(EventEnrichmentProposal proposal)
    {
        var result = new List<EventEvidence>();
        foreach (var group in proposal.Evidence.Where(e => e.Automatic && !string.IsNullOrWhiteSpace(e.Value)).GroupBy(e => e.Field))
        {
            if (!proposal.Snapshot.Fields.TryGetValue(group.Key, out var current) || current.Value is not null || current.Protected) continue;
            var best = group.Where(e => e.Quality == group.Max(e => e.Quality))
                .GroupBy(Normalize).Select(g => g.OrderBy(e => e.Provider, StringComparer.Ordinal).First()).ToList();
            if (group.Key == EventMetadataField.Cover && best.Count > 1)
                best = best.OrderBy(e => e.IssueCover).ThenBy(e => e.Provider == "Metron" ? 0 : e.Provider == "ComicVine" ? 1 : 2).Take(1).ToList();
            if (best.Count == 1) result.Add(best[0]);
        }
        return result;
    }

    private static string Normalize(EventEvidence evidence)
    {
        if (evidence.Field is EventMetadataField.StartDate or EventMetadataField.EndDate && JsonSerializer.Deserialize<EventPublicationDate>(evidence.Value) is { } d)
            return $"{d.Year}|{d.Month}|{d.Day}|{d.Uncertain}|{d.Provisional}";
        return Regex.Replace(evidence.Value.Trim(), @"\s+", " ");
    }

    public static bool ValidDates(string? startJson, string? endJson)
    {
        var start = startJson is null ? null : JsonSerializer.Deserialize<EventPublicationDate>(startJson);
        var end = endJson is null ? null : JsonSerializer.Deserialize<EventPublicationDate>(endJson);
        return start is null || end is null || start.Lower <= end.Upper;
    }
}

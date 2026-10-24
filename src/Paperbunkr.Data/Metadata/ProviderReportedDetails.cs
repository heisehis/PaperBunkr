using System.Globalization;
using System.Text.Json;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Explicitly attributed provider facts, separate from editable canonical metadata.</summary>
public static class ProviderReportedDetails
{
    public static IReadOnlyList<(string Label, string Value)> Read(IEnumerable<ExternalMetadataSnapshot> snapshots)
    {
        var rows = new List<(string, string)>();
        foreach (var snapshot in snapshots.GroupBy(s => s.Provider)
                     .Select(g => g.OrderByDescending(s => s.RetrievedAt).ThenByDescending(s => s.Id).First())
                     .OrderBy(s => s.Provider))
        {
            ExternalMediaMetadata? metadata;
            try { metadata = JsonSerializer.Deserialize<ExternalMediaMetadata>(snapshot.Payload); }
            catch (JsonException) { continue; }
            if (metadata is null) continue;
            string provider = snapshot.Provider.ToString();
            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) rows.Add(($"{provider} · {label}", value));
            }
            Add("Publication start", FormatDate(metadata.PublicationStart));
            Add("Publication end", FormatDate(metadata.PublicationEnd));
            if (metadata.Popularity is >= 0)
                Add("Popularity (users)", metadata.Popularity.Value.ToString("N0", CultureInfo.CurrentCulture));
            foreach (var identifier in metadata.AdditionalIdentifiers ?? new Dictionary<string, string>())
                Add($"{identifier.Key.Replace('_', ' ')} ID", identifier.Value);
        }
        return rows;
    }

    private static string? FormatDate(ProviderPublicationDate? date)
    {
        if (date is null || (date.Year is null && date.Month is null && date.Day is null)) return null;
        string year = date.Year?.ToString("D4", CultureInfo.InvariantCulture) ?? "????";
        if (date.Month is null && date.Day is null) return year;
        string month = date.Month?.ToString("D2", CultureInfo.InvariantCulture) ?? "??";
        return date.Day is null ? $"{year}-{month}" : $"{year}-{month}-{date.Day.Value.ToString("D2", CultureInfo.InvariantCulture)}";
    }
}

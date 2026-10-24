using System.Globalization;
using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Builds the current generated Reading List tag set from stored list and issue metadata.</summary>
public static class ReadingListTagGenerator
{
    public sealed record GeneratedTag(string Value, string Category, string Key);

    public static IReadOnlyList<GeneratedTag> Generate(ReadingList list)
    {
        var result = new Dictionary<string, GeneratedTag>(StringComparer.OrdinalIgnoreCase);

        AddMetadata(result, "metadata:source", "Source", list.Source is { Length: > 0 } source
            ? ReadingListSourceRegistry.GetDisplayName(source)
            : null);
        AddMetadata(result, "metadata:type", "List type", list.Type.ToString());
        AddMetadata(result, "metadata:event", "Story Event", list.StoryEvent?.Name);
        AddMetadata(result, "metadata:continuity", "Continuity", list.Continuity?.Name);

        foreach (var tag in list.Items
                     .Where(item => item.Issue is not null)
                     .SelectMany(item => item.Issue!.Tags))
        {
            string value = tag.Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            string category = tag.Category?.Trim() ?? string.Empty;
            string key = $"issue:{Normalize(category)}:{Normalize(value)}";
            result.TryAdd(key, new GeneratedTag(value, category, key));
        }

        return result.Values.OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddMetadata(IDictionary<string, GeneratedTag> result, string key, string category, string? rawValue)
    {
        string value = rawValue?.Trim() ?? string.Empty;
        if (value.Length > 0)
        {
            result[key] = new GeneratedTag(value, category, key);
        }
    }

    private static string Normalize(string value) => string.Join(' ', value.Trim().Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

using System.Globalization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// ComicRack's "Same X and different Y" script as a smart-list condition (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §1): group the books by the
/// values of the X attributes, drop groups of one, and keep every book of the groups where some Y attribute differs between two books (every book of every group when Y is empty,
/// which makes "same X" a duplicate finder). Attributes are <c>|</c>-separated names; a name is a smart-list field (by enum name or label, ignoring case) and otherwise a custom value.
/// </summary>
internal static class SameXDifferentY
{
    private static readonly Dictionary<string, SmartListField> FieldsByName = BuildFieldIndex();

    /// <summary>The ids of the books in <paramref name="library"/> that sit in a flagged group. Needs <see cref="Issue.CustomValues"/> loaded for custom-value attributes.</summary>
    public static HashSet<int> Compute(IReadOnlyList<Issue> library, string? x, string? y)
    {
        var flagged = new HashSet<int>();
        string[] xs = Split(x);
        if (xs.Length == 0)
        {
            return flagged;
        }

        string[] ys = Split(y);
        var groups = new Dictionary<string, List<Issue>>(StringComparer.Ordinal);
        foreach (Issue issue in library)
        {
            string key = string.Concat(xs.Select(name => Resolve(name, issue) is { } value ? "-" + value : string.Empty));
            if (key.Length == 0)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out List<Issue>? members))
            {
                groups[key] = members = new List<Issue>();
            }

            members.Add(issue);
        }

        foreach (List<Issue> members in groups.Values.Where(g => g.Count >= 2))
        {
            // With no Y every book of a group of two or more is returned; otherwise only a group where some attribute takes two different values.
            if (ys.Length == 0 || ys.Any(name => members.Select(m => Resolve(name, m) ?? "None").Distinct(StringComparer.Ordinal).Skip(1).Any()))
            {
                flagged.UnionWith(members.Select(m => m.Id));
            }
        }

        return flagged;
    }

    /// <summary>The attribute's value as text, or null when the book has no such attribute (not a field, and no custom value of that name).</summary>
    private static string? Resolve(string name, Issue issue)
    {
        if (FieldsByName.TryGetValue(name, out SmartListField field))
        {
            return FieldText(field, issue);
        }

        return issue.CustomValues.FirstOrDefault(cv => string.Equals(cv.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string? FieldText(SmartListField field, Issue issue) => SmartListCatalog.Definitions[field].DataType switch
    {
        SmartListDataType.Text => SmartListCatalog.TextSelectors[field](issue),
        SmartListDataType.Number => SmartListCatalog.NumberSelectors[field](issue).ToString(CultureInfo.InvariantCulture),
        SmartListDataType.Toggle => SmartListCatalog.ToggleSelectors[field](issue).ToString(),
        SmartListDataType.Date => SmartListCatalog.DateSelectors[field](issue)?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        _ => null,
    };

    private static string[] Split(string? list) =>
        (list ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Name and label of every field that has a flat selector (the four series figures and the like are computed elsewhere and left out).</summary>
    private static Dictionary<string, SmartListField> BuildFieldIndex()
    {
        var index = new Dictionary<string, SmartListField>(StringComparer.OrdinalIgnoreCase);
        foreach (SmartListFieldDefinition definition in SmartListCatalog.Definitions.Values)
        {
            bool hasSelector = definition.DataType switch
            {
                SmartListDataType.Text => SmartListCatalog.TextSelectors.ContainsKey(definition.Field),
                SmartListDataType.Number => SmartListCatalog.NumberSelectors.ContainsKey(definition.Field),
                SmartListDataType.Toggle => SmartListCatalog.ToggleSelectors.ContainsKey(definition.Field),
                SmartListDataType.Date => SmartListCatalog.DateSelectors.ContainsKey(definition.Field),
                _ => false,
            };
            if (!hasSelector)
            {
                continue;
            }

            index.TryAdd(definition.Field.ToString(), definition.Field);
            index.TryAdd(definition.Label, definition.Field);
        }

        return index;
    }
}

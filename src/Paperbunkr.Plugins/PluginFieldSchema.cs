using System.Globalization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins;

/// <summary>One declared plugin smart field. <see cref="StorageName"/> is the custom-value name its values live under.</summary>
public sealed record PluginFieldDefinition(string PluginKey, string Key, string Label, SmartListDataType Type)
{
    public string StorageName => PluginFieldSchema.StorageName(PluginKey, Key);
}

/// <summary>
/// A plugin's declared smart fields (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §D1).
/// A field is a <em>typed custom value</em>: values are stored as the issue custom value <c>&lt;pluginKey&gt;.&lt;fieldKey&gt;</c> (database
/// only, never written to files), as invariant-culture text - a number as <c>0.##</c>, a date as <c>yyyy-MM-dd</c>, a toggle as
/// <c>true</c>/<c>false</c>. The declared type is what lets a smart list compare it as a number, date or yes/no instead of as text.
/// </summary>
public sealed class PluginFieldSchema
{
    private readonly Dictionary<string, PluginFieldDefinition> _byKey;

    private PluginFieldSchema(IReadOnlyList<PluginFieldDefinition> fields)
    {
        Fields = fields;
        _byKey = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<PluginFieldDefinition> Fields { get; }

    public PluginFieldDefinition? Find(string key) => _byKey.GetValueOrDefault(key);

    public static string StorageName(string pluginKey, string fieldKey) => $"{pluginKey}.{fieldKey}";

    /// <summary>Null with no error when the manifest declares no fields; null with an error for a malformed one (the plugin is blocked).</summary>
    public static PluginFieldSchema? Parse(string pluginKey, PluginManifest manifest, out string? error)
    {
        error = null;
        if (manifest.Fields is not { Count: > 0 } entries)
        {
            return null;
        }

        var fields = new List<PluginFieldDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FieldManifestEntry entry in entries)
        {
            string key = entry.Key?.Trim() ?? string.Empty;
            if (key.Length == 0)
            {
                error = "a <Field> has no key";
                return null;
            }

            if (!seen.Add(key))
            {
                error = $"field '{key}' is declared more than once";
                return null;
            }

            SmartListDataType? type = entry.Type?.Trim().ToLowerInvariant() switch
            {
                "number" => SmartListDataType.Number,
                "text" => SmartListDataType.Text,
                "date" => SmartListDataType.Date,
                "toggle" => SmartListDataType.Toggle,
                _ => null,
            };
            if (type is null)
            {
                error = $"field '{key}': unknown type '{entry.Type}' (expected number, text, date or toggle)";
                return null;
            }

            string label = string.IsNullOrWhiteSpace(entry.Label) ? key : entry.Label!.Trim();
            fields.Add(new PluginFieldDefinition(pluginKey, key, label, type.Value));
        }

        return new PluginFieldSchema(fields);
    }

    /// <summary>
    /// <paramref name="value"/> in the stored form for <paramref name="type"/>, or null when it isn't a valid value of that type.
    /// Accepts invariant-culture text; a date may carry a time, which is dropped.
    /// </summary>
    public static string? Normalize(SmartListDataType type, string value)
    {
        string trimmed = value.Trim();
        return type switch
        {
            SmartListDataType.Number => decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal n)
                ? n.ToString("0.##########", CultureInfo.InvariantCulture)
                : null,
            SmartListDataType.Date => DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d)
                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null,
            SmartListDataType.Toggle => bool.TryParse(trimmed, out bool b) ? (b ? "true" : "false") : null,
            _ => value,
        };
    }
}

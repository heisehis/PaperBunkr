using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>What <see cref="PluginSettingsTransfer.Import"/> did, for the overlay's one-line summary.</summary>
public sealed record PluginSettingsImportResult(
    bool Succeeded,
    string? Error,
    int Imported,
    IReadOnlyList<string> Skipped)
{
    public static PluginSettingsImportResult Failure(string error) => new(false, error, 0, Array.Empty<string>());

    /// <summary>One line for the overlay: what was imported and how many were skipped (the reasons go in <see cref="Skipped"/>).</summary>
    public string Describe() =>
        !Succeeded
            ? Error!
            : Skipped.Count == 0
                ? $"Imported {Imported} {(Imported == 1 ? "setting" : "settings")}."
                : $"Imported {Imported} {(Imported == 1 ? "setting" : "settings")}; skipped {Skipped.Count}: {string.Join("; ", Skipped)}";
}

/// <summary>
/// Export/import of one plugin's stored settings as JSON (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §6).
/// Declared secrets are never exported (DPAPI ciphertext only opens for one Windows user on one machine, and plaintext in a file would
/// defeat it); import validates every value against the plugin's schema and only ever touches the plugin named in the file.
/// </summary>
public static class PluginSettingsTransfer
{
    public const string FormatName = "paperbunkr-plugin-settings";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>The file's JSON: every stored key for the plugin, sorted, minus declared secrets (named in <c>secretsExcluded</c>).</summary>
    public static string Export(PluginSettingsAccess access, string pluginKey, string pluginName, PluginSettingsSchema schema, DateTime? exportedUtc = null)
    {
        var secrets = schema.Definitions.Where(d => d.Type == PluginSettingType.Secret).Select(d => d.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in access.StoredEntries(pluginKey))
        {
            if (!secrets.Contains(entry.Key, StringComparer.Ordinal))
            {
                settings[entry.Key] = entry.Value;
            }
        }

        var document = new Dictionary<string, object>
        {
            ["format"] = FormatName,
            ["version"] = FormatVersion,
            ["plugin"] = pluginKey,
            ["pluginName"] = pluginName,
            ["apiVersion"] = PluginApi.Current.ToString(),
            ["exportedUtc"] = (exportedUtc ?? DateTime.UtcNow).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["secretsExcluded"] = secrets,
            ["settings"] = settings,
        };
        return JsonSerializer.Serialize(document, WriteOptions);
    }

    /// <summary>
    /// Applies <paramref name="json"/> to <paramref name="pluginKey"/>. Refuses (writing nothing) malformed JSON, another format or
    /// version, or a file exported from a different plugin. Otherwise each key is written unless it is a declared secret, fails the
    /// schema (skipped with the reason), or is locked with a value already stored; undeclared keys are written verbatim.
    /// </summary>
    public static PluginSettingsImportResult Import(PluginSettingsAccess access, string pluginKey, PluginSettingsSchema schema, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return PluginSettingsImportResult.Failure("That file isn't valid JSON.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, "format", out string? format) || format != FormatName)
            {
                return PluginSettingsImportResult.Failure("That isn't a Paperbunkr plugin settings file.");
            }

            if (!root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int versionNumber) || versionNumber != FormatVersion)
            {
                return PluginSettingsImportResult.Failure("That settings file is from a different version of the format.");
            }

            if (!TryString(root, "plugin", out string? filePlugin) || !string.Equals(filePlugin, pluginKey, StringComparison.Ordinal))
            {
                return PluginSettingsImportResult.Failure($"That file holds settings for '{filePlugin ?? "an unknown plugin"}', not '{pluginKey}'.");
            }

            if (!root.TryGetProperty("settings", out JsonElement settings) || settings.ValueKind != JsonValueKind.Object)
            {
                return PluginSettingsImportResult.Failure("That settings file has no settings in it.");
            }

            int imported = 0;
            var skipped = new List<string>();
            foreach (JsonProperty property in settings.EnumerateObject())
            {
                string key = property.Name;
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    skipped.Add($"{key} (not a text value)");
                    continue;
                }

                string value = property.Value.GetString() ?? string.Empty;
                PluginSettingDefinition? definition = schema.Find(key);
                if (definition is not null)
                {
                    if (definition.Type == PluginSettingType.Secret)
                    {
                        skipped.Add($"{key} (secrets aren't imported)");
                        continue;
                    }

                    if (access.IsLocked(pluginKey, definition))
                    {
                        skipped.Add($"{key} (locked)");
                        continue;
                    }

                    string? problem = PluginSettingsSchema.Validate(definition, value);
                    if (problem is not null)
                    {
                        skipped.Add($"{key} ({problem.TrimEnd('.')})");
                        continue;
                    }
                }

                access.Set(pluginKey, key, value);
                imported++;
            }

            return new PluginSettingsImportResult(true, null, imported, skipped);
        }
    }

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        return false;
    }
}

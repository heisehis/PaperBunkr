using System.Globalization;

namespace Paperbunkr.Plugins;

/// <summary>The value type of a declared plugin setting (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6.1).</summary>
public enum PluginSettingType
{
    Toggle,
    Text,
    Choice,
    Number,

    /// <summary>Like text, but masked in the UI and encrypted at rest with DPAPI (§6.2). Has no default.</summary>
    Secret,
}

public sealed record PluginSettingChoice(string Value, string Label);

/// <summary>One validated setting declared in a plugin's <c>&lt;Settings&gt;</c>. Values are still strings in the store; this adds typing and rendering.</summary>
public sealed record PluginSettingDefinition(
    string Key,
    string Label,
    PluginSettingType Type,
    string? Default,
    string? Description,
    decimal? Min,
    decimal? Max,
    IReadOnlyList<PluginSettingChoice> Choices);

/// <summary>
/// Protects <c>secret</c> setting values at rest. The app's implementation is Windows DPAPI; tests use a
/// fake. Where <see cref="IsAvailable"/> is false (no DPAPI on this platform) secrets fail closed - they
/// read as unset and can't be written - rather than being stored as plain text.
/// </summary>
public interface ISecretProtector
{
    bool IsAvailable { get; }

    /// <summary>Encrypts <paramref name="plaintext"/>. Throws <see cref="PlatformNotSupportedException"/> when not <see cref="IsAvailable"/>.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a value from <see cref="Protect"/>; null if it can't be (wrong user/machine, corrupt, unavailable) - never throws.</summary>
    string? TryUnprotect(string stored);

    /// <summary>True when <paramref name="stored"/> is in this protector's encrypted format.</summary>
    bool IsProtected(string stored);
}

/// <summary>
/// A plugin's declared settings (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6): parsed and
/// validated from <c>&lt;Settings&gt;</c> once at discovery, then used to resolve what value a plugin
/// should see. The host is the sanitization layer - a plugin never receives a value that violates its own
/// schema: an invalid stored value resolves to the declared default, while the raw stored string is left
/// alone for the settings UI to show and flag.
/// </summary>
public sealed class PluginSettingsSchema
{
    private readonly Dictionary<string, PluginSettingDefinition> _byKey;

    private PluginSettingsSchema(IReadOnlyList<PluginSettingDefinition> definitions)
    {
        Definitions = definitions;
        _byKey = definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<PluginSettingDefinition> Definitions { get; }

    public PluginSettingDefinition? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>
    /// Parses and validates <paramref name="manifest"/>'s <c>&lt;Settings&gt;</c>. Returns null with a null
    /// <paramref name="error"/> when the manifest declares none (nothing to do), null with an
    /// <paramref name="error"/> when a declaration is malformed (the plugin is then blocked with that
    /// reason - a malformed manifest entry is broken-with-a-reason everywhere else too), or the schema.
    /// </summary>
    public static PluginSettingsSchema? Parse(PluginManifest manifest, out string? error)
    {
        error = null;
        if (manifest.Settings is not { Count: > 0 } entries)
        {
            return null;
        }

        var definitions = new List<PluginSettingDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SettingManifestEntry entry in entries)
        {
            string key = entry.Key?.Trim() ?? string.Empty;
            if (key.Length == 0)
            {
                error = "a <Setting> has no key";
                return null;
            }

            if (!seen.Add(key))
            {
                error = $"setting '{key}' is declared more than once";
                return null;
            }

            string? problem = TryBuild(key, entry, out PluginSettingDefinition? definition);
            if (problem is not null)
            {
                error = $"setting '{key}': {problem}";
                return null;
            }

            definitions.Add(definition!);
        }

        return new PluginSettingsSchema(definitions);
    }

    private static string? TryBuild(string key, SettingManifestEntry entry, out PluginSettingDefinition? definition)
    {
        definition = null;
        if (!Enum.TryParse(entry.Type, ignoreCase: true, out PluginSettingType type) || !Enum.IsDefined(type))
        {
            return $"unknown type '{entry.Type}' (expected toggle, text, choice, number or secret)";
        }

        var choices = entry.Choices
            .Select(c => new PluginSettingChoice(c.Value ?? string.Empty, string.IsNullOrEmpty(c.Label) ? c.Value ?? string.Empty : c.Label!))
            .ToList();

        if (type != PluginSettingType.Choice && choices.Count > 0)
        {
            return "<Choice> is only valid on a choice setting";
        }

        if (type != PluginSettingType.Number && (entry.Min is not null || entry.Max is not null))
        {
            return "min/max are only valid on a number setting";
        }

        string label = string.IsNullOrWhiteSpace(entry.Label) ? key : entry.Label!;
        string? defaultValue = entry.Default;
        decimal? min = null;
        decimal? max = null;

        switch (type)
        {
            case PluginSettingType.Secret:
                if (defaultValue is not null)
                {
                    return "a secret can't have a default";
                }

                break;

            case PluginSettingType.Toggle:
                if (defaultValue is not null && !IsBool(defaultValue))
                {
                    return $"default '{defaultValue}' isn't true or false";
                }

                break;

            case PluginSettingType.Choice:
                if (choices.Count == 0)
                {
                    return "a choice setting needs at least one <Choice>";
                }

                if (choices.Any(c => c.Value.Length == 0))
                {
                    return "a <Choice> has no value";
                }

                if (choices.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count() != choices.Count)
                {
                    return "two <Choice>s have the same value";
                }

                if (defaultValue is not null && !choices.Any(c => c.Value == defaultValue))
                {
                    return $"default '{defaultValue}' isn't one of the choices";
                }

                break;

            case PluginSettingType.Number:
                if (entry.Min is not null)
                {
                    if (!TryNumber(entry.Min, out decimal parsedMin))
                    {
                        return $"min '{entry.Min}' isn't a number";
                    }

                    min = parsedMin;
                }

                if (entry.Max is not null)
                {
                    if (!TryNumber(entry.Max, out decimal parsedMax))
                    {
                        return $"max '{entry.Max}' isn't a number";
                    }

                    max = parsedMax;
                }

                if (min is not null && max is not null && min > max)
                {
                    return $"min {entry.Min} is greater than max {entry.Max}";
                }

                if (defaultValue is not null)
                {
                    if (!TryNumber(defaultValue, out decimal parsedDefault))
                    {
                        return $"default '{defaultValue}' isn't a number";
                    }

                    if ((min is not null && parsedDefault < min) || (max is not null && parsedDefault > max))
                    {
                        return $"default {defaultValue} is outside the min/max range";
                    }
                }

                break;
        }

        definition = new PluginSettingDefinition(key, label, type, defaultValue, entry.Description, min, max, choices);
        return null;
    }

    /// <summary>
    /// Checks <paramref name="value"/> against <paramref name="definition"/>; null when valid, otherwise a
    /// short message fit to show inline. <c>text</c> and <c>secret</c> accept anything. A toggle is
    /// <c>true</c>/<c>false</c> (any case); a number must parse (invariant culture) and sit within min/max;
    /// a choice must be one of its declared values. Null/unset is valid - it simply resolves to the default.
    /// </summary>
    public static string? Validate(PluginSettingDefinition definition, string? value)
    {
        if (value is null)
        {
            return null;
        }

        switch (definition.Type)
        {
            case PluginSettingType.Toggle:
                return IsBool(value) ? null : "Must be true or false.";

            case PluginSettingType.Number:
                if (!TryNumber(value, out decimal number))
                {
                    return "Must be a number.";
                }

                if (definition.Min is decimal min && number < min)
                {
                    return $"Must be at least {min.ToString(CultureInfo.InvariantCulture)}.";
                }

                if (definition.Max is decimal max && number > max)
                {
                    return $"Must be at most {max.ToString(CultureInfo.InvariantCulture)}.";
                }

                return null;

            case PluginSettingType.Choice:
                return definition.Choices.Any(c => c.Value == value)
                    ? null
                    : $"Must be one of: {string.Join(", ", definition.Choices.Select(c => c.Value))}.";

            default:
                return null;
        }
    }

    /// <summary>
    /// The value a plugin should see for <paramref name="definition"/> given what is <paramref name="stored"/>
    /// (null = never set): the declared default when unset or when the stored value violates the schema, a
    /// secret's decrypted text (the default when it won't decrypt), otherwise the stored value. Never throws.
    /// </summary>
    public static string? Resolve(PluginSettingDefinition definition, string? stored, ISecretProtector protector)
    {
        if (stored is null)
        {
            return definition.Default;
        }

        if (definition.Type == PluginSettingType.Secret)
        {
            if (!protector.IsProtected(stored))
            {
                // Written before the schema existed (or by an older host): plain text is still the value.
                return stored;
            }

            return protector.TryUnprotect(stored) ?? definition.Default;
        }

        return Validate(definition, stored) is null ? stored : definition.Default;
    }

    private static bool IsBool(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static bool TryNumber(string value, out decimal number) =>
        decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
}

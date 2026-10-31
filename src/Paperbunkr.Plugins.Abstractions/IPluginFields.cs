using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins;

/// <summary>
/// Values for this plugin's declared smart fields (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-
/// design.md §D1). Declare a field in <c>plugin.xml</c> - <c>&lt;Fields&gt;&lt;Field key="daysSinceRead" label="Days since read"
/// type="number"/&gt;&lt;/Fields&gt;</c> - and push a value per issue; the field then appears in the Smart List editor under "Plugin
/// fields" and is compared as its type. Values are kept in the library database only, never written into files.
/// </summary>
public interface IPluginFields
{
    /// <summary>
    /// Stores <paramref name="value"/> for <paramref name="field"/> on <paramref name="issue"/>; null removes it. False (and logged) when the
    /// field isn't declared, the value isn't valid for the field's type, or the issue no longer exists.
    /// </summary>
    bool Set(Issue issue, string field, string? value);

    /// <summary>The stored value, or null when the issue has none.</summary>
    string? Get(Issue issue, string field);

    /// <summary>Removes <paramref name="field"/> from every issue; returns how many values were removed.</summary>
    int Clear(string field);
}

/// <summary>What an environment without field support offers: nothing is declared, so every call declines.</summary>
public sealed class UnavailablePluginFields : IPluginFields
{
    public static UnavailablePluginFields Instance { get; } = new();

    public bool Set(Issue issue, string field, string? value) => false;

    public string? Get(Issue issue, string field) => null;

    public int Clear(string field) => 0;
}

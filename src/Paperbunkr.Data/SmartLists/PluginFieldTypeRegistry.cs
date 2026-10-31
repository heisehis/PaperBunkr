using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>One plugin smart field as the Smart List engine and editor see it: the custom-value name its values live under, its label and type.</summary>
public sealed record PluginSmartField(string StorageName, string Label, SmartListDataType Type);

/// <summary>
/// The plugin smart fields currently declared by enabled plugins (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-
/// pitch-completion-design.md §D1). The plugin host refills it after every discovery; the Smart List engine reads it so a
/// <see cref="SmartListField.CustomValue"/> condition on a declared field compares as the field's type, and the editor lists the fields.
/// A name that isn't registered (its plugin was removed) keeps the plain text comparison every custom value has always had.
/// </summary>
public static class PluginFieldTypeRegistry
{
    private static volatile IReadOnlyDictionary<string, PluginSmartField> _fields = new Dictionary<string, PluginSmartField>();

    /// <summary>Replaces every registered field at once (a discovery is a full rescan).</summary>
    public static void Replace(IEnumerable<PluginSmartField> fields) =>
        _fields = fields.GroupBy(f => f.StorageName).ToDictionary(g => g.Key, g => g.First());

    public static IReadOnlyList<PluginSmartField> All => _fields.Values.OrderBy(f => f.Label).ToList();

    public static bool TryGet(string? storageName, out PluginSmartField field)
    {
        if (storageName is not null && _fields.TryGetValue(storageName, out PluginSmartField? found))
        {
            field = found;
            return true;
        }

        field = null!;
        return false;
    }
}

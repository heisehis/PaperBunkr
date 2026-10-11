using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists.CeImport;

/// <summary>
/// Maps the <c>PluginKey</c> of a ComicRack <c>ComicBookPluginMatcher</c> to a native smart-list condition
/// (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md). CE ran an IronPython script over the books the earlier matchers had kept; we cannot run
/// those, so a key only imports when a native equivalent has been registered here. <see cref="Default"/> starts empty: the proposed-values field and the
/// same-X-different-Y condition register themselves when they exist. An unregistered key makes the reader drop the condition and warn.
/// </summary>
public sealed class PluginMatcherRegistry
{
    private readonly Dictionary<string, Func<string, string, SmartListCondition?>> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registry the importer uses: the scripts that have a native equivalent (<c>findProposedValues</c> is <see cref="SmartListField.HasProposedValue"/>).</summary>
    public static PluginMatcherRegistry Default { get; } = CreateDefault();

    private static PluginMatcherRegistry CreateDefault()
    {
        var registry = new PluginMatcherRegistry();

        // ComicRack's "Proposed Values" script: MatchValue is its comma list of fields (docs/superpowers/specs/2026-10-11-proposed-values-design.md).
        registry.Register("findProposedValues", (fields, _) => new SmartListCondition
        {
            Field = SmartListField.HasProposedValue,
            Operator = SmartListOperator.Is,
            Value = fields,
        });
        // ComicRack's "Same X and different Y" script: MatchValue is X, MatchValue2 is Y, both |-separated (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §1).
        registry.Register("SameXDifferentY", (x, y) => new SmartListCondition
        {
            Field = SmartListField.SameXDifferentY,
            Operator = SmartListOperator.Is,
            Value = x,
            Value2 = y,
        });
        return registry;
    }

    /// <summary>Registers <paramref name="factory"/> for <paramref name="pluginKey"/>; it receives the matcher's <c>MatchValue</c> and <c>MatchValue2</c> and returns the native condition, or null when those arguments cannot be expressed.</summary>
    public void Register(string pluginKey, Func<string, string, SmartListCondition?> factory)
    {
        lock (_factories)
        {
            _factories[pluginKey] = factory;
        }
    }

    public bool IsRegistered(string pluginKey)
    {
        lock (_factories)
        {
            return _factories.ContainsKey(pluginKey);
        }
    }

    /// <summary>The native condition for the plugin matcher, or null when the key is unknown or the factory cannot express the arguments.</summary>
    public SmartListCondition? TryCreate(string? pluginKey, string value, string value2)
    {
        if (string.IsNullOrEmpty(pluginKey))
        {
            return null;
        }

        Func<string, string, SmartListCondition?>? factory;
        lock (_factories)
        {
            _factories.TryGetValue(pluginKey, out factory);
        }

        return factory?.Invoke(value, value2);
    }
}

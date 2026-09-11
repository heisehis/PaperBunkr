using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// One plugin-facing rule condition (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-
/// design.md §4). Deliberately a lightweight DTO, not the <c>SmartListCondition</c> EF entity, so a
/// plugin can build and evaluate a throwaway rule with no write access to the <c>SmartList</c>
/// tables. Field-for-field the same shape <c>SmartListCondition</c> carries.
/// </summary>
public sealed record PluginCondition(
    SmartListField Field,
    SmartListOperator Op,
    string Value,
    string? Value2 = null,
    string? CustomValueName = null,
    int? VirtualTagId = null,
    SearchMode? SearchMode = null,
    bool Not = false,
    bool IgnoreCase = true);

/// <summary>
/// A plugin-facing AND/OR group (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-
/// design.md §4). Intentionally mirrors <c>SmartListConditionGroup</c>'s shape
/// (<see cref="Mode"/>/<see cref="Conditions"/>/<see cref="ChildGroups"/>) so the adapter's
/// translation into what <c>SmartListQueryBuilder</c> consumes is closer to a projection than a
/// rewrite. If the SmartList Engine v2 nested-group work isn't present, an
/// <see cref="SmartListGroupMode.And"/> group with an empty <see cref="ChildGroups"/> is exactly
/// the pre-v2 flat-AND shape.
/// </summary>
public sealed record PluginConditionGroup(
    SmartListGroupMode Mode,
    IReadOnlyList<PluginCondition> Conditions,
    IReadOnlyList<PluginConditionGroup> ChildGroups)
{
    /// <summary>Convenience for the common flat case: a single AND group over <paramref name="conditions"/>.</summary>
    public static PluginConditionGroup And(params PluginCondition[] conditions) =>
        new(SmartListGroupMode.And, conditions, Array.Empty<PluginConditionGroup>());

    /// <summary>Convenience for a single OR group over <paramref name="conditions"/>.</summary>
    public static PluginConditionGroup Or(params PluginCondition[] conditions) =>
        new(SmartListGroupMode.Or, conditions, Array.Empty<PluginConditionGroup>());
}

/// <summary>
/// Runs the app's own Smart List matcher for a plugin (docs/superpowers/specs/2026-08-28-plugin-
/// api-v3-data-manager-design.md §4) instead of the plugin hand-rolling filtering that might not
/// agree with what the real Smart Lists screen computes. The adapter translates the DTOs above
/// into the exact in-memory shape <c>SmartListQueryBuilder.Build</c> already consumes and calls it
/// directly — zero duplicated matching logic, by construction.
/// </summary>
public interface IRulesEngine
{
    /// <summary>The library issues matching <paramref name="rule"/>.</summary>
    IReadOnlyList<Issue> Evaluate(PluginConditionGroup rule);

    /// <summary>
    /// The issues a saved <c>SmartList</c> currently matches, by its Id — the common Data Manager
    /// case ("give me what Smart List #N matches") without re-describing the rule. Returns an empty
    /// list if no such list exists.
    /// </summary>
    IReadOnlyList<Issue> EvaluateSmartList(int smartListId);
}

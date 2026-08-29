using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IRulesEngine"/> (docs/superpowers/specs/2026-08-28-plugin-api-v3-
/// data-manager-design.md §4). Translates the plugin-facing <see cref="PluginConditionGroup"/> /
/// <see cref="PluginCondition"/> DTOs into the <see cref="SmartListConditionGroup"/> /
/// <see cref="SmartListCondition"/> shape <see cref="SmartListQueryBuilder.Build"/> already consumes
/// and calls it directly — no duplicated matching logic.
///
/// Because <see cref="PluginConditionGroup"/> intentionally mirrors
/// <see cref="SmartListConditionGroup"/>'s shape (<c>Mode</c>/<c>Conditions</c>/<c>ChildGroups</c>),
/// <see cref="ToGroup"/> is closer to a projection than a rewrite. <see cref="SmartListQueryBuilder"/>
/// is <see langword="internal"/> to <c>Paperbunkr.Data</c> (§7) and reachable here via
/// <c>InternalsVisibleTo("Paperbunkr.App")</c> — never directly from a plugin script.
/// </summary>
public sealed class PaperbunkrRulesEngine : IRulesEngine
{
    public IReadOnlyList<Issue> Evaluate(PluginConditionGroup rule)
    {
        using var context = PaperbunkrDb.CreateContext();
        var transient = new SmartList { Name = "(plugin rule)", RootGroup = ToGroup(rule) };
        return SmartListQueryBuilder.Build(context, transient);
    }

    public IReadOnlyList<Issue> EvaluateSmartList(int smartListId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = SmartListTreeLoader.LoadWithTree(context, smartListId);
        return list is null ? new List<Issue>() : SmartListQueryBuilder.Build(context, list);
    }

    private static SmartListConditionGroup ToGroup(PluginConditionGroup source)
    {
        var group = new SmartListConditionGroup { Mode = source.Mode };

        int order = 0;
        foreach (var condition in source.Conditions ?? Enumerable.Empty<PluginCondition>())
        {
            group.Conditions.Add(new SmartListCondition
            {
                Field = condition.Field,
                Operator = condition.Op,
                Value = condition.Value ?? string.Empty,
                Value2 = condition.Value2,
                CustomValueName = condition.CustomValueName,
                VirtualTagId = condition.VirtualTagId,
                SearchMode = condition.SearchMode,
                Not = condition.Not,
                IgnoreCase = condition.IgnoreCase,
                SortOrder = order++,
            });
        }

        order = 0;
        foreach (var child in source.ChildGroups ?? Enumerable.Empty<PluginConditionGroup>())
        {
            var childGroup = ToGroup(child);
            childGroup.SortOrder = order++;
            group.ChildGroups.Add(childGroup);
        }

        return group;
    }
}

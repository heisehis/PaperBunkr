using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Evaluates a <see cref="SmartListTargetKind.Series"/> list's condition tree against
/// <see cref="Series"/> rows (docs/superpowers/specs/2026-08-30-smart-collections-design.md).
/// Mirrors <see cref="SmartListQueryBuilder"/>'s shape — group-combination logic
/// (<see cref="EvaluateGroup"/>) is copied rather than shared (it's the part that genuinely differs
/// per kind: which entity's rows get filtered), while leaf operator evaluation is shared via
/// <see cref="SmartListLeafEvaluator"/>.
/// </summary>
internal static class SeriesSmartListQueryBuilder
{
    public sealed class SeriesSnapshot
    {
        public required IReadOnlyList<Series> SeriesList { get; init; }
    }

    public static SeriesSnapshot LoadSnapshot(PaperbunkrDbContext ctx, IReadOnlyCollection<SmartListCondition> conditions)
    {
        var query = ctx.Series.AsSplitQuery();

        // Same Include-gating idiom as the Issue builder: the common list doesn't touch Continuity,
        // so don't pay for the join when nothing needs it.
        if (conditions.Any(c => c.Field == SmartListField.Continuity))
        {
            query = query.Include(s => s.ContinuityMemberships).ThenInclude(m => m.Continuity).AsSplitQuery();
        }

        return new SeriesSnapshot { SeriesList = query.ToList() };
    }

    public static List<Series> Evaluate(SeriesSnapshot snapshot, SmartList list) =>
        snapshot.SeriesList.Where(s => EvaluateGroup(s, list.RootGroup)).ToList();

    public static List<Series> Build(PaperbunkrDbContext ctx, SmartList list) =>
        Evaluate(LoadSnapshot(ctx, SmartListQueryBuilder.Flatten(list.RootGroup).ToList()), list);

    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => Build(ctx, list).Count;

    private static bool EvaluateGroup(Series series, SmartListConditionGroup group)
    {
        IEnumerable<bool> results = group.Conditions
            .OrderBy(c => c.SortOrder)
            .Select(c => EvaluateCondition(series, c) ^ c.Not)
            .Concat(group.ChildGroups
                .OrderBy(g => g.SortOrder)
                .Select(g => EvaluateGroup(series, g)));

        return group.Mode == SmartListGroupMode.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool EvaluateCondition(Series series, SmartListCondition condition)
    {
        var definition = SeriesSmartListCatalog.Definitions[condition.Field];
        return definition.DataType switch
        {
            SmartListDataType.Text => SmartListLeafEvaluator.EvaluateText(SeriesSmartListCatalog.TextSelectors[condition.Field](series), condition),
            SmartListDataType.Toggle => SmartListLeafEvaluator.EvaluateToggle(SeriesSmartListCatalog.ToggleSelectors[condition.Field](series), condition),
            _ => false,
        };
    }
}

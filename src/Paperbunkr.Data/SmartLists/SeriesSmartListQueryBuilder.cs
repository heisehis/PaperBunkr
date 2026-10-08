using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

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

        /// <summary>Per-series reading progress (series id → progress); only loaded when a condition uses a progress field.</summary>
        public IReadOnlyDictionary<int, SeriesProgress>? Progress { get; init; }

        /// <summary>The clock <see cref="SmartListField.DaysSinceLastRead"/> is measured against (injectable for tests).</summary>
        public DateTime NowUtc { get; init; } = DateTime.UtcNow;
    }

    public static SeriesSnapshot LoadSnapshot(PaperbunkrDbContext ctx, IReadOnlyCollection<SmartListCondition> conditions, DateTime? nowUtc = null)
    {
        var query = ctx.Series.AsSplitQuery();

        // Same Include-gating idiom as the Issue builder: the common list doesn't touch Continuity,
        // so don't pay for the join when nothing needs it.
        if (conditions.Any(c => c.Field == SmartListField.Continuity))
        {
            query = query.Include(s => s.ContinuityMemberships).ThenInclude(m => m.Continuity).AsSplitQuery();
        }

        // Smart features S1: the progress fields read the series' issues and its newest reading event, so (gated the same way) load them
        // only when a condition needs one.
        bool needsProgress = conditions.Any(c => SeriesSmartListCatalog.IsProgressField(c.Field));
        if (needsProgress)
        {
            query = query.Include(s => s.Issues).AsSplitQuery();
        }

        var list = query.ToList();
        Dictionary<int, SeriesProgress>? progress = null;
        if (needsProgress)
        {
            var lastEvent = ctx.ReadingEvents
                .Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId != null)
                .GroupBy(e => e.SeriesId!.Value)
                .Select(g => new { SeriesId = g.Key, Last = g.Max(e => e.TimestampUtc) })
                .ToDictionary(x => x.SeriesId, x => x.Last);

            progress = list.ToDictionary(
                s => s.Id,
                s => SeriesProgress.Of(s.Issues, lastEvent.TryGetValue(s.Id, out var last) ? last : null));
        }

        return new SeriesSnapshot { SeriesList = list, Progress = progress, NowUtc = nowUtc ?? DateTime.UtcNow };
    }

    public static List<Series> Evaluate(SeriesSnapshot snapshot, SmartList list) =>
        snapshot.SeriesList.Where(s => EvaluateGroup(s, list.RootGroup, snapshot)).ToList();

    public static List<Series> Build(PaperbunkrDbContext ctx, SmartList list) =>
        Evaluate(LoadSnapshot(ctx, SmartListQueryBuilder.Flatten(list.RootGroup).ToList()), list);

    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => Build(ctx, list).Count;

    private static bool EvaluateGroup(Series series, SmartListConditionGroup group, SeriesSnapshot snapshot)
    {
        IEnumerable<bool> results = group.Conditions
            .OrderBy(c => c.SortOrder)
            .Select(c => EvaluateCondition(series, c, snapshot) ^ c.Not)
            .Concat(group.ChildGroups
                .OrderBy(g => g.SortOrder)
                .Select(g => EvaluateGroup(series, g, snapshot)));

        return group.Mode == SmartListGroupMode.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool EvaluateCondition(Series series, SmartListCondition condition, SeriesSnapshot snapshot)
    {
        var definition = SeriesSmartListCatalog.Definitions[condition.Field];
        return definition.DataType switch
        {
            SmartListDataType.Text => SmartListLeafEvaluator.EvaluateText(SeriesSmartListCatalog.TextSelectors[condition.Field](series), condition),
            SmartListDataType.Toggle => SmartListLeafEvaluator.EvaluateToggle(SeriesSmartListCatalog.ToggleSelectors[condition.Field](series), condition),
            SmartListDataType.Number => EvaluateProgress(series, condition, snapshot),
            _ => false,
        };
    }

    /// <summary>A progress field with no value (a never-opened series' days-since-last-read) never matches a number condition.</summary>
    private static bool EvaluateProgress(Series series, SmartListCondition condition, SeriesSnapshot snapshot)
    {
        if (snapshot.Progress is null || !snapshot.Progress.TryGetValue(series.Id, out var progress))
        {
            return false;
        }

        return SeriesSmartListCatalog.ProgressValue(condition.Field, progress, snapshot.NowUtc) is float value
            && SmartListLeafEvaluator.EvaluateNumber(value, condition);
    }
}

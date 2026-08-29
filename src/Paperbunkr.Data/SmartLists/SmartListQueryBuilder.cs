using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Evaluates a <see cref="SmartList"/>'s AND-ed conditions against the library. In-memory
/// predicate evaluation over the fully materialized issue set — see
/// <see cref="SmartListCatalog"/>'s remarks for why that's the CE-faithful choice here, not just a
/// convenience.
///
/// The library load is a <see cref="LibrarySnapshot"/> — issues plus whatever derived data
/// (duplicate ids, virtual-tag definitions, continuity memberships) the conditions actually
/// reference. <see cref="LoadSnapshot"/> is split out so a caller with several lists to evaluate
/// (the Smart screen sidebar's live match counts) loads the library <b>once</b> and runs a cheap
/// in-memory <see cref="Evaluate"/> pass per list, instead of a full DB round-trip per list. Every
/// DB query here uses <see cref="RelationalQueryableExtensions.AsSplitQuery{TEntity}"/> — with
/// 2000+ issues, a single query carrying four collection <c>Include</c>s multiplies out to
/// millions of rows for EF to de-duplicate on the UI thread.
/// </summary>
public static class SmartListQueryBuilder
{
    /// <summary>One library load, reusable across many <see cref="Evaluate"/> passes.</summary>
    public sealed class LibrarySnapshot
    {
        public required IReadOnlyList<Issue> Issues { get; init; }
        public HashSet<int>? DuplicateIds { get; init; }
        public Dictionary<int, VirtualTagDefinition>? VirtualTags { get; init; }
    }

    /// <summary>
    /// Loads the issue set plus the derived data any of <paramref name="conditions"/> reference.
    /// Pass the union of every list's conditions when preparing a snapshot to be shared across
    /// lists.
    /// </summary>
    public static LibrarySnapshot LoadSnapshot(PaperbunkrDbContext ctx, IReadOnlyCollection<SmartListCondition> conditions)
    {
        // Include(Tags) - SmartListCatalog's Genre/Tags TextSelectors read Issue.Tags (docs/
        // superpowers/specs/2026-08-23-weighted-categorized-tags-design.md); without it every issue
        // would look like it has no Genre/Tags at all, silently breaking every Smart List condition
        // on either field.
        // MetadataProposals feeds Effective* (Number/Year/Volume + duplicate keys); Tags feeds the
        // Genre/Tags selectors - both broadly enough reached to always load. CustomValues is only
        // ever touched by the CustomValue / HasCustomValues fields, so it's the one include worth
        // gating (a library's worth of custom values is pure dead weight for the common list).
        bool needsCustomValues = conditions.Any(c => c.Field is SmartListField.CustomValue or SmartListField.HasCustomValues);

        var query = ctx.Issues
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .Include(i => i.Tags)
            .AsSplitQuery();

        if (needsCustomValues)
        {
            query = query.Include(i => i.CustomValues).AsSplitQuery();
        }

        var issues = query.ToList();

        // Continuity condition reads i.Series.ContinuityMemberships (docs/superpowers/specs/2026-08-27-
        // metadata-model-phase4f-continuity-browse-design.md) - loaded separately so the common
        // no-continuity-condition case doesn't pay for the join. EF's change tracker fixes these up
        // onto the Series instances already referenced by issues[].Series (queries here are tracked).
        if (conditions.Any(c => c.Field == SmartListField.Continuity))
        {
            ctx.Series.Include(s => s.ContinuityMemberships).ThenInclude(m => m.Continuity).AsSplitQuery().Load();
        }

        HashSet<int>? duplicateIds = conditions.Any(c => c.Field == SmartListField.Duplicate)
            ? DuplicateIssueIds(issues)
            : null;

        // Disabled tags are excluded, not just unpicklable in the rule-builder UI - a condition
        // saved against a tag that's since been disabled in Preferences stops matching too,
        // consistent with the Detail-screen pills surface also only showing enabled tags.
        Dictionary<int, VirtualTagDefinition>? virtualTags = conditions.Any(c => c.Field == SmartListField.VirtualTag)
            ? ctx.VirtualTagDefinitions.Where(v => v.IsEnabled).ToDictionary(v => v.Id)
            : null;

        return new LibrarySnapshot { Issues = issues, DuplicateIds = duplicateIds, VirtualTags = virtualTags };
    }

    /// <summary>Pure in-memory pass: the issues in <paramref name="snapshot"/> that satisfy every one of <paramref name="list"/>'s conditions.</summary>
    public static List<Issue> Evaluate(LibrarySnapshot snapshot, SmartList list)
    {
        IEnumerable<Issue> result = snapshot.Issues;
        foreach (var condition in list.Conditions.OrderBy(c => c.SortOrder))
        {
            result = result.Where(i => Evaluate(i, condition, snapshot.DuplicateIds, snapshot.VirtualTags));
        }

        return result.ToList();
    }

    public static List<Issue> Build(PaperbunkrDbContext ctx, SmartList list) =>
        Evaluate(LoadSnapshot(ctx, list.Conditions), list);

    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => Build(ctx, list).Count;

    /// <summary>
    /// Match counts for many lists off a single library load - one <see cref="LoadSnapshot"/> over
    /// the union of every list's conditions, then an in-memory <see cref="Evaluate"/> per list.
    /// Backs the Smart screen sidebar, which previously issued a full <see cref="Build"/> (its own
    /// DB round-trip, four collection includes) per list on every screen open.
    /// </summary>
    public static Dictionary<int, int> MatchCounts(PaperbunkrDbContext ctx, IReadOnlyCollection<SmartList> lists)
    {
        var union = lists.SelectMany(l => l.Conditions).ToList();
        var snapshot = LoadSnapshot(ctx, union);
        return lists.ToDictionary(l => l.Id, l => Evaluate(snapshot, l).Count);
    }

    private static bool Evaluate(Issue issue, SmartListCondition condition, HashSet<int>? duplicateIds, Dictionary<int, VirtualTagDefinition>? virtualTags)
    {
        if (condition.Field == SmartListField.Duplicate)
        {
            bool isDuplicate = duplicateIds?.Contains(issue.Id) ?? false;
            return condition.Operator == SmartListOperator.IsNot ? !isDuplicate : isDuplicate;
        }

        if (condition.Field == SmartListField.CustomValue)
        {
            return EvaluateCustomValue(issue, condition);
        }

        if (condition.Field == SmartListField.VirtualTag)
        {
            return EvaluateVirtualTag(issue, condition, virtualTags);
        }

        var definition = SmartListCatalog.Definitions[condition.Field];
        return definition.DataType switch
        {
            SmartListDataType.Text => EvaluateText(SmartListCatalog.TextSelectors[condition.Field](issue), condition),
            SmartListDataType.Number => EvaluateNumber(SmartListCatalog.NumberSelectors[condition.Field](issue), condition),
            SmartListDataType.Toggle => EvaluateToggle(SmartListCatalog.ToggleSelectors[condition.Field](issue), condition),
            SmartListDataType.Date => EvaluateDate(SmartListCatalog.DateSelectors[condition.Field](issue), condition),
            _ => false,
        };
    }

    private static bool EvaluateText(string value, SmartListCondition c)
    {
        const StringComparison sc = StringComparison.OrdinalIgnoreCase;
        return c.Operator switch
        {
            SmartListOperator.Is => value.Equals(c.Value, sc),
            SmartListOperator.IsNot => !value.Equals(c.Value, sc),
            SmartListOperator.Contains => value.Contains(c.Value, sc),
            SmartListOperator.ContainsAny => SplitValues(c.Value).Any(v => value.Contains(v, sc)),
            SmartListOperator.ContainsAll => SplitValues(c.Value).All(v => value.Contains(v, sc)),
            SmartListOperator.StartsWith => value.StartsWith(c.Value, sc),
            SmartListOperator.EndsWith => value.EndsWith(c.Value, sc),
            _ => false,
        };
    }

    private static IEnumerable<string> SplitValues(string raw) =>
        raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool EvaluateNumber(float value, SmartListCondition c)
    {
        float target = ParseFloat(c.Value);
        return c.Operator switch
        {
            SmartListOperator.Is => Math.Abs(value - target) < 0.0001f,
            SmartListOperator.IsNot => Math.Abs(value - target) >= 0.0001f,
            SmartListOperator.GreaterThan => value > target,
            SmartListOperator.LessThan => value < target,
            SmartListOperator.InRange => value >= target && value <= ParseFloat(c.Value2 ?? c.Value),
            _ => false,
        };
    }

    private static float ParseFloat(string s) => float.TryParse(s, out var f) ? f : 0f;

    private static bool EvaluateToggle(bool value, SmartListCondition c)
    {
        bool target = bool.TryParse(c.Value, out var b) && b;
        return c.Operator == SmartListOperator.IsNot ? value != target : value == target;
    }

    private static bool EvaluateDate(DateTime? value, SmartListCondition c)
    {
        // An unset date never matches a date condition — there's nothing to compare.
        if (value is not { } dt)
        {
            return false;
        }

        return c.Operator switch
        {
            SmartListOperator.Is => DateTime.TryParse(c.Value, out var eq) && dt.Date == eq.Date,
            SmartListOperator.IsAfter => DateTime.TryParse(c.Value, out var after) && dt > after,
            SmartListOperator.IsBefore => DateTime.TryParse(c.Value, out var before) && dt < before,
            SmartListOperator.WithinLastDays => int.TryParse(c.Value, out var days) && dt >= DateTime.Now.AddDays(-days),
            SmartListOperator.DateInRange => DateTime.TryParse(c.Value, out var start)
                && DateTime.TryParse(c.Value2, out var end) && dt >= start && dt <= end,
            _ => false,
        };
    }

    private static bool EvaluateCustomValue(Issue issue, SmartListCondition c)
    {
        var match = issue.CustomValues.FirstOrDefault(cv => cv.Name == c.CustomValueName);
        return match is not null && EvaluateText(match.Value, c);
    }

    /// <summary>
    /// A disabled/deleted tag (id no longer in <paramref name="virtualTags"/>) never matches -
    /// same "nothing to compare" contract as <see cref="EvaluateCustomValue"/> and
    /// <see cref="EvaluateDate"/>'s unset-date case, rather than throwing on stale references left
    /// behind after a tag is disabled or removed in Preferences.
    /// </summary>
    private static bool EvaluateVirtualTag(Issue issue, SmartListCondition c, Dictionary<int, VirtualTagDefinition>? virtualTags)
    {
        if (c.VirtualTagId is not int id || virtualTags is null || !virtualTags.TryGetValue(id, out var tag))
        {
            return false;
        }

        string value = VirtualTagTemplateEvaluator.Evaluate(tag.CaptionFormat, issue, issue.Series);
        return EvaluateText(value, c);
    }

    /// <summary>
    /// CE's exact dual-key duplicate detection (<c>ComicBookDuplicateMatcher</c>, confirmed via
    /// direct source check): union of (a) a metadata-key group — Series + Format + Count + Number +
    /// Volume + LanguageISO + Year + Month + Day — and (b) a FilePath group, each with
    /// <c>Count() &gt; 1</c>, concatenated and de-duplicated so an issue matching both keys isn't
    /// double-counted.
    /// </summary>
    public static HashSet<int> DuplicateIssueIds(IReadOnlyCollection<Issue> issues)
    {
        // Number/Volume/Year read through Effective* (Phase 2a) so a filename-inferred value still
        // counts toward the dedup key, not just a stored one.
        var byMetadata = issues
            .GroupBy(i => (i.SeriesId, i.Format, i.Count, i.EffectiveNumber(), i.EffectiveVolume(), i.LanguageISO, i.EffectiveYear(), i.Month, i.Day))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);

        var byPath = issues
            .Where(i => !string.IsNullOrEmpty(i.FilePath))
            .GroupBy(i => i.FilePath)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);

        return byMetadata.Concat(byPath).Select(i => i.Id).ToHashSet();
    }
}

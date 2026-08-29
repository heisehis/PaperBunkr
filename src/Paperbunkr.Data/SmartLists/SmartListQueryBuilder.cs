using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Evaluates a <see cref="SmartList"/>'s nested AND/OR condition tree against the library
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2). In-memory predicate
/// evaluation over the fully materialized issue set — see <see cref="SmartListCatalog"/>'s remarks
/// for why that's the CE-faithful choice here, not just a convenience.
///
/// <see cref="Build"/> is recursive: a <see cref="SmartListConditionGroup"/> matches an issue when
/// its own <see cref="SmartListConditionGroup.Conditions"/> (each XOR'd with its
/// <see cref="SmartListCondition.Not"/> flag) and its <see cref="SmartListConditionGroup.ChildGroups"/>
/// (recursively), combined by <see cref="SmartListConditionGroup.Mode"/>, agree. Leaf-condition
/// evaluation (<see cref="EvaluateText"/> etc.) is unchanged from v1 apart from §3's new operators.
///
/// Callers must load the full tree first (<see cref="SmartListTreeLoader"/>); an in-memory list
/// with its <see cref="SmartList.RootGroup"/> populated works directly.
///
/// The library load is a <see cref="LibrarySnapshot"/> — issues plus whatever derived data
/// (duplicate ids, virtual-tag definitions, continuity memberships) the tree's conditions actually
/// reference. <see cref="LoadSnapshot"/> is split out so a caller with several lists to evaluate
/// (the Smart screen sidebar's live match counts) loads the library <b>once</b> and runs a cheap
/// in-memory <see cref="Evaluate"/> pass per list, instead of a full DB round-trip per list. Every
/// DB query here uses <see cref="RelationalQueryableExtensions.AsSplitQuery{TEntity}"/> — with
/// 2000+ issues, a single query carrying several collection <c>Include</c>s multiplies out to
/// millions of rows for EF to de-duplicate on the UI thread.
///
/// <b>Visibility:</b> <see langword="internal"/> (docs/superpowers/specs/2026-08-28-plugin-api-v3-
/// data-manager-design.md §7) — the app's <c>IRulesEngine</c> adapter reaches it via
/// <c>InternalsVisibleTo("Paperbunkr.App")</c>, but a plugin <c>.csx</c> script referencing
/// <c>Paperbunkr.Data.dll</c> can no longer resolve it. A plugin evaluates rules through
/// <c>IRulesEngine</c>, never this type directly.
/// </summary>
internal static class SmartListQueryBuilder
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
    /// Pass the flattened union of every list's condition tree (<see cref="Flatten"/>) when
    /// preparing a snapshot to be shared across lists.
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

    /// <summary>
    /// Pure in-memory pass: the issues in <paramref name="snapshot"/> that satisfy
    /// <paramref name="list"/>'s condition tree (<see cref="EvaluateGroup"/> over
    /// <see cref="SmartList.RootGroup"/>).
    /// </summary>
    public static List<Issue> Evaluate(LibrarySnapshot snapshot, SmartList list)
    {
        var evalContext = new EvalContext(snapshot.DuplicateIds, snapshot.VirtualTags);
        return snapshot.Issues.Where(i => EvaluateGroup(i, list.RootGroup, evalContext)).ToList();
    }

    public static List<Issue> Build(PaperbunkrDbContext ctx, SmartList list) =>
        Evaluate(LoadSnapshot(ctx, Flatten(list.RootGroup).ToList()), list);

    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => Build(ctx, list).Count;

    /// <summary>
    /// Match counts for many lists off a single library load - one <see cref="LoadSnapshot"/> over
    /// the union of every list's condition tree, then an in-memory <see cref="Evaluate"/> per list.
    /// Backs the Smart screen sidebar, which previously issued a full <see cref="Build"/> (its own
    /// DB round-trip, several collection includes) per list on every screen open.
    /// </summary>
    public static Dictionary<int, int> MatchCounts(PaperbunkrDbContext ctx, IReadOnlyCollection<SmartList> lists)
    {
        var union = lists.SelectMany(l => Flatten(l.RootGroup)).ToList();
        var snapshot = LoadSnapshot(ctx, union);
        return lists.ToDictionary(l => l.Id, l => Evaluate(snapshot, l).Count);
    }

    /// <summary>Every condition anywhere in the tree — used for Include-gating and quick "does this list touch field X" checks.</summary>
    public static IEnumerable<SmartListCondition> Flatten(SmartListConditionGroup group)
    {
        foreach (var condition in group.Conditions)
        {
            yield return condition;
        }

        foreach (var child in group.ChildGroups)
        {
            foreach (var condition in Flatten(child))
            {
                yield return condition;
            }
        }
    }

    private sealed record EvalContext(HashSet<int>? DuplicateIds, Dictionary<int, VirtualTagDefinition>? VirtualTags);

    /// <summary>
    /// A group matches when its conditions (XOR'd with <see cref="SmartListCondition.Not"/>) and
    /// child groups, combined by <see cref="SmartListConditionGroup.Mode"/>, agree. An empty
    /// <see cref="SmartListGroupMode.And"/> group matches every issue (vacuous truth — matches CE's
    /// <c>ComicBookGroupMatcher.Match</c>, which returns all items when it holds no matchers, and the
    /// v1 "a list with no conditions matches everything" behavior).
    /// </summary>
    private static bool EvaluateGroup(Issue issue, SmartListConditionGroup group, EvalContext ctx)
    {
        IEnumerable<bool> results = group.Conditions
            .OrderBy(c => c.SortOrder)
            .Select(c => EvaluateCondition(issue, c, ctx) ^ c.Not)
            .Concat(group.ChildGroups
                .OrderBy(g => g.SortOrder)
                .Select(g => EvaluateGroup(issue, g, ctx)));

        return group.Mode == SmartListGroupMode.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool EvaluateCondition(Issue issue, SmartListCondition condition, EvalContext ctx)
    {
        if (condition.Field == SmartListField.Duplicate)
        {
            bool isDuplicate = ctx.DuplicateIds?.Contains(issue.Id) ?? false;
            return condition.Operator == SmartListOperator.IsNot ? !isDuplicate : isDuplicate;
        }

        if (condition.Field == SmartListField.CustomValue)
        {
            return EvaluateCustomValue(issue, condition);
        }

        if (condition.Field == SmartListField.VirtualTag)
        {
            return EvaluateVirtualTag(issue, condition, ctx.VirtualTags);
        }

        if (condition.Field == SmartListField.AllProperties)
        {
            return EvaluateAllProperties(issue, condition);
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

    /// <summary>
    /// <see cref="SmartListField.AllProperties"/> (docs/superpowers/specs/2026-08-28-smartlist-engine-
    /// v2-design.md §4): match if <em>any</em> field value in the <see cref="SmartListCondition.SearchMode"/>
    /// bundle satisfies the condition's operator. Same <see cref="EvaluateText"/> codepath as every
    /// other Text condition, so List Contains / Regular Expression / case-sensitivity all apply here
    /// for free.
    /// </summary>
    private static bool EvaluateAllProperties(Issue issue, SmartListCondition condition) =>
        SearchFieldBundleCatalog.For(condition.SearchMode)(issue)
            .Any(value => EvaluateText(value ?? string.Empty, condition));

    private static bool EvaluateText(string value, SmartListCondition c)
    {
        StringComparison sc = c.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return c.Operator switch
        {
            SmartListOperator.Is => value.Equals(c.Value, sc),
            SmartListOperator.IsNot => !value.Equals(c.Value, sc),
            SmartListOperator.Contains => value.Contains(c.Value, sc),
            SmartListOperator.ContainsAny => SplitValues(c.Value).Any(v => value.Contains(v, sc)),
            SmartListOperator.ContainsAll => SplitValues(c.Value).All(v => value.Contains(v, sc)),
            SmartListOperator.StartsWith => value.StartsWith(c.Value, sc),
            SmartListOperator.EndsWith => value.EndsWith(c.Value, sc),
            SmartListOperator.ListContains => ListContains(value, c),
            SmartListOperator.RegularExpression => RegexMatches(value, c),
            _ => false,
        };
    }

    /// <summary>
    /// CE operator 6 (<c>_reference/ComicRackCE/ComicRack.Engine/ComicBookStringMatcher.cs:114-118,152</c>):
    /// the match value is one whole <c>,</c>/<c>;</c>-delimited item of <paramref name="value"/>,
    /// surrounding whitespace ignored — the same delimiter set <see cref="SplitValues"/> and the
    /// <c>JoinedTags()</c>/<c>JoinedGenre()</c> helpers use. Unlike CE (whose <c>rxList</c> is
    /// hard-wired <c>RegexOptions.IgnoreCase</c>), this honours <see cref="SmartListCondition.IgnoreCase"/>
    /// per the v2 spec's "case-sensitivity-aware" wording.
    /// </summary>
    private static bool ListContains(string value, SmartListCondition c)
    {
        string needle = c.Value.Trim();
        var comparer = c.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => comparer.Equals(item, needle));
    }

    /// <summary>
    /// CE operator 7 — a raw .NET regex. Bounded by a 250ms timeout (the same "thousands not
    /// millions" per-condition budget <see cref="SmartListCatalog"/> documents); a
    /// <see cref="RegexParseException"/>/<see cref="ArgumentException"/> (malformed pattern) or a
    /// <see cref="RegexMatchTimeoutException"/> is treated as "no match" rather than surfaced as an
    /// app error — same "never let one bad input crash the host" spirit as the plugin engine.
    /// </summary>
    private static bool RegexMatches(string value, SmartListCondition c)
    {
        try
        {
            var options = c.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            return Regex.IsMatch(value, c.Value, options, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
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

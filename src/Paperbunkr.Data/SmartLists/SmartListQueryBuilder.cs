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

        /// <summary>Library-wide series figures for the <c>Series*</c> fields, null when no condition uses one.</summary>
        public Dictionary<SeriesStatistics.Key, SeriesStatistics.Stats>? SeriesStats { get; init; }
    }

    /// <summary>The four CE "Series: …" fields, which read figures computed over the whole library rather than one issue.</summary>
    private static bool IsSeriesStatField(SmartListField field) =>
        field is SmartListField.SeriesLastNumber or SmartListField.SeriesMaxYear or SmartListField.SeriesMaxCount or SmartListField.SeriesMaxGapSize
            or SmartListField.SeriesLastAddedTime or SmartListField.SeriesLastOpenedTime or SmartListField.SeriesLastPublishedTime or SmartListField.SeriesLastReleasedTime;

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
        bool needsCustomValues = conditions.Any(c => c.Field is SmartListField.CustomValue or SmartListField.HasCustomValues or SmartListField.SameXDifferentY);

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

        var seriesStats = conditions.Any(c => IsSeriesStatField(c.Field)) ? SeriesStatistics.Compute(issues) : null;

        return new LibrarySnapshot { Issues = issues, DuplicateIds = duplicateIds, VirtualTags = virtualTags, SeriesStats = seriesStats };
    }

    /// <summary>
    /// Pure in-memory pass: the issues in <paramref name="snapshot"/> that satisfy
    /// <paramref name="list"/>'s condition tree (<see cref="EvaluateGroup"/> over
    /// <see cref="SmartList.RootGroup"/>).
    /// </summary>
    public static List<Issue> Evaluate(LibrarySnapshot snapshot, SmartList list)
    {
        var evalContext = new EvalContext(snapshot.DuplicateIds, snapshot.VirtualTags, snapshot.SeriesStats, SameXGroups(snapshot.Issues, list.RootGroup));
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

    private sealed record EvalContext(
        HashSet<int>? DuplicateIds,
        Dictionary<int, VirtualTagDefinition>? VirtualTags,
        Dictionary<SeriesStatistics.Key, SeriesStatistics.Stats>? SeriesStats = null,
        Dictionary<object, HashSet<int>>? SameXIds = null);

    /// <summary>
    /// The flagged books of every "same X, different Y" condition in the tree, worked out over the whole library once per evaluation (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §1).
    /// Keyed by the condition object itself, since two conditions of one list can have different X and Y.
    /// </summary>
    private static Dictionary<object, HashSet<int>>? SameXGroups(IReadOnlyList<Issue> issues, SmartListConditionGroup root)
    {
        var conditions = Flatten(root).Where(c => c.Field == SmartListField.SameXDifferentY).ToList();
        if (conditions.Count == 0)
        {
            return null;
        }

        var groups = new Dictionary<object, HashSet<int>>(ReferenceEqualityComparer.Instance);
        foreach (var condition in conditions)
        {
            groups[condition] = SameXDifferentY.Compute(issues, condition.Value, condition.Value2);
        }

        return groups;
    }

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

        if (IsSeriesStatField(condition.Field))
        {
            if (condition.Field is SmartListField.SeriesLastAddedTime or SmartListField.SeriesLastOpenedTime or SmartListField.SeriesLastPublishedTime or SmartListField.SeriesLastReleasedTime)
                return SmartListLeafEvaluator.EvaluateDate(SeriesDateValue(issue, condition.Field, ctx.SeriesStats), condition);
            return SmartListLeafEvaluator.EvaluateNumber(SeriesStatValue(issue, condition.Field, ctx.SeriesStats), condition);
        }

        if (condition.Field == SmartListField.SameXDifferentY)
        {
            bool flagged = ctx.SameXIds is not null && ctx.SameXIds.TryGetValue(condition, out var ids) && ids.Contains(issue.Id);
            return condition.Operator == SmartListOperator.IsNot ? !flagged : flagged;
        }

        if (condition.Field == SmartListField.HasProposedValue)
        {
            bool has = ProposedValues.Has(issue, ProposedValues.ParseFields(condition.Value));
            return condition.Operator == SmartListOperator.IsNot ? !has : has;
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
            SmartListDataType.Text => SmartListLeafEvaluator.EvaluateText(SmartListCatalog.TextSelectors[condition.Field](issue), condition),
            SmartListDataType.Number => SmartListLeafEvaluator.EvaluateNumber(SmartListCatalog.NumberSelectors[condition.Field](issue), condition),
            SmartListDataType.Toggle => SmartListLeafEvaluator.EvaluateToggle(SmartListCatalog.ToggleSelectors[condition.Field](issue), condition),
            SmartListDataType.Date => SmartListLeafEvaluator.EvaluateDate(SmartListCatalog.DateSelectors[condition.Field](issue), condition),
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
            .Any(value => SmartListLeafEvaluator.EvaluateText(value ?? string.Empty, condition));

    /// <summary>
    /// CE's <c>ComicBookCustomValuesMatcher</c> reads <c>GetCustomValue(name)</c>, which is null for a custom value the comic does not have, and the string
    /// matcher compares <c>value ?? ""</c> (<c>ComicBookStringMatcher.MatchBook</c>). So a missing custom value is the empty string: "is ''" matches every comic
    /// without it, which is how ComicRack lists spell "not tagged yet".
    /// </summary>
    private static bool EvaluateCustomValue(Issue issue, SmartListCondition c)
    {
        var match = issue.CustomValues.FirstOrDefault(cv => cv.Name == c.CustomValueName);

        // A plugin smart field (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §D1) compares
        // as its declared type. Unlike text, a missing or unreadable number/date/toggle has nothing to compare and never matches.
        if (PluginFieldTypeRegistry.TryGet(c.CustomValueName, out var pluginField) && pluginField.Type != SmartListDataType.Text)
        {
            string? raw = match?.Value;
            return pluginField.Type switch
            {
                SmartListDataType.Number => float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n)
                                            && SmartListLeafEvaluator.EvaluateNumber(n, c),
                SmartListDataType.Date => DateTime.TryParseExact(raw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime d)
                                          && SmartListLeafEvaluator.EvaluateDate(d, c),
                SmartListDataType.Toggle => bool.TryParse(raw, out bool b) && SmartListLeafEvaluator.EvaluateToggle(b, c),
                _ => false,
            };
        }

        return SmartListLeafEvaluator.EvaluateText(match?.Value ?? string.Empty, c);
    }

    /// <summary>The group's figure for <paramref name="field"/>; CE's invalid value (-1) for a comic with no statistics (a stale snapshot).</summary>
    private static float SeriesStatValue(Issue issue, SmartListField field, Dictionary<SeriesStatistics.Key, SeriesStatistics.Stats>? stats)
    {
        if (stats is null || !stats.TryGetValue(SeriesStatistics.KeyOf(issue), out var s))
        {
            return -1f;
        }

        return field switch
        {
            SmartListField.SeriesLastNumber => s.LastNumber,
            SmartListField.SeriesMaxYear => s.MaxYear,
            SmartListField.SeriesMaxCount => s.MaxCount,
            _ => s.MaxGapSize,
        };
    }

    private static DateTime? SeriesDateValue(Issue issue, SmartListField field, Dictionary<SeriesStatistics.Key, SeriesStatistics.Stats>? stats)
    {
        if (stats is null || !stats.TryGetValue(SeriesStatistics.KeyOf(issue), out var s)) return null;
        return field switch
        {
            SmartListField.SeriesLastAddedTime => s.LastAdded,
            SmartListField.SeriesLastOpenedTime => s.LastOpened,
            SmartListField.SeriesLastPublishedTime => s.LastPublished,
            _ => s.LastReleased,
        };
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
        return SmartListLeafEvaluator.EvaluateText(value, c);
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

    /// <summary>
    /// Groups issues for the Needs Review "Duplicate Files" section (docs/superpowers/specs/
    /// 2026-09-05-duplicate-files-review-design.md) - a second entry point over the same two keys
    /// <see cref="DuplicateIssueIds"/> uses, needed because that method only returns a flat set of
    /// flagged ids, not which issues actually belong together. Union-find over both keys: two issues
    /// sharing *either* the metadata tuple or an identical file path land in one cluster, so an issue
    /// linked to different partners via different keys still gets one group card, not two overlapping
    /// ones. Only clusters of 2+ are returned.
    /// </summary>
    public static List<List<Issue>> BuildDuplicateGroups(IReadOnlyCollection<Issue> issues)
    {
        var list = issues as IReadOnlyList<Issue> ?? issues.ToList();
        int n = list.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            parent[i] = i;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
            {
                parent[a] = b;
            }
        }

        var byMetadata = new Dictionary<(int, string?, int?, string?, string?, string?, int?, int?, int?), List<int>>();
        var byPath = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < n; i++)
        {
            var issue = list[i];
            var key = (issue.SeriesId, issue.Format, issue.Count, issue.EffectiveNumber(), issue.EffectiveVolume(), issue.LanguageISO, issue.EffectiveYear(), issue.Month, issue.Day);
            if (!byMetadata.TryGetValue(key, out var metaIndexes))
            {
                byMetadata[key] = metaIndexes = new List<int>();
            }

            metaIndexes.Add(i);

            if (!string.IsNullOrEmpty(issue.FilePath))
            {
                if (!byPath.TryGetValue(issue.FilePath, out var pathIndexes))
                {
                    byPath[issue.FilePath] = pathIndexes = new List<int>();
                }

                pathIndexes.Add(i);
            }
        }

        foreach (var indexes in byMetadata.Values.Where(g => g.Count > 1))
        {
            for (int k = 1; k < indexes.Count; k++)
            {
                Union(indexes[0], indexes[k]);
            }
        }

        foreach (var indexes in byPath.Values.Where(g => g.Count > 1))
        {
            for (int k = 1; k < indexes.Count; k++)
            {
                Union(indexes[0], indexes[k]);
            }
        }

        var clusters = new Dictionary<int, List<Issue>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!clusters.TryGetValue(root, out var members))
            {
                clusters[root] = members = new List<Issue>();
            }

            members.Add(list[i]);
        }

        // FileIsMissing ascending first (a missing copy is never the default keep when a present one
        // exists in the same cluster), then FileSize descending (nulls last via the -1 sentinel - a
        // null size can't be "largest"), then AddedTime ascending, then Id as a final deterministic
        // tie-break.
        return clusters.Values
            .Where(members => members.Count > 1)
            .Select(members => members
                .OrderBy(i => i.FileIsMissing)
                .ThenByDescending(i => i.FileSize ?? -1)
                .ThenBy(i => i.AddedTime ?? DateTime.MaxValue)
                .ThenBy(i => i.Id)
                .ToList())
            .ToList();
    }
}

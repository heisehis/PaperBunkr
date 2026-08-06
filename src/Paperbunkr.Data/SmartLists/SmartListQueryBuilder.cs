using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Evaluates a <see cref="SmartList"/>'s AND-ed conditions against the library. In-memory
/// predicate evaluation over the fully materialized issue set — see
/// <see cref="SmartListCatalog"/>'s remarks for why that's the CE-faithful choice here, not just a
/// convenience.
/// </summary>
public static class SmartListQueryBuilder
{
    public static List<Issue> Build(PaperbunkrDbContext ctx, SmartList list)
    {
        var issues = ctx.Issues
            .Include(i => i.Series)
            .Include(i => i.CustomValues)
            .ToList();

        HashSet<int>? duplicateIds = list.Conditions.Any(c => c.Field == SmartListField.Duplicate)
            ? DuplicateIssueIds(issues)
            : null;

        IEnumerable<Issue> result = issues;
        foreach (var condition in list.Conditions.OrderBy(c => c.SortOrder))
        {
            result = result.Where(i => Evaluate(i, condition, duplicateIds));
        }

        return result.ToList();
    }

    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => Build(ctx, list).Count;

    private static bool Evaluate(Issue issue, SmartListCondition condition, HashSet<int>? duplicateIds)
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
    /// CE's exact dual-key duplicate detection (<c>ComicBookDuplicateMatcher</c>, confirmed via
    /// direct source check): union of (a) a metadata-key group — Series + Format + Count + Number +
    /// Volume + LanguageISO + Year + Month + Day — and (b) a FilePath group, each with
    /// <c>Count() &gt; 1</c>, concatenated and de-duplicated so an issue matching both keys isn't
    /// double-counted.
    /// </summary>
    public static HashSet<int> DuplicateIssueIds(IReadOnlyCollection<Issue> issues)
    {
        var byMetadata = issues
            .GroupBy(i => (i.SeriesId, i.Format, i.Count, i.Number, i.Volume, i.LanguageISO, i.Year, i.Month, i.Day))
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

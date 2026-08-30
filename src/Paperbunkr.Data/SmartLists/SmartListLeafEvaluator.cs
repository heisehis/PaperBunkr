using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Operator evaluation for one already-extracted field value against a
/// <see cref="SmartListCondition"/> (docs/superpowers/specs/2026-08-30-smart-collections-design.md).
/// Extracted out of <see cref="SmartListQueryBuilder"/> — these methods take primitive values, not
/// <see cref="Issue"/>, so they were already kind-agnostic in everything but visibility. Shared by
/// every target-kind query builder (<see cref="SmartListQueryBuilder"/>, <c>SeriesSmartListQueryBuilder</c>,
/// <c>NovelSmartListQueryBuilder</c>) rather than copy-pasted, since sharing pure functions over
/// primitives costs nothing and avoids three copies of the same operator logic drifting apart.
///
/// Selector extraction (getting a field's value off an <see cref="Issue"/>/<c>Series</c>/<c>Book</c>
/// row) stays per-kind and is <em>not</em> here — only the operator semantics that apply once a
/// value is already in hand.
/// </summary>
internal static class SmartListLeafEvaluator
{
    public static bool EvaluateText(string value, SmartListCondition c)
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

    public static IEnumerable<string> SplitValues(string raw) =>
        raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool EvaluateNumber(float value, SmartListCondition c)
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

    public static bool EvaluateToggle(bool value, SmartListCondition c)
    {
        bool target = bool.TryParse(c.Value, out var b) && b;
        return c.Operator == SmartListOperator.IsNot ? value != target : value == target;
    }

    public static bool EvaluateDate(DateTime? value, SmartListCondition c)
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
}

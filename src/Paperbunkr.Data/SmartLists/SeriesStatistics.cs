using cYo.Common.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// The four library-wide series figures CE's <c>SmartListSeries*Matcher</c>s read
/// (<c>_reference/ComicRackCE/ComicRack.Engine/ComicBookSeriesStatistics.cs</c>). CE groups every comic by
/// <c>(ShadowSeries, ShadowVolume)</c> over the <em>whole library</em> (not the books an earlier matcher kept) and
/// computes each figure once per group; this mirrors that, keyed by series name and effective volume text.
/// </summary>
internal static class SeriesStatistics
{
    /// <summary>CE's <c>ComicBookSeriesStatistics.Key</c>: the series name and volume the comic belongs to.</summary>
    public readonly record struct Key(string Series, string Volume);

    /// <summary>One group's figures. <see cref="LastNumber"/>/<see cref="MaxYear"/>/<see cref="MaxCount"/> are -1 when no comic of the group has a value (CE's invalid value).</summary>
    public readonly record struct Stats(float LastNumber, int MaxYear, int MaxCount, int MaxGapSize,
        DateTime? LastAdded, DateTime? LastOpened, DateTime? LastPublished, DateTime? LastReleased);

    public static Key KeyOf(Issue issue) => new(issue.Series?.Name ?? string.Empty, issue.EffectiveVolume() ?? string.Empty);

    public static Dictionary<Key, Stats> Compute(IEnumerable<Issue> issues) =>
        issues.GroupBy(KeyOf).ToDictionary(g => g.Key, g => Of(g.ToList()));

    public static Stats Of(IReadOnlyCollection<Issue> group)
    {
        float lastNumber = group.Max(SafeNumber);
        int maxYear = group.Max(i => i.EffectiveYear() ?? -1);
        int maxCount = group.Max(i => i.Count ?? -1);
        return new Stats(lastNumber, maxYear, maxCount, MaxGapSize(group),
            group.MaxDate(i => i.AddedTime), group.MaxDate(i => i.OpenedTime),
            group.MaxDate(i => PublishedDate(i)), group.MaxDate(i => i.ReleasedTime));
    }

    private static DateTime? MaxDate(this IReadOnlyCollection<Issue> group, Func<Issue, DateTime?> selector)
    {
        DateTime[] values = group.Select(selector).Where(d => d.HasValue).Select(d => d!.Value).ToArray();
        return values.Length == 0 ? null : values.Max();
    }

    private static DateTime? PublishedDate(Issue issue)
    {
        int year = issue.EffectiveYear() ?? 0;
        if (year <= 0) return null;
        year = Math.Clamp(year, 1, 9999);
        int month = Math.Clamp(issue.Month ?? 1, 1, 12);
        int day = Math.Clamp(issue.Day ?? 1, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day);
    }

    /// <summary>
    /// CE's <c>ComicBook.CompareNumber</c> as a float, or -1 when the number is not a number
    /// (<c>ComicBookSeriesStatistics.GetSafeNumber</c>). CE reads the first number found in the text, so "1a" is 1;
    /// the one special case it adds is "1/2" = 0.5.
    /// </summary>
    public static float SafeNumber(Issue issue) => ParseNumber(issue.EffectiveNumber(), out float number) ? number : -1f;

    /// <summary>CE's <c>ComicBookNumberMatcher.GetValue</c> / <c>ComicTextNumberFloat</c> parse.</summary>
    public static bool ParseNumber(string? text, out float number)
    {
        text = text?.Trim() ?? string.Empty;
        if (text == "1/2")
        {
            number = 0.5f;
            return true;
        }

        return text.TryParse(out number, invariant: true);
    }

    /// <summary>
    /// CE's <c>GetGaps</c> + <c>MaxGapSize</c>: every positive number of the group (a range such as "3-6" adds the numbers after its
    /// first), sorted; each step above 1 is a gap whose size is the <em>step</em> (not the count of missing issues); the figure is the
    /// largest step, 0 when there is none.
    /// </summary>
    private static int MaxGapSize(IReadOnlyCollection<Issue> group)
    {
        var numbers = group
            .SelectMany(i => new[] { SafeNumber(i) }.Concat(RangeNumbers(i)))
            .Where(n => n > 0f)
            .OrderBy(n => n)
            .ToList();
        if (numbers.Count == 0)
        {
            return 0;
        }

        float largest = 0f;
        float previous = numbers[0];
        foreach (float n in numbers.Skip(1))
        {
            float step = n - previous;
            if (step > 1f)
            {
                largest = Math.Max(largest, step);
            }

            previous = n;
        }

        return (int)largest;
    }

    // CE's GetRangeNumber: GetRange().Skip(1), because the first number of a range is "added by GetSafeNumber". For a range text such as
    // "3-6" that number is not parseable on its own, so CE loses it too; kept as it is.
    private static IEnumerable<float> RangeNumbers(Issue issue) =>
        new TextNumberFloat(issue.EffectiveNumber() ?? string.Empty).GetRange().Skip(1);
}

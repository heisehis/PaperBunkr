using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Writes one ComicVine issue's per-issue fields onto a tracked <see cref="Issue"/>, honouring a <see cref="ScrapeFieldPolicy"/>. Ported from the
/// Cluster Library Manager's <c>ApplyIssueDetails</c> (whose field mapping was verified against CE's ComicVineScraper). Volume-level fields (series name,
/// publisher, imprint) are not touched here: a scrape by id already knows its series.
/// </summary>
public static class IssueDetailsApplier
{
    /// <summary>Applies the details; returns the fields that actually changed (empty when nothing did). The caller saves.</summary>
    public static IReadOnlyList<ScrapeField> Apply(Issue issue, ComicVineIssueDetails details, ScrapeFieldPolicy policy)
    {
        var changed = new List<ScrapeField>();

        void Text(ScrapeField field, string? current, string? value, Action<string?> set)
        {
            if (policy.ShouldWrite(field, !string.IsNullOrEmpty(current), !string.IsNullOrEmpty(value)) && !string.Equals(current, value, StringComparison.Ordinal))
            {
                set(value);
                changed.Add(field);
            }
        }

        Text(ScrapeField.Number, issue.Number, details.IssueNumber, v => issue.Number = v);
        Text(ScrapeField.Title, issue.Title, details.Title, v => issue.Title = v);
        Text(ScrapeField.Summary, issue.Summary, details.Summary, v => issue.Summary = v);
        Text(ScrapeField.Webpage, issue.Web, details.SiteDetailUrl, v => issue.Web = v);

        // ComicVine's story_arc_credits is what CE's "crossovers" toggle writes; there is no separate story-arc toggle in the matrix.
        Text(ScrapeField.Crossovers, issue.StoryArc, JoinOrNull(details.StoryArcs), v => issue.StoryArc = v);
        Text(ScrapeField.Characters, issue.Characters, JoinOrNull(details.Characters), v => issue.Characters = v);
        Text(ScrapeField.Teams, issue.Teams, JoinOrNull(details.Teams), v => issue.Teams = v);
        Text(ScrapeField.Locations, issue.Locations, JoinOrNull(details.Locations), v => issue.Locations = v);

        // One toggle governs the whole published date (year, month, day together), as in CE.
        if (policy.ShouldWrite(ScrapeField.Published, issue.Year.HasValue, details.PublishedDate.Year.HasValue)
            && (issue.Year != details.PublishedDate.Year || issue.Month != details.PublishedDate.Month || issue.Day != details.PublishedDate.Day))
        {
            issue.Year = details.PublishedDate.Year;
            issue.Month = details.PublishedDate.Month;
            issue.Day = details.PublishedDate.Day;
            changed.Add(ScrapeField.Published);
        }

        var released = BuildDate(details.ReleasedDate);
        if (policy.ShouldWrite(ScrapeField.Released, issue.ReleasedTime.HasValue, released.HasValue) && issue.ReleasedTime != released)
        {
            issue.ReleasedTime = released;
            changed.Add(ScrapeField.Released);
        }

        Credit(ScrapeField.Writer, "Writer", issue.Writer, v => issue.Writer = v);
        Credit(ScrapeField.Penciller, "Penciller", issue.Penciller, v => issue.Penciller = v);
        Credit(ScrapeField.Inker, "Inker", issue.Inker, v => issue.Inker = v);
        Credit(ScrapeField.Colorist, "Colorist", issue.Colorist, v => issue.Colorist = v);
        Credit(ScrapeField.Letterer, "Letterer", issue.Letterer, v => issue.Letterer = v);
        Credit(ScrapeField.CoverArtist, "CoverArtist", issue.CoverArtist, v => issue.CoverArtist = v);
        Credit(ScrapeField.Editor, "Editor", issue.Editor, v => issue.Editor = v);

        return changed;

        void Credit(ScrapeField field, string role, string? current, Action<string?> set) =>
            Text(field, current, JoinOrNull(details.Credits.Where(c => c.Field == role).Select(c => c.Name).Distinct()), set);
    }

    private static string? JoinOrNull(IEnumerable<string> values)
    {
        var joined = string.Join(", ", values);
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static DateTime? BuildDate(ComicVineDatePart part)
    {
        if (part.Year is not int year)
        {
            return null;
        }

        int month = part.Month is > 0 and <= 12 ? part.Month.Value : 1;
        int day = part.Day is > 0 ? part.Day.Value : 1;
        // A day that doesn't exist in that month (a partial or odd ComicVine date) falls back to the 1st instead of losing the whole date.
        return day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : new DateTime(year, month, 1);
    }
}

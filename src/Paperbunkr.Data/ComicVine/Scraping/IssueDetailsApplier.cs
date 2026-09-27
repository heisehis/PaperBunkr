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
    /// <param name="arcPositions">This issue's reading-order positions, when the caller computed them (the scraper does, from ComicVine's per-arc issue lists); null leaves StoryArcOrder untouched.</param>
    public static IReadOnlyList<ScrapeField> Apply(Issue issue, ComicVineIssueDetails details, ScrapeFieldPolicy policy, StoryArcPositions? arcPositions = null)
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
        Text(ScrapeField.Crossovers, issue.StoryArc, JoinOrNull(details.StoryArcs.Select(a => a.Name)), v => issue.StoryArc = v);
        Text(ScrapeField.Characters, issue.Characters, JoinOrNull(details.Characters.Select(c => c.Name)), v => issue.Characters = v);
        Text(ScrapeField.Teams, issue.Teams, JoinOrNull(details.Teams.Select(t => t.Name)), v => issue.Teams = v);
        Text(ScrapeField.Locations, issue.Locations, JoinOrNull(details.Locations.Select(l => l.Name)), v => issue.Locations = v);
        Text(ScrapeField.AgeRating, issue.AgeRating, details.AgeRating, v => issue.AgeRating = v);
        Text(ScrapeField.Isbn, issue.ISBN, details.Isbn, v => issue.ISBN = v);
        Text(ScrapeField.Upc, issue.Upc, details.Upc, v => issue.Upc = v);
        Text(ScrapeField.Imprint, issue.Imprint, details.Imprint, v => issue.Imprint = v);

        // Fork fields (2026-09-25). Main character/team is the fork's own heuristic: the first
        // character, else the first team. Series Group mirrors the arc names, as in the fork -
        // ComicVine has no separate "event" resource to source it from.
        string? mainCharacterOrTeam = details.Characters.Count > 0 ? details.Characters[0].Name : details.Teams.Count > 0 ? details.Teams[0].Name : null;
        Text(ScrapeField.MainCharacterOrTeam, issue.MainCharacterOrTeam, mainCharacterOrTeam, v => issue.MainCharacterOrTeam = v);
        Text(ScrapeField.SeriesGroup, issue.SeriesGroup, JoinOrNull(details.StoryArcs.Select(a => a.Name)), v => issue.SeriesGroup = v);

        if (arcPositions is not null && !arcPositions.IsEmpty)
        {
            // One toggle for all three, like the fork's single "Story Arc Order" checkbox. Empty
            // entries (an arc whose position couldn't be found) are dropped from the list.
            string? positions = JoinOrNull(arcPositions.Numbers.Where(n => n.Length > 0));
            bool wroteAny = false;
            if (policy.ShouldWrite(ScrapeField.StoryArcOrder, !string.IsNullOrEmpty(issue.StoryArcNumber), positions is not null) && issue.StoryArcNumber != positions)
            {
                issue.StoryArcNumber = positions;
                wroteAny = true;
            }

            if (policy.ShouldWrite(ScrapeField.StoryArcOrder, !string.IsNullOrEmpty(issue.AlternateNumber), positions is not null) && issue.AlternateNumber != positions)
            {
                issue.AlternateNumber = positions;
                wroteAny = true;
            }

            if (policy.ShouldWrite(ScrapeField.StoryArcOrder, issue.AlternateCount.HasValue, arcPositions.AlternateCount.HasValue) && issue.AlternateCount != arcPositions.AlternateCount)
            {
                issue.AlternateCount = arcPositions.AlternateCount;
                wroteAny = true;
            }

            if (wroteAny)
            {
                changed.Add(ScrapeField.StoryArcOrder);
            }
        }

        // Concepts become additive Tags, like Genre below - never removes a tag already there.
        if (policy.Enabled.Contains(ScrapeField.Concepts) && details.Concepts.Count > 0)
        {
            var existingTags = issue.Tags
                .Where(t => t.Field == IssueTagField.Tags)
                .Select(t => t.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var concept in details.Concepts)
            {
                if (!existingTags.Add(concept))
                {
                    continue;
                }

                issue.Tags.Add(new IssueTag { IssueId = issue.Id, Field = IssueTagField.Tags, Value = concept, Category = "Uncategorized" });
                changed.Add(ScrapeField.Concepts);
            }
        }

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

        // Metron's community rating (average_rating/rating_count) - a secondary "community says" data
        // point alongside the user's own Rating, distinct from it (docs/superpowers/specs/2026-09-23-
        // metron-api-utilization-design.md). ComicVine never populates AverageRating, so this is a no-op
        // for that provider (IgnoreBlankValues already covers it).
        float? newRating = details.AverageRating is double avg ? (float)avg : null;
        if (policy.ShouldWrite(ScrapeField.CommunityRating, issue.CommunityRating.HasValue, newRating.HasValue) && issue.CommunityRating != newRating)
        {
            issue.CommunityRating = newRating;
            issue.CommunityRatingCount = details.RatingCount;
            changed.Add(ScrapeField.CommunityRating);
        }

        Credit(ScrapeField.Writer, "Writer", issue.Writer, v => issue.Writer = v);
        Credit(ScrapeField.Penciller, "Penciller", issue.Penciller, v => issue.Penciller = v);
        Credit(ScrapeField.Inker, "Inker", issue.Inker, v => issue.Inker = v);
        Credit(ScrapeField.Colorist, "Colorist", issue.Colorist, v => issue.Colorist = v);
        Credit(ScrapeField.Letterer, "Letterer", issue.Letterer, v => issue.Letterer = v);
        Credit(ScrapeField.CoverArtist, "CoverArtist", issue.CoverArtist, v => issue.CoverArtist = v);
        Credit(ScrapeField.Editor, "Editor", issue.Editor, v => issue.Editor = v);
        Credit(ScrapeField.Translator, "Translator", issue.Translator, v => issue.Translator = v);

        // Genre is additive, not a Text() overwrite - a provider's genre list only ever adds
        // IssueTag rows, never removes ones the user (or another provider) already added
        // (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md - Series.Genre's own
        // doc comment pointed at a since-removed Issue.Genre field; IssueTag is the real, current
        // home for genre data).
        if (policy.Enabled.Contains(ScrapeField.Genre) && details.Genres.Count > 0)
        {
            var existingGenres = issue.Tags
                .Where(t => t.Field == IssueTagField.Genre)
                .Select(t => t.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // HashSet.Add both checks and records in one step, so a provider response that itself
            // repeats a genre (confirmed live: ComicVine can return the same genre name twice for one
            // issue) only ever adds it once - the old .Where(!existingGenres.Contains) filtered every
            // duplicate against the same pre-loop snapshot, so a repeated genre passed the filter twice
            // and produced two IssueTag rows with the same (IssueId, Field, Value), which then crashed
            // IssuePropertiesScreenViewModel.ApplyTagRows's ToDictionary the next time that issue was
            // opened for editing ("An item with the same key has already been added").
            foreach (var genre in details.Genres)
            {
                if (!existingGenres.Add(genre))
                {
                    continue;
                }

                issue.Tags.Add(new IssueTag { IssueId = issue.Id, Field = IssueTagField.Genre, Value = genre, Category = "Genre" });
                changed.Add(ScrapeField.Genre);
            }
        }

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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>One field a match would change: <see cref="Before"/> to <see cref="After"/>, both as shown to the user.</summary>
public sealed record ScrapeFieldChange(string Field, string Before, string After, bool IsSeriesField);

/// <summary>
/// What applying one comic's match would do, worked out by a dry run without saving (docs/superpowers/specs/2026-10-21-scraper-organizer-c-
/// automation-ux-design.md section 3). Carries the decided volume and issue details, so applying it later asks nothing again.
/// </summary>
public sealed record ScrapeProposal(
    int IssueId,
    string BookLabel,
    ComicProvider Provider,
    ComicVineVolumeSearchResult Volume,
    ComicVineIssueDetails? Details,
    IReadOnlyList<ScrapeFieldChange> Changes)
{
    /// <summary>The changes a dry run left pending in <paramref name="context"/> (issue and series columns), in a stable order.</summary>
    public static IReadOnlyList<ScrapeFieldChange> Collect(PaperbunkrDbContext context)
    {
        var changes = new List<ScrapeFieldChange>();
        context.ChangeTracker.DetectChanges();
        foreach (EntityEntry entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Modified && e.Entity is Issue or Series))
        {
            bool series = entry.Entity is Series;
            // A move to another series is reported by the orchestrator in words; the raw key would read as a number.
            foreach (var property in entry.Properties.Where(p => p.IsModified && p.Metadata.Name != nameof(Issue.SeriesId)))
            {
                string before = Show(property.OriginalValue);
                string after = Show(property.CurrentValue);
                if (before != after)
                {
                    changes.Add(new ScrapeFieldChange(Label(property.Metadata.Name, series), before, after, series));
                }
            }
        }

        return changes.OrderBy(c => c.IsSeriesField).ThenBy(c => c.Field, StringComparer.Ordinal).ToList();
    }

    private static string Label(string property, bool series) => (series ? "Series " : string.Empty) + property switch
    {
        nameof(Issue.MetadataSource) => "Source",
        nameof(Issue.CoverArtist) => "Cover artist",
        nameof(Issue.StoryArc) => "Story arc",
        nameof(Issue.StoryArcNumber) => "Story arc number",
        nameof(Issue.SeriesGroup) => "Series group",
        nameof(Issue.AlternateNumber) => "Alternate number",
        nameof(Issue.AlternateCount) => "Alternate count",
        nameof(Issue.MainCharacterOrTeam) => "Main character or team",
        nameof(Issue.CommunityRating) => "Community rating",
        nameof(Issue.CommunityRatingCount) => "Rating count",
        nameof(Issue.ReleasedTime) => "Released",
        nameof(Issue.AgeRating) => "Age rating",
        nameof(Issue.GcdIssueId) => "GCD id",
        _ => property,
    };

    private static string Show(object? value) => value switch
    {
        null => "—",
        string s when string.IsNullOrWhiteSpace(s) => "—",
        string s => s.Length > 160 ? s[..157] + "…" : s,
        DateTime d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "—",
    };
}

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// The scraper's field-toggle matrix, ported from CE's ComicVineScraper "Details" tab (the Cluster Library Manager plugin, spec section 4). All fields default enabled, as in CE.
/// </summary>
public enum ScrapeField
{
    Series,
    Number,
    Published,
    Released,
    Title,
    Crossovers,
    Writer,
    Penciller,
    Inker,
    CoverArtist,
    Colorist,
    Letterer,
    Editor,
    Summary,
    Imprint,
    Publisher,
    Volume,
    Characters,
    Teams,
    Locations,
    Webpage,

    // Added docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md - fields both
    // provider clients already receive but neither ever wrote anywhere.
    Translator,
    AgeRating,
    Isbn,
    Upc,
    Genre,
    CommunityRating,

    // Added 2026-09-25, ported from the user's own fork of the ComicVine Scraper plugin (which CE's
    // original never wrote): Count comes from the volume's issue count; the rest from the issue's own
    // ComicVine data. StoryArcOrder governs StoryArcNumber, AlternateNumber and AlternateCount together,
    // as the fork's single "Story Arc Order" checkbox does.
    Count,
    MainCharacterOrTeam,
    Concepts,
    SeriesGroup,
    StoryArcOrder,
}

/// <summary>Which fields a scrape may write, and whether it may overwrite or blank existing values.</summary>
public sealed class ScrapeFieldPolicy
{
    /// <summary>Every field on, overwriting existing values but never blanking one with an empty ComicVine value.</summary>
    public static ScrapeFieldPolicy Default => new();

    public HashSet<ScrapeField> Enabled { get; init; } = new(Enum.GetValues<ScrapeField>());

    /// <summary>When false, a field that already has a value is left alone.</summary>
    public bool OverwriteExisting { get; init; } = true;

    /// <summary>When true (the default), an empty ComicVine value never replaces an existing one.</summary>
    public bool IgnoreBlankValues { get; init; } = true;

    /// <summary>Number (and Series, applied elsewhere via <c>ScrapeOrchestrator.ShouldWrite</c>) is
    /// hardcoded to always ignore blanks regardless of <see cref="IgnoreBlankValues"/> - CE exempts
    /// these two identity fields from that setting unconditionally (comicbook.py:259-268, verified
    /// directly against source: "we ALWAYS ignore blanks for 'series'!"/"issue number"), so an empty
    /// ComicVine response can never blank either one out even with the global setting off (docs/
    /// superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §1.2).</summary>
    public bool ShouldWrite(ScrapeField field, bool hasCurrentValue, bool hasNewValue)
    {
        if (!Enabled.Contains(field))
        {
            return false;
        }

        bool ignoreBlankValues = field == ScrapeField.Number || IgnoreBlankValues;
        if (ignoreBlankValues && !hasNewValue)
        {
            return false;
        }

        return OverwriteExisting || !hasCurrentValue;
    }
}

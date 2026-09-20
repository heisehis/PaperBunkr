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

    public bool ShouldWrite(ScrapeField field, bool hasCurrentValue, bool hasNewValue)
    {
        if (!Enabled.Contains(field))
        {
            return false;
        }

        if (IgnoreBlankValues && !hasNewValue)
        {
            return false;
        }

        return OverwriteExisting || !hasCurrentValue;
    }
}

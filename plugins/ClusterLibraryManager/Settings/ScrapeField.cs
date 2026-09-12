namespace ClusterLibraryManager.Settings;

/// <summary>CE's own 20-entry field-scrape toggle matrix (design doc §4, `configuration.py`'s
/// "Details" tab `CheckedListBox`) - all default enabled, per CE.</summary>
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

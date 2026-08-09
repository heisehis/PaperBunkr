namespace Paperbunkr.Data.Entities;

/// <summary>
/// App-wide settings, a single singleton row (<see cref="Id"/> always 1) rather than a generic
/// key-value store - matches every other entity in this codebase's typed-columns convention.
/// New settings (Reader/Behavior/Libraries/Scripts/Advanced tabs, per docs/ce-feature-inventory.md
/// §E) get their own migration when their own spec lands, same as any other schema change here.
/// </summary>
public class AppSettings
{
    public int Id { get; set; } = 1;

    /// <summary>Key of the currently active skin - "default" (the built-in theme) or an installed .crpck's key.</summary>
    public string ActiveSkinKey { get; set; } = "default";

    /// <summary>Selected font family name, or null for the app default (no override).</summary>
    public string? SelectedFontFamily { get; set; }

    /// <summary>Whether opening an issue resumes at <see cref="Issue.LastPageRead"/>, or always starts at page 1. CE default: true.</summary>
    public bool OpenLastPage { get; set; } = true;

    /// <summary>Whether reading past an issue's last/first page loads the next/previous issue in the series. CE default: true.</summary>
    public bool AutoNavigateComics { get; set; } = true;

    /// <summary>Folder backups are written to, or null for the default (%AppData%\Paperbunkr\backups).</summary>
    public string? BackupLocation { get; set; }

    /// <summary>How many database backups to retain before pruning the oldest. CE default: 5.</summary>
    public int BackupsToKeep { get; set; } = 5;

    /// <summary>
    /// Whether left/right page-turn navigation (click zones, arrow keys, scrubber buttons) is
    /// reversed for issues whose effective <see cref="ReadingMode"/> is <see cref="Entities.ReadingMode.RightToLeft"/>.
    /// Default true - deliberately diverging from CE's equivalent (<c>LeftRightMovementReversed</c>,
    /// default false), since CE's default only reads correctly because its default RTL mode does
    /// pixel-level page mirroring Paperbunkr doesn't implement; without this on, RTL would do
    /// nothing observable at all.
    /// </summary>
    public bool ReverseRtlNavigation { get; set; } = true;

    /// <summary>
    /// Whether pages are scaled to fit the canvas using high-quality (bicubic) interpolation, or
    /// faster/lower-quality scaling. CE default: true (<c>ImageDisplayOptions.HighQuality</c>,
    /// on by default).
    /// </summary>
    public bool HighQualityPageDisplay { get; set; } = true;
}

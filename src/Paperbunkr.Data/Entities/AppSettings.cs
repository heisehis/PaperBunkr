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

    /// <summary>
    /// Whether zoom resets to 1.0 on every page turn within a session, or persists across pages
    /// until the issue is closed/reopened (Paperbunkr's existing behavior). CE:
    /// <c>Settings.ResetZoomOnPageChange</c>, default false - both this and Paperbunkr's own
    /// pre-existing default agree, so this setting only changes anything for someone who
    /// deliberately turns it on.
    /// </summary>
    public bool ResetZoomOnPageChange { get; set; }

    /// <summary>
    /// Mouse-wheel scroll/pan speed multiplier, replacing <c>PageCanvas</c>'s previously-fixed
    /// <c>WheelPanStep</c> constant. CE: <c>Settings.MouseWheelSpeed</c> ("lines per mouse
    /// scrolling"), default 2.0, UI range 0.5-5.0 (CE's own trackbar min/max) - governs plain-wheel
    /// pan speed, not Ctrl+wheel zoom, confirmed from CE source
    /// (<c>ComicDisplay.OnMouseWheel</c>'s <c>scrollLines = ... * MouseWheelSpeed</c>).
    /// </summary>
    public double MouseWheelSpeed { get; set; } = 2.0;

    /// <summary>
    /// Global default fit mode for a book with no <see cref="Issue.PageFitModeOverride"/>
    /// (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md §3 left
    /// this as a fixed code constant pending a Reader Preferences surface to edit it - this is
    /// that surface). Default matches the constant it replaces.
    /// </summary>
    public ImageFitMode DefaultPageFitMode { get; set; } = ImageFitMode.FitWidth;

    /// <summary>Same rationale as <see cref="DefaultPageFitMode"/>, for the auto-rotate-landscape-pages default.</summary>
    public bool DefaultAutoRotate { get; set; }
}

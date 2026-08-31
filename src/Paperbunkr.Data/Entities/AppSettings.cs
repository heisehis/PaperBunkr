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

    /// <summary>
    /// Whether UI transitions (docs/superpowers/specs/2026-08-24-design-language-foundation-design.md
    /// motion tokens) are shortened to effectively instant. Default false - the app ships with
    /// "snappy & responsive" motion on by default, this is an opt-in accessibility/preference toggle.
    /// </summary>
    public bool ReducedMotion { get; set; }

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

    /// <summary>
    /// Paged-mode page-turn transition style (docs/superpowers/specs/2026-08-13-reader-page-
    /// transition-animations-design.md §2). Default <see cref="PageTransitionStyle.None"/>, matching
    /// CE's own <c>BlendWhilePaging</c> default of <c>false</c>.
    /// </summary>
    public PageTransitionStyle PageTransitionStyle { get; set; } = PageTransitionStyle.None;

    /// <summary>
    /// Page-turn transition duration in milliseconds, UI range 100-600 (spec §2) - CE parity in
    /// spirit (<c>AnimationDuration</c> 250-300, <c>BlendDuration</c> 400 depending on version)
    /// collapsed into one duration here rather than a literal port of either.
    /// </summary>
    public int PageTransitionDurationMs { get; set; } = 250;

    /// <summary>
    /// Global default for paged-mode double-page spread (docs/superpowers/specs/2026-08-15-reader-
    /// double-page-spread-design.md §2), the bottom of the <c>Issue.PageLayoutModeOverride ??
    /// Series.PageLayoutMode ?? AppSettings.DefaultPageLayoutMode</c> resolution chain - unlike
    /// <see cref="Series.PageLayoutMode"/> (nullable, so this layer can act as its live fallback),
    /// this always has a concrete value.
    /// </summary>
    public PageLayoutMode DefaultPageLayoutMode { get; set; } = PageLayoutMode.Single;

    /// <summary>
    /// Magnifier zoom multiplier (docs/superpowers/specs/2026-08-10-reader-polish-continuous-
    /// scroll-chrome-overlays-design.md §8). CE default 2.0 (<c>ComicDisplayControl.magnifierZoom</c>
    /// field initializer, confirmed from source).
    /// </summary>
    public double MagnifierZoom { get; set; } = 2.0;

    /// <summary>CE default 1.0 (<c>ComicDisplayControl.MagnifierOpacity</c>'s <c>[DefaultValue(1f)]</c>, fully opaque).</summary>
    public double MagnifierOpacity { get; set; } = 1.0;

    /// <summary>CE default 200 (<c>ComicDisplayControl.MagnifierSize</c>'s <c>[DefaultValue(typeof(Size), "200, 200")]</c> - square, one dimension stored).</summary>
    public int MagnifierSizePixels { get; set; } = 200;

    /// <summary>
    /// Global default live image-adjustment values (docs/superpowers/specs/2026-08-10-reader-
    /// polish-continuous-scroll-chrome-overlays-design.md §9), additive with
    /// <see cref="Issue.BrightnessOverride"/>. Default 0 matches CE's <c>BitmapAdjustment.Empty</c>.
    /// </summary>
    public double DefaultBrightness { get; set; }

    /// <summary>See <see cref="DefaultBrightness"/>.</summary>
    public double DefaultContrast { get; set; }

    /// <summary>See <see cref="DefaultBrightness"/>.</summary>
    public double DefaultSaturation { get; set; }

    /// <summary>See <see cref="DefaultBrightness"/>.</summary>
    public double DefaultGamma { get; set; }

    /// <summary>
    /// Reader canvas background mode (docs/superpowers/specs/2026-08-10-reader-polish-continuous-
    /// scroll-chrome-overlays-design.md §10). CE default <c>Color</c> (confirmed from
    /// <c>DisplayWorkspace.cs</c>'s <c>[DefaultValue(ImageBackgroundMode.Color)]</c>).
    /// </summary>
    public ImageBackgroundMode ImageBackgroundMode { get; set; } = ImageBackgroundMode.Color;

    /// <summary>CE default "WhiteSmoke" (<c>DisplayWorkspace.BackgroundColor</c>'s <c>[DefaultValue("WhiteSmoke")]</c>), a named color, hex or named string.</summary>
    public string BackgroundColor { get; set; } = "WhiteSmoke";

    /// <summary>CE default false (<c>DisplayWorkspace.PageMargin</c>'s <c>[DefaultValue(false)]</c>).</summary>
    public bool PageMarginEnabled { get; set; }

    /// <summary>CE default 0.05 (<c>DisplayWorkspace.PageMarginPercentWidth</c>'s <c>[DefaultValue(0.05f)]</c>).</summary>
    public double PageMarginPercentWidth { get; set; } = 0.05;

    /// <summary>
    /// Whether the fullscreen scrubber/page-browser overlay (docs/superpowers/specs/2026-08-10-
    /// reader-polish-continuous-scroll-chrome-overlays-design.md §7) is shown by default. Default
    /// true - deliberately diverging from CE's own opt-in <c>InfoOverlays.None</c> default, since CE
    /// has toolbar-based page nav as a fallback that Paperbunkr's fullscreen mode deliberately hides.
    /// </summary>
    public bool ShowScrubberOverlay { get; set; } = true;

    /// <summary>
    /// Library screen's sort/group/display/filter state (docs/superpowers/specs/2026-08-17-library-
    /// saved-list-layouts-design.md), the CE <c>DisplayListConfig</c> equivalent - a single,
    /// transparently auto-persisted config, not named/multiple presets (that's the separate,
    /// not-yet-built Saved Workspaces feature, CE's <c>DisplayWorkspace</c>). Every field here
    /// defaults to whatever <c>LibraryScreenViewModel</c>'s own in-code default already was before
    /// persistence existed, so an existing settings row reproduces prior startup behavior exactly.
    /// </summary>
    /// <remarks>
    /// Real again as of the same-session follow-up to Slice 3 above: the user asked for the
    /// series-card view back as a real, switchable option (<see cref="LibraryContentGranularity"/>),
    /// not permanently replaced by per-issue tiles. This is the series-card Sort field, used when
    /// <see cref="LibraryGranularity"/> is <see cref="LibraryContentGranularity.Series"/>; see
    /// <see cref="LibraryIssueListSortField"/> for the per-issue equivalent, used otherwise.
    /// </remarks>
    public LibrarySortField LibrarySortField { get; set; } = LibrarySortField.DateAdded;

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public SortDirection LibrarySortDirection { get; set; } = SortDirection.Descending;

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public LibraryGroupField LibraryGroupField { get; set; } = LibraryGroupField.None;

    /// <summary>
    /// Per-issue Sort field, used when <see cref="LibraryGranularity"/> is
    /// <see cref="LibraryContentGranularity.Issue"/> - see <see cref="LibrarySortField"/> for the
    /// series-card equivalent, used otherwise.
    /// </summary>
    public IssueListSortField LibraryIssueListSortField { get; set; } = IssueListSortField.Added;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public SortDirection LibraryIssueListSortDirection { get; set; } = SortDirection.Descending;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public IssueListGroupField LibraryIssueListGroupField { get; set; } = IssueListGroupField.None;

    /// <summary>
    /// Card granularity - series-aggregate cards vs per-issue tiles, independent of
    /// <see cref="LibraryViewMode"/>'s layout *shape*. See <see cref="LibraryContentGranularity"/>.
    /// </summary>
    public LibraryContentGranularity LibraryGranularity { get; set; } = LibraryContentGranularity.Issue;

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public LibraryViewMode LibraryViewMode { get; set; } = LibraryViewMode.PosterGrid;

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public double LibraryGridDensity { get; set; } = 1.0;

    /// <summary>
    /// Poster-grid tile title row on/off (UI rework Phase 4a - docs/superpowers/specs/
    /// 2026-08-27-library-browsing-4a-poster-grid-design.md). Off reproduces the former
    /// <c>CoverOnlyGrid</c>. Auto-hidden by the ViewModel below a card-width threshold regardless.
    /// </summary>
    public bool LibraryShowTileTitles { get; set; } = true;

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryShowUnreadBadge { get; set; } = true;

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryShowPublisherBadge { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryShowLanguageBadge { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryUseLanguageIcon { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryShowContinueReadingButton { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public string? LibrarySearchQuery { get; set; }

    /// <summary>Field scope for <see cref="LibrarySearchQuery"/> - see <see cref="SearchMode"/>.</summary>
    public SearchMode LibrarySearchMode { get; set; } = SearchMode.All;

    /// <summary>
    /// See <see cref="LibrarySortField"/>. Mutually exclusive with <see cref="LibraryActiveCollectionId"/>
    /// - null means "All Series" (both null) or this content type is the active sidebar filter.
    /// </summary>
    public ContentType? LibraryActiveContentType { get; set; }

    /// <summary>
    /// See <see cref="LibraryActiveContentType"/>. If the referenced <c>Collection</c> no longer
    /// exists at load time (deleted since last session), <c>LibraryScreenViewModel</c> falls back to
    /// "All Series" rather than rendering a silently empty grid.
    /// </summary>
    public int? LibraryActiveCollectionId { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryFilterUnreadOnly { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryFilterMissingIssues { get; set; }

    /// <summary>See <see cref="LibrarySortField"/>.</summary>
    public bool LibraryFilterTrackedOnly { get; set; }

    /// <summary>
    /// Library's configurable Details-table columns (docs/superpowers/specs/2026-08-27-library-
    /// browsing-4b-toolbar-rework-design.md §8) - a comma-joined list of <see cref="IssueListSortField"/>
    /// enum names in display order, e.g. <c>"Title,Series,Number,Volume,Year"</c>. Null means
    /// "never configured" and falls back to <c>IssueListFieldCatalog.DefaultDetailsColumns</c>;
    /// unknown/removed enum names are skipped on load. Nullable string, same rationale as
    /// <see cref="LibrarySearchQuery"/> - no HasDefaultValue/HasSentinel needed.
    /// </summary>
    public string? LibraryDetailsColumns { get; set; }

    /// <summary>
    /// Books screen's persisted sort/group state (docs/superpowers/specs/2026-08-27-books-screen-
    /// chrome-and-home-strip-design.md). Search text is deliberately not persisted. Enum-as-string
    /// with the same HasDefaultValue/HasSentinel treatment as <see cref="LibrarySortField"/>.
    /// </summary>
    public BooksSortField BooksSortField { get; set; } = BooksSortField.Title;

    /// <summary>See <see cref="BooksSortField"/>.</summary>
    public SortDirection BooksSortDirection { get; set; } = SortDirection.Ascending;

    /// <summary>See <see cref="BooksSortField"/>.</summary>
    public BooksGroupField BooksGroupField { get; set; } = BooksGroupField.None;

    /// <summary>
    /// Global policy governing newly-created <see cref="MetadataProposal"/> rows (docs/superpowers/
    /// specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md) - one setting for the
    /// whole library, not per-issue (unlike CE's per-book <c>EnableProposed</c>), since there's no
    /// per-book UI surface or user request for one. Default <see cref="MetadataResolutionPolicy.Automatic"/>
    /// matches <c>LibraryFolderScanner</c>'s pre-existing filename-fallback UX exactly.
    /// </summary>
    public MetadataResolutionPolicy MetadataResolutionPolicy { get; set; } = MetadataResolutionPolicy.Automatic;

    /// <summary>
    /// Whether minimizing (and, deliberately diverging from CE - see docs/superpowers/specs/
    /// 2026-08-23-app-chrome-crash-reporter-and-tray-design.md §4) closing the main window hides it
    /// to a tray icon instead of exiting. CE default false (<c>Settings.MinimizeToTray</c>,
    /// confirmed from <c>MainForm.cs</c>'s own opt-in Preferences toggle).
    /// </summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>
    /// Whether the one-time "Paperbunkr is still running in the tray" explanation has already been
    /// shown - functionally equivalent to CE's <c>HiddenMessageBoxes</c> bit for this message, scoped
    /// to just this one flag since there's no other suppressible message in this app yet.
    /// </summary>
    public bool MinimizeToTrayNoticeShown { get; set; }

    /// <summary>
    /// Whether the nav rail's hover-expand (docs/superpowers/specs/2026-08-24-navigation-shell-
    /// motion-system-design.md) is pinned permanently open (200px, real layout reflow) rather than
    /// only expanding as a temporary hover overlay. Default false - collapsed 64px is the default look.
    /// </summary>
    public bool NavRailPinned { get; set; }

    /// <summary>
    /// Avalonia GPU rendering backend (docs/superpowers/specs/2026-08-27-hardware-accelerated-
    /// rendering-design.md). Restart-only, and the source of truth - mirrored to a
    /// <c>%AppData%\Paperbunkr\graphics.json</c> cache (read by <c>GraphicsBootstrap</c> before the
    /// database is available at startup) and reconciled to it after the DB opens. No CE equivalent.
    /// Default <see cref="RenderBackend.Auto"/> = GPU-first with software fallback.
    /// </summary>
    public RenderBackend RenderingBackend { get; set; } = RenderBackend.Auto;

    /// <summary>
    /// Whether native OpenGL (WGL) is tried before ANGLE/Direct3D in the rendering fallback chain
    /// (spec §4). Default false - ANGLE is the better default on Windows; this is the "ANGLE is the
    /// thing misbehaving on this box" knob. Restart-only, mirrored to <c>graphics.json</c> with
    /// <see cref="RenderingBackend"/>.
    /// </summary>
    public bool PreferNativeOpenGl { get; set; }

    /// <summary>
    /// Whether <c>BackupService.RunAutoBackupIfDue</c> fires automatically on app startup and clean
    /// shutdown (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md §2), on top of
    /// the existing manual "Backup Now". Default true - a user should have a recent backup even if
    /// they've never touched the Advanced tab.
    /// </summary>
    public bool AutoBackupEnabled { get; set; } = true;

    /// <summary>
    /// Minimum age (hours) the newest existing backup must be before an automatic backup trigger
    /// fires another one - see <see cref="AutoBackupEnabled"/>. Default 4, so a user who restarts
    /// the app repeatedly in one session doesn't accumulate a backup per launch.
    /// </summary>
    public int AutoBackupMinIntervalHours { get; set; } = 4;

    /// <summary>
    /// The shell screen active when the app last closed (docs/superpowers/specs/2026-08-30-app-
    /// shell-navigation-history-design.md) - restore-on-launch reopens directly here. Matches
    /// <c>MainViewModel.CurrentScreen</c>'s string values. Null means "never navigated/first launch",
    /// falls back to Home.
    /// </summary>
    public string? LastScreenKey { get; set; }

    /// <summary>
    /// The entity id (series/issue/book id, depending on <see cref="LastScreenKey"/>) that went with
    /// it, or null when <see cref="LastScreenKey"/> is a lateral rail screen with no entity. If the
    /// referenced entity was deleted since last session, restore-on-launch falls back to Home rather
    /// than rendering a broken screen - same posture as <see cref="LibraryActiveCollectionId"/>'s
    /// existing "falls back to All Series if deleted" handling.
    /// </summary>
    public int? LastScreenEntityId { get; set; }

    /// <summary>
    /// Whether the first-run WelcomeOverlay has been shown and closed (docs/superpowers/specs/
    /// 2026-08-31-first-run-onboarding-design.md). Default false. Deliberately independent of
    /// PaperbunkrDb.HasAnySeries() - a user who skips, or adds a folder with zero comics in it, must
    /// never see the welcome screen re-trigger on a later launch just because the library is still
    /// empty. Replaces the old isFreshInstall-based auto-migration gate in App.axaml.cs.
    /// </summary>
    public bool WelcomeScreenShown { get; set; }

    /// <summary>
    /// Whether the one-time post-welcome "want a quick tour?" offer has been shown - see
    /// <see cref="WelcomeScreenShown"/>. Flips true the moment the offer is *shown* (accepted or
    /// declined), not just when it's answered, so an app close mid-prompt can't cause it to reappear
    /// next launch. No replay entry point by design - once resolved, gone for this install.
    /// </summary>
    public bool WelcomeTourOffered { get; set; }

    /// <summary>
    /// Library search box's remembered past queries (docs/superpowers/specs/2026-08-31-library-
    /// search-suggestions-design.md) - JSON-serialized <c>List&lt;string&gt;</c>, most-recent-first,
    /// capped at 8, case-insensitive deduped. Null/empty means no history yet. JSON rather than
    /// <see cref="LibraryDetailsColumns"/>'s comma-join, since a search query can legally contain a
    /// comma (an enum name never can).
    /// </summary>
    public string? LibraryRecentSearches { get; set; }
}

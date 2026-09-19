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

    /// <summary>
    /// Key of the currently active theme - "default" (the built-in theme) or an installed .crpck's
    /// key. Renamed from <c>ActiveSkinKey</c> (EF <c>RenameColumn</c>, data preserved) by
    /// docs/superpowers/specs/2026-09-16-theme-system-design.md.
    /// </summary>
    public string ActiveThemeKey { get; set; } = "default";

    // --- Theme system extensions (docs/superpowers/specs/2026-09-16-theme-system-design.md).

    /// <summary>
    /// OLED "true black" override - when on and the active theme's <c>mode</c> is Dark, live
    /// PbBg/PbChrome/PbSurface0-3 resources are overwritten to #000000. Default false. Auto-
    /// suspended (live resources only, this setting untouched) while a Reader screen is active - see
    /// <see cref="MainViewModel"/>'s <c>OnCurrentScreenChanged</c>.
    /// </summary>
    public bool TrueBlackDark { get; set; }

    /// <summary>
    /// Whether the Matrix theme's animated code-rain background plays. Default true. Only consulted
    /// while the Matrix theme is active (the Appearance toggle row is hidden under every other
    /// theme), but persisted globally so it survives switching away from Matrix and back.
    /// </summary>
    public bool MatrixRainEnabled { get; set; } = true;

    /// <summary>Whether/how the active theme auto-switches - see <see cref="Entities.ThemeAutoMode"/>. Default Off.</summary>
    public ThemeAutoMode ThemeAutoMode { get; set; } = ThemeAutoMode.Off;

    /// <summary>The Light-mode theme Auto mode switches to - updated automatically on every manual apply of a Light theme, not just when Auto is on.</summary>
    public string? LastLightThemeKey { get; set; }

    /// <summary>See <see cref="LastLightThemeKey"/>, Dark-mode counterpart.</summary>
    public string? LastDarkThemeKey { get; set; }

    /// <summary>Local hour (0-23) <see cref="Entities.ThemeAutoMode.Scheduled"/> switches to the dark theme. Default 20 (8pm).</summary>
    public int ThemeScheduledDarkHour { get; set; } = 20;

    /// <summary>Local hour (0-23) <see cref="Entities.ThemeAutoMode.Scheduled"/> switches to the light theme. Default 7 (7am).</summary>
    public int ThemeScheduledLightHour { get; set; } = 7;

    /// <summary>Local hour (0-23) <see cref="TrueBlackDark"/> auto-enables, null = no schedule (manual toggle only). Reuses the same periodic time-check as <see cref="Entities.ThemeAutoMode.Scheduled"/>.</summary>
    public int? TrueBlackAutoHour { get; set; }

    /// <summary>
    /// User-picked accent override hex, global (not per-theme) - null means no override, use the
    /// active theme's own accent. When set, derived accentText/accentSoft/glow are computed
    /// bg-luminance-aware against whichever theme is active (never a fixed darken-only rule).
    /// </summary>
    public string? AccentOverrideHex { get; set; }

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
    /// Whether the comic reader's floating chrome clusters (<c>ReaderScreenViewModel.ShowChrome</c>)
    /// fade out after <c>OverlayAutoHideDelay</c> of pointer inactivity, or stay permanently visible.
    /// Default true, matching the hardcoded-always-on behavior this setting replaces - real user
    /// report 2026-09-16: the idle-fade is sensitive to any pointer movement at all (every
    /// <c>PointerMoved</c> over the reading canvas restarts the timer unconditionally), which reads
    /// as "never hides" for anyone whose hand naturally rests near the mouse while reading; this
    /// gives them a way to turn it off entirely instead. No direct CE equivalent - checked
    /// <c>ExtendedSettings.AutoHideCursorDuration</c> (a numeric OS-cursor-hide delay, not a chrome/
    /// toolbar toggle, and not exposed as an on/off checkbox in CE's own Settings UI either) and
    /// found it's a different feature, not a parity gap to port.
    /// </summary>
    public bool ReaderAutoHideChrome { get; set; } = true;

    /// <summary>
    /// Which mechanism reveals the comic reader's chrome clusters - swappable per direct user
    /// request (2026-09-16), after the per-cluster hover reveal replaced the original ambient-
    /// reveal-on-any-movement behavior outright and the user asked for both back as options rather
    /// than losing the old one. Default <see cref="ReaderChromeHoverMode.PerCluster"/>, matching
    /// today's shipped behavior; <see cref="ReaderChromeHoverMode.Ambient"/> restores the original
    /// "any pointer movement shows everything, then idle-fades" behavior. See
    /// <see cref="ReaderChromeHoverMode"/>'s own doc comment for what each value does.
    /// </summary>
    public ReaderChromeHoverMode ReaderChromeHoverMode { get; set; } = ReaderChromeHoverMode.PerCluster;

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
    /// Cap (in MiB) on the reader decode/cache pipeline's in-memory budget for one open book
    /// (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §5).
    /// <see langword="null"/> = Auto: <c>clamp(25% physical RAM, 128 MiB, 512 MiB)</c>. Only one
    /// reader is open at a time, so this is the whole-reader budget, split internally across
    /// decoded pages, the thumbnail rail, and the compressed-bytes tier. No CE equivalent - CE's
    /// fixed <c>MemoryPageCacheCount</c> is superseded by the adaptive byte budget.
    /// </summary>
    public int? ReaderMemoryLimitMb { get; set; }

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

    /// <summary>
    /// Which bundled texture is used when <see cref="ImageBackgroundMode"/> is
    /// <see cref="ImageBackgroundMode.Texture"/> (docs/superpowers/specs/2026-09-10-reader-backlog-
    /// batch-b-design.md Item 1) - a texture <b>id</b> (<c>"neutral-dark"</c> / <c>"carbon"</c> /
    /// <c>"linen"</c>), never a file path (CE stores a path; deliberate deviation). Null / empty /
    /// unknown resolves to the first texture. Global-only, like the rest of the background settings.
    /// </summary>
    public string? BackgroundTexture { get; set; }

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
    /// 2026-09-03: Library has a single sort/group pool for both card granularities. This one
    /// column now drives per-series <b>and</b> per-issue cards; the old series-only
    /// <c>LibrarySortField</c> / <c>LibrarySortDirection</c> / <c>LibraryGroupField</c> columns
    /// were dropped (migration <c>UnifyLibrarySortGroupFields</c>).
    /// </remarks>
    public IssueListSortField LibraryIssueListSortField { get; set; } = IssueListSortField.Added;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public SortDirection LibraryIssueListSortDirection { get; set; } = SortDirection.Descending;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public IssueListGroupField LibraryIssueListGroupField { get; set; } = IssueListGroupField.None;

    /// <summary>
    /// Which <c>VirtualTagDefinition</c> is selected when <see cref="LibraryIssueListSortField"/> is
    /// <see cref="IssueListSortField.VirtualTag"/> - meaningless otherwise. Same shape as
    /// <c>SmartListCondition.VirtualTagId</c> (docs/superpowers/specs/2026-09-12-library-sort-
    /// group-axes-design.md §1). Not a foreign key - a deleted tag just falls back to the default
    /// sort field at load time rather than needing cascade cleanup.
    /// </summary>
    public int? LibrarySortVirtualTagId { get; set; }

    /// <summary>See <see cref="LibrarySortVirtualTagId"/>, but for <see cref="LibraryIssueListGroupField"/>.</summary>
    public int? LibraryGroupVirtualTagId { get; set; }

    /// <summary>
    /// Card granularity - series-aggregate cards vs per-issue tiles, independent of
    /// <see cref="LibraryViewMode"/>'s layout *shape*. See <see cref="LibraryContentGranularity"/>.
    /// </summary>
    public LibraryContentGranularity LibraryGranularity { get; set; } = LibraryContentGranularity.Issue;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public LibraryViewMode LibraryViewMode { get; set; } = LibraryViewMode.PosterGrid;

    /// <summary>
    /// Cover-fit/tile-style toggle within <see cref="LibraryViewMode.PosterGrid"/> - see
    /// <see cref="LibraryGridCoverFit"/>. Master-Detail redesign (docs/superpowers/specs/
    /// 2026-09-14-library-visual-redesign-design.md §2).
    /// </summary>
    public LibraryGridCoverFit LibraryGridCoverFit { get; set; } = LibraryGridCoverFit.Poster;

    /// <summary>
    /// Live preview panel's `GridSplitter`-adjusted width in the Master-Detail layout
    /// (docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md §4). Persisted so a
    /// resize survives a restart, same rationale as <see cref="LibraryDetailsColumns"/>' persisted
    /// column widths.
    /// </summary>
    public double LibraryPreviewPanelWidth { get; set; } = 320;

    /// <summary>
    /// Manual collapse for the live preview panel in <see cref="LibraryViewMode.PosterGrid"/>/
    /// <see cref="LibraryViewMode.List"/> (independent of <see cref="LibraryViewMode.DetailsTable"/>'s
    /// own unconditional auto-hide) - toolbar toggle + Ctrl+B
    /// (docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md §4).
    /// </summary>
    public bool IsLibraryPreviewPanelVisible { get; set; } = true;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public double LibraryGridDensity { get; set; } = 1.0;

    /// <summary>
    /// Poster-grid tile title row on/off (UI rework Phase 4a - docs/superpowers/specs/
    /// 2026-08-27-library-browsing-4a-poster-grid-design.md). Off reproduces the former
    /// <c>CoverOnlyGrid</c>. Auto-hidden by the ViewModel below a card-width threshold regardless.
    /// </summary>
    public bool LibraryShowTileTitles { get; set; } = true;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryShowUnreadBadge { get; set; } = true;

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryShowPublisherBadge { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryShowLanguageBadge { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryUseLanguageIcon { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryShowContinueReadingButton { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public string? LibrarySearchQuery { get; set; }

    /// <summary>Field scope for <see cref="LibrarySearchQuery"/> - see <see cref="SearchMode"/>.</summary>
    public SearchMode LibrarySearchMode { get; set; } = SearchMode.All;

    /// <summary>
    /// See <see cref="LibraryIssueListSortField"/>. Mutually exclusive with <see cref="LibraryActiveCollectionId"/>
    /// - null means "All Series" (both null) or this content type is the active sidebar filter.
    /// </summary>
    public ContentType? LibraryActiveContentType { get; set; }

    /// <summary>
    /// See <see cref="LibraryActiveContentType"/>. If the referenced <c>Collection</c> no longer
    /// exists at load time (deleted since last session), <c>LibraryScreenViewModel</c> falls back to
    /// "All Series" rather than rendering a silently empty grid.
    /// </summary>
    public int? LibraryActiveCollectionId { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryFilterUnreadOnly { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
    public bool LibraryFilterMissingIssues { get; set; }

    /// <summary>See <see cref="LibraryIssueListSortField"/>.</summary>
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
    /// with the same HasDefaultValue/HasSentinel treatment as <see cref="LibraryIssueListSortField"/>.
    /// </summary>
    public BooksSortField BooksSortField { get; set; } = BooksSortField.Title;

    /// <summary>See <see cref="BooksSortField"/>.</summary>
    public SortDirection BooksSortDirection { get; set; } = SortDirection.Ascending;

    /// <summary>See <see cref="BooksSortField"/>.</summary>
    public BooksGroupField BooksGroupField { get; set; } = BooksGroupField.None;

    /// <summary>Books-screen counterpart to <see cref="LibraryActiveWorkspaceId"/> - same cosmetic-label-only role.</summary>
    public int? BooksActiveWorkspaceId { get; set; }

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
    /// Whether clicking the main window's close button asks "Close Paperbunkr?" before actually
    /// quitting - on-screen feedback, no CE precedent, default false like every other opt-in toggle
    /// with no CE default to match. Only gates a close that would actually exit the app - when
    /// <see cref="MinimizeToTray"/> is already redirecting the close button to the tray instead, this
    /// doesn't fire (confirming an action that doesn't quit anything would be pure friction).
    /// </summary>
    public bool ConfirmBeforeClose { get; set; }

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
    /// The Paperbunkr version string last seen running on this machine - the four-part assembly
    /// version, e.g. "0.3.0.0" (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
    /// design.md). Compared against the running version on startup to decide whether to show the
    /// "What's New" overlay; written every launch regardless of whether the overlay showed, so a
    /// patch release with no changelog entry still advances the marker. Null until the first launch
    /// that writes it - a null here means "fresh install", which shows nothing (there's no prior
    /// version to have changed from).
    /// </summary>
    public string? LastRunVersion { get; set; }

    /// <summary>
    /// Library search box's remembered past queries (docs/superpowers/specs/2026-08-31-library-
    /// search-suggestions-design.md) - JSON-serialized <c>List&lt;string&gt;</c>, most-recent-first,
    /// capped at 8, case-insensitive deduped. Null/empty means no history yet. JSON rather than
    /// <see cref="LibraryDetailsColumns"/>'s comma-join, since a search query can legally contain a
    /// comma (an enum name never can).
    /// </summary>
    public string? LibraryRecentSearches { get; set; }

    /// <summary>
    /// The Library workspace last applied from the toolbar switcher (docs/superpowers/specs/
    /// 2026-09-03-library-saved-workspaces-design.md). Purely cosmetic - drives the dropdown's
    /// label. Set on apply, cleared to null the moment any governed field changes for a reason
    /// other than an apply. Nothing branches on it; a stale id (workspace deleted) just falls the
    /// label back to the neutral "Workspace" text. Nullable, no HasDefaultValue/HasSentinel - same
    /// treatment as <see cref="LibraryActiveCollectionId"/>.
    /// </summary>
    public int? LibraryActiveWorkspaceId { get; set; }

    /// <summary>
    /// Whether the app checks GitHub Releases for a newer version on startup (docs/superpowers/specs/
    /// 2026-09-01-auto-update-and-changelog-design.md). Default true. Persisted from either the
    /// update-available dialog's own opt-out checkbox or the Preferences → About toggle - same
    /// setting, two entry points, matching CE's own "never check for updates on startup" flag
    /// (MainForm.cs HiddenMessageBoxes.DoNotCheckForUpdate).
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Gates the periodic background sweep that retroactively classifies series still at
    /// <see cref="ContentType.Unknown"/> via <see cref="Metadata.PublisherContentTypeClassifier"/>
    /// (docs/superpowers/specs/2026-08-30-publisher-content-type-classification-design.md),
    /// mirroring <see cref="AutoBackupEnabled"/>'s startup-trigger shape
    /// (<c>BackupService.RunAutoBackupIfDue</c>). Null means "never run" - the sweep fires on a
    /// 7-day interval and only advances this on full completion, so an interrupted pass retries
    /// next launch.
    /// </summary>
    public DateTime? LastContentTypeSweepUtc { get; set; }

    // --- Books reader ergonomics global defaults (docs/superpowers/specs/2026-09-01-books-reader-
    // ergonomics-and-annotations-design.md) - falls back for any Book with no per-book override
    // column set (see Book.FontSizeOverride etc.). Defaults match BookReaderSettings' own pre-
    // existing in-code constructor defaults, so an existing settings row reproduces prior
    // session-only behavior exactly.

    public double BookReaderFontSize { get; set; } = 17;

    public BookFontFamilyOption BookReaderFontFamily { get; set; } = BookFontFamilyOption.Serif;

    public BookLineSpacingOption BookReaderLineSpacing { get; set; } = BookLineSpacingOption.Normal;

    public double BookReaderCharacterSpacing { get; set; }

    public double BookReaderWordSpacing { get; set; }

    /// <summary>Matches the fixed value this replaces (BookReaderScreen.axaml's old hardcoded paragraph <c>Margin="0,0,0,10"</c>).</summary>
    public double BookReaderParagraphSpacing { get; set; } = 10;

    /// <summary>Matches the fixed value this replaces (BookReaderScreen.axaml's old hardcoded <c>ScrollViewer Padding="40,70,40,60"</c>).</summary>
    public double BookReaderPageMargin { get; set; } = 40;

    public BookTheme BookReaderTheme { get; set; } = BookTheme.MatchAppSkin;

    /// <summary>Whether the reader's chrome (toolbar/progress bar) auto-fades after ~2.5s of no pointer movement while visible. Default true.</summary>
    public bool BookReaderAutoHideChrome { get; set; } = true;

    // --- File metadata write-back (docs/superpowers/specs/2026-09-03-file-metadata-write-back-
    // design.md). Mirrors CE's three checkboxes (Settings.cs: updateComicFiles /
    // autoUpdateComicsFiles / updateComicBookFiles), all of which default false there too - nothing
    // touches a user's original comic files until they explicitly opt in.

    /// <summary>
    /// Master switch for writing edited metadata back into comic files' embedded
    /// <c>ComicInfo.xml</c>. Off means no file is ever written; the two settings below are inert.
    /// CE: <c>Settings.UpdateComicFiles</c> ("Allow writing of Book info into files"), default false.
    /// </summary>
    public bool WriteMetadataToFiles { get; set; }

    /// <summary>
    /// When <see cref="WriteMetadataToFiles"/> is on: whether a qualifying metadata edit triggers a
    /// debounced background write automatically. Off means writes happen only via the explicit
    /// "Write metadata to files" action (Library context menu / Preferences button). CE:
    /// <c>Settings.AutoUpdateComicsFiles</c> ("Book files are updated automatically"), default false.
    /// </summary>
    public bool WriteMetadataAutomatically { get; set; }

    /// <summary>
    /// When <see cref="WriteMetadataToFiles"/> is on: whether a versioned <c>paperbunkr.json</c>
    /// sidecar entry is also written into the archive, carrying fields with no <c>ComicInfo.xml</c>
    /// home (tag categories/weights, personal rating, review, book age, per-page rotation, proposed
    /// values). Deliberate Paperbunkr deviation from CE's <c>UpdateComicBookFiles</c> ("Allow
    /// writing of Library info into files"), which embeds CE's proprietary <c>ComicBook.xml</c>
    /// instead - see the design doc. Default false.
    /// </summary>
    public bool WriteNativeSidecar { get; set; }

    // --- Behavior settings, second batch (docs/superpowers/specs/2026-09-04-behavior-settings-
    // batch2-design.md). Follows the first batch (OpenLastPage/AutoNavigateComics above); each gates
    // something that already works today. Defaults chosen so an existing settings row reproduces
    // current behavior exactly - the two that Paperbunkr already does unconditionally default true,
    // the two new opt-in behaviors default to CE's own false.

    /// <summary>
    /// Whether launch restores the screen active when the app last closed (<see cref="LastScreenKey"/>),
    /// or always opens Home. CE: <c>Settings.OpenLastFile</c> ("Reopen Books from last session"),
    /// default true. A <c>--open</c> CLI deep link still wins regardless (App.axaml.cs checks it first).
    /// </summary>
    public bool RestoreSessionOnStartup { get; set; } = true;

    /// <summary>
    /// Whether reaching the end of a comic (paging past the last page with no next issue to advance
    /// to) auto-opens the Quick Rate overlay for that issue. CE: <c>Settings.AutoShowQuickReview</c>
    /// ("Show Quick Review Dialog after finishing Book"), default false. Fires at most once per
    /// reader-load session.
    /// </summary>
    public bool PromptReviewOnFinish { get; set; }

    // --- Tracker behavior (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md) ---

    /// <summary>Open the tracker link panel automatically the first time a source-linked manga
    /// series is opened while a tracker account is connected. Default on.</summary>
    public bool TrackerAutoOpenLinkPanel { get; set; } = true;

    /// <summary>Push progress to linked trackers when the comic reader finishes an issue. Default on.</summary>
    public bool TrackerUpdateAfterReading { get; set; } = true;

    /// <summary>What a manual mark-as-read does for linked trackers. Default Always.</summary>
    public TrackerAutoUpdateMode TrackerUpdateOnMarkRead { get; set; } = TrackerAutoUpdateMode.Always;

    /// <summary>Pull remote progress when a linked series' detail screen opens. Default OFF - a pull
    /// rewrites local read state, unlike a forward-only push.</summary>
    public bool TrackerAutoSyncFromTrackers { get; set; }

    /// <summary>Pin a series' already-linked metadata source as the first tracker-link candidate. Default on.</summary>
    public bool TrackerUseSourceMetadata { get; set; } = true;

    /// <summary>
    /// Whether files/folders/.cbl can be imported by dragging them onto the Library or Reading List
    /// screens. CE: <c>Settings.DisableDragDrop</c> (inverted sense), default false there = drop
    /// enabled, so default true here.
    /// </summary>
    public bool EnableDragDropImport { get; set; } = true;

    /// <summary>
    /// Whether hovering the collapsed nav rail temporarily expands it (docs/superpowers/specs/
    /// 2026-08-24-navigation-shell-motion-system-design.md's hover-expand mechanism) - user-facing
    /// toggle added 2026-09-05 for people who found the hover-expand distracting. Pinning
    /// (<see cref="NavRailPinned"/>) is unaffected either way - that's a separate, explicit "always
    /// expanded" mechanism. Default true preserves today's behavior for existing installs.
    /// </summary>
    public bool NavRailHoverExpandEnabled { get; set; } = true;

    /// <summary>
    /// UTC timestamp of the last completed full cover-content verification pass (docs/superpowers/
    /// specs/2026-08-30-cover-thumbnail-content-verification-design.md). Null means never run.
    /// Only set on successful completion - an interrupted pass (app closed mid-run) retries next
    /// launch rather than being marked done early.
    /// </summary>
    public DateTime? LastCoverVerificationUtc { get; set; }

    /// <summary>
    /// How loudly a finished scheduled maintenance task announces itself (docs/superpowers/specs/
    /// 2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1). Default
    /// <see cref="ScheduledTaskNotificationLevel.OnlyFailures"/> - routine upkeep shouldn't nag, but
    /// a persistently-failing task should be visible.
    /// </summary>
    public ScheduledTaskNotificationLevel ScheduledTaskNotificationLevel { get; set; } = ScheduledTaskNotificationLevel.OnlyFailures;

    /// <summary>
    /// UTC timestamp of the last completed Library Health Verify pass (docs/superpowers/specs/
    /// 2026-09-06-missing-files-library-health-design.md). Null means never run ("Never" in the
    /// summary row). Only set on successful completion, same contract as
    /// <see cref="LastCoverVerificationUtc"/> - an interrupted pass retries next time, not marked
    /// done early.
    /// </summary>
    public DateTime? LastLibraryHealthVerifyUtc { get; set; }

    /// <summary>
    /// Consecutive missing Verify passes before an issue is eligible for "Remove All Confirmed
    /// Missing" (docs/superpowers/specs/2026-09-07-library-health-redesign-design.md §7). Was a
    /// hardcoded <c>LibraryHealthService.ConfirmedMissingThreshold</c> constant; surfaced as a
    /// setting in this redesign. Default 2 preserves the prior hardcoded behavior.
    /// </summary>
    public int LibraryHealthConfirmedMissingThreshold { get; set; } = 2;

    /// <summary>
    /// Whether a Scan Now automatically runs Library Health's "Remove All Confirmed Missing" (same
    /// two-strikes eligibility, no confirmation dialog) once the scan completes (docs/superpowers/
    /// specs/2026-09-06-scan-missing-file-handling-design.md). CE: Settings
    /// .RemoveMissingFilesOnFullScan, default false. Deliberately goes through the existing
    /// two-strikes grace window rather than CE's immediate single-pass removal, plus an additional
    /// drive-reachability check the manual button doesn't need - see design doc.
    /// </summary>
    public bool AutoRemoveMissingOnScan { get; set; }

    /// <summary>
    /// Whether a file path recorded in <see cref="Entities.RemovedFilePath"/> is skipped during
    /// import instead of being silently re-added (docs/superpowers/specs/2026-09-06-scan-missing-
    /// file-handling-design.md). CE: Settings.DontAddRemoveFiles, default false. The table itself is
    /// always populated on removal regardless of this setting - see design doc.
    /// </summary>
    public bool DontReimportRemovedFiles { get; set; }

    // --- Cosmetic Preferences micro-toggles (docs/superpowers/specs/2026-09-13-preferences-
    // cosmetic-toggles-design.md), CE's "Browser"/"Import & Export" Settings categories.

    /// <summary>
    /// Whether a comic cover thumbnail fades in from transparent on its first real decode (never on
    /// a cache hit repaint). CE: <c>Settings.FadeInThumbnails</c>, default true.
    /// </summary>
    public bool FadeInThumbnails { get; set; } = true;

    /// <summary>
    /// Eased mouse-wheel scrolling in the Library grids (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §6):
    /// each wheel notch animates to its target instead of jumping. A deliberate Paperbunkr addition - ComicRack has no such setting.
    /// Default true; Reduced Motion turns it off regardless. Additive column, so an older build sharing this database ignores it.
    /// </summary>
    public bool SmoothScrolling { get; set; } = true;

    /// <summary>
    /// Whether hovering or selecting a Poster/Panorama tile peeks the comic's real second page from
    /// behind its cover - CE's own "preview the next page" affordance, not a status badge. CE:
    /// <c>Settings.DogEarThumbnails</c>, default true. Gated on the issue having no custom cover
    /// override, more than one page, and not being a missing file - see design doc.
    /// </summary>
    public bool DogEarThumbnails { get; set; } = true;

    /// <summary>
    /// Whether hovering a library tile (any view except Tiles) pops a small metadata preview -
    /// thumbnail, title, writer/penciller, summary excerpt, file size, format. CE:
    /// <c>Settings.ShowToolTips</c>, default false.
    /// </summary>
    public bool ShowToolTips { get; set; }

    /// <summary>
    /// Whether a hover-reveal badge on the tile shows the issue's numeric rating. Shares the tile's
    /// bottom-right corner with the selection checkbox - hidden whenever any issue is selected. CE:
    /// <c>Settings.NumericRatingThumbnails</c>, default true. No CE star-strip fallback for the off
    /// state - see design doc.
    /// </summary>
    public bool NumericRatingThumbnails { get; set; } = true;

    /// <summary>
    /// Whether exporting a reading list to <c>.cbl</c> embeds each member issue's file path
    /// alongside its metadata identifiers. CE: <c>Settings.ExportedListsContainFilenames</c>, default
    /// false. CBL export only - CE itself never reads this setting on import, and Paperbunkr's CSV
    /// reading-list format is untouched.
    /// </summary>
    public bool ExportedListsContainFilenames { get; set; }
}

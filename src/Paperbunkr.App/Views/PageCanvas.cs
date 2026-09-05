using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Reader screen's page canvas (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md
/// §4/§6). Renders <see cref="Page"/> via <see cref="ReaderPageVisualHandler"/>, a
/// <see cref="CompositionCustomVisualHandler"/> attached in <see cref="OnAttachedToVisualTree"/> -
/// unified onto the same composition-visual pipeline continuous mode needs (docs/superpowers/specs/
/// 2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §1/§4), replacing the
/// original <c>ICustomDrawOperation</c>-based renderer. Clicking the left/right half or pressing
/// <see cref="LeftKey"/>/<see cref="RightKey"/> (remappable via Preferences, default the physical
/// Left/Right arrows) invokes <see cref="LeftCommand"/>/<see cref="RightCommand"/> - bound from
/// XAML like every other command in this codebase, rather than a code-behind event the ViewModel
/// would need to subscribe to. Named spatially, not semantically ("Previous"/"Next"), per
/// docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §3 - which physical side means
/// "forward" depends on reading direction, and PageCanvas itself has no opinion on that.
///
/// Also handles zoom/pan gestures (mouse wheel, drag, double-click, touch tap-zones/flick) per
/// docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md - input handling
/// stays exactly where it was; only the drawing mechanism moved (spec §4's explicit "input handling
/// stays on PageCanvas itself" note). <see cref="ZoomLevel"/>/<see cref="PanOffsetX"/>/
/// <see cref="PanOffsetY"/> are two-way bound - <see cref="ViewModels.ReaderScreenViewModel.ZoomLevel"/>
/// is the clamp authority, not this control; this control writes proposed values and trusts the
/// TwoWay round-trip to reflect the clamped result back, the same mechanism a TwoWay-bound
/// <c>Slider.Value</c> relies on.
/// </summary>
public class PageCanvas : Control
{
    private const double KeyPanStep = 40;
    private const double WheelZoomStep = 0.25;
    private const double MinFlickDistance = 60;
    private const double MaxFlickDurationMs = 400;
    private const double WheelScrollStepPixels = 80;
    private const double PageJumpFraction = 0.9;

    public static readonly StyledProperty<Bitmap?> PageProperty =
        AvaloniaProperty.Register<PageCanvas, Bitmap?>(nameof(Page));

    /// <summary>The second page of a double-page spread (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §4), null for solo display - always changes alongside <see cref="Page"/>, never independently.</summary>
    public static readonly StyledProperty<Bitmap?> SecondaryPageProperty =
        AvaloniaProperty.Register<PageCanvas, Bitmap?>(nameof(SecondaryPage));

    public static readonly StyledProperty<ICommand?> LeftCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(LeftCommand));

    public static readonly StyledProperty<ICommand?> RightCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(RightCommand));

    /// <summary>F/F11 fullscreen toggle (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §7) - handled here alongside every other Reader key, unlike <see cref="LeftCommand"/>/<see cref="RightCommand"/> this fires regardless of paged/continuous mode, checked before either branch in <see cref="OnKeyDown"/>.</summary>
    public static readonly StyledProperty<ICommand?> FullscreenToggleCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(FullscreenToggleCommand));

    /// <summary>Touch center-zone tap (docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-design.md §3) - invoked by <see cref="InvokeTouchZone"/> when <see cref="PageTurnGestureMath.ResolveZone"/> returns the reserved center-column no-op.</summary>
    public static readonly StyledProperty<ICommand?> ToggleChromeCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(ToggleChromeCommand));

    /// <summary>
    /// Always-context commands (docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-
    /// design.md §2/§3) - checked first in <see cref="OnKeyDown"/>, ahead of every mode branch,
    /// same precedence <see cref="FullscreenToggleCommand"/> already had. <see cref="SetFitModeCommand"/>
    /// is one command taking an <see cref="ImageFitMode"/> parameter, not five separate bound
    /// commands - <see cref="OnKeyDown"/> matches the pressed gesture against each Fit*Gesture
    /// property and executes this with the corresponding mode.
    /// </summary>
    public static readonly StyledProperty<ICommand?> SetFitModeCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(SetFitModeCommand));

    public static readonly StyledProperty<ICommand?> RotateClockwiseCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(RotateClockwiseCommand));

    public static readonly StyledProperty<ICommand?> RotateCounterClockwiseCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(RotateCounterClockwiseCommand));

    public static readonly StyledProperty<ICommand?> PreviousBookmarkCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(PreviousBookmarkCommand));

    public static readonly StyledProperty<ICommand?> NextBookmarkCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(NextBookmarkCommand));

    public static readonly StyledProperty<ICommand?> ZoomInCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(ZoomInCommand));

    public static readonly StyledProperty<ICommand?> ZoomOutCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(ZoomOutCommand));

    public static readonly StyledProperty<bool> HighQualityDisplayProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(HighQualityDisplay), defaultValue: true);

    public static readonly StyledProperty<KeyGesture> LeftKeyProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(LeftKey), defaultValue: new KeyGesture(Key.Left));

    public static readonly StyledProperty<KeyGesture> RightKeyProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(RightKey), defaultValue: new KeyGesture(Key.Right));

    /// <summary>
    /// Pan (zoomed paged mode) and scroll (continuous mode) direction gestures, plus continuous
    /// mode's page-jump/start/end gestures (docs/superpowers/specs/2026-08-16-remappable-reader-
    /// shortcuts-design.md §1/§3) - independently remappable per direction, per mode, per user
    /// direction (not unified into one "move" command the way <see cref="LeftKey"/>/
    /// <see cref="RightKey"/> already are for spatial page-turn). Defaults reproduce today's
    /// hardcoded arrow/PageUp/PageDown/Home/End behavior exactly.
    /// </summary>
    public static readonly StyledProperty<KeyGesture> PanLeftGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(PanLeftGesture), defaultValue: new KeyGesture(Key.Left));

    public static readonly StyledProperty<KeyGesture> PanRightGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(PanRightGesture), defaultValue: new KeyGesture(Key.Right));

    public static readonly StyledProperty<KeyGesture> PanUpGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(PanUpGesture), defaultValue: new KeyGesture(Key.Up));

    public static readonly StyledProperty<KeyGesture> PanDownGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(PanDownGesture), defaultValue: new KeyGesture(Key.Down));

    public static readonly StyledProperty<KeyGesture> ScrollLeftGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollLeftGesture), defaultValue: new KeyGesture(Key.Left));

    public static readonly StyledProperty<KeyGesture> ScrollRightGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollRightGesture), defaultValue: new KeyGesture(Key.Right));

    public static readonly StyledProperty<KeyGesture> ScrollUpGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollUpGesture), defaultValue: new KeyGesture(Key.Up));

    public static readonly StyledProperty<KeyGesture> ScrollDownGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollDownGesture), defaultValue: new KeyGesture(Key.Down));

    public static readonly StyledProperty<KeyGesture> ScrollPageUpGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollPageUpGesture), defaultValue: new KeyGesture(Key.PageUp));

    public static readonly StyledProperty<KeyGesture> ScrollPageDownGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollPageDownGesture), defaultValue: new KeyGesture(Key.PageDown));

    public static readonly StyledProperty<KeyGesture> ScrollToStartGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollToStartGesture), defaultValue: new KeyGesture(Key.Home));

    public static readonly StyledProperty<KeyGesture> ScrollToEndGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ScrollToEndGesture), defaultValue: new KeyGesture(Key.End));

    /// <summary>Toggles the ViewModel-driven hands-free auto-scroll timer (docs/superpowers/specs/2026-08-16-reader-auto-scroll-design.md) - meaningless outside continuous mode, gesture-matched in the same OnKeyDown block as the other continuous-mode gestures above, not the Always-context block.</summary>
    public static readonly StyledProperty<KeyGesture> ToggleAutoScrollGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ToggleAutoScrollGesture), defaultValue: new KeyGesture(Key.S));

    public static readonly StyledProperty<ICommand?> ToggleAutoScrollCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(ToggleAutoScrollCommand));

    /// <summary>
    /// Fired once accumulated wheel-scroll pull past the continuous-mode scroll clamp crosses
    /// <see cref="OverscrollThresholdPixels"/> (docs/superpowers/specs/2026-08-23-reader-chapter-
    /// transition-design.md) - a bool parameter (true = pulled past the end, false = past the
    /// start). Meaningless outside continuous mode, same as <see cref="ToggleAutoScrollCommand"/>.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ChapterBoundaryOverscrollCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(ChapterBoundaryOverscrollCommand));

    /// <summary>
    /// Always-context gestures (docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-
    /// design.md §1/§3) - F11 stays a hardcoded secondary fullscreen trigger in <see cref="OnKeyDown"/>
    /// (an OS-level convention, not really "a shortcut" in the remappable sense); this gesture is
    /// what's actually remappable.
    /// </summary>
    public static readonly StyledProperty<KeyGesture> FullscreenToggleGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(FullscreenToggleGesture), defaultValue: new KeyGesture(Key.F));

    public static readonly StyledProperty<KeyGesture> RotateClockwiseGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(RotateClockwiseGesture), defaultValue: new KeyGesture(Key.R));

    public static readonly StyledProperty<KeyGesture> RotateCounterClockwiseGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(RotateCounterClockwiseGesture), defaultValue: new KeyGesture(Key.R, KeyModifiers.Shift));

    public static readonly StyledProperty<KeyGesture> PreviousBookmarkGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(PreviousBookmarkGesture), defaultValue: new KeyGesture(Key.PageUp, KeyModifiers.Control));

    public static readonly StyledProperty<KeyGesture> NextBookmarkGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(NextBookmarkGesture), defaultValue: new KeyGesture(Key.PageDown, KeyModifiers.Control));

    public static readonly StyledProperty<KeyGesture> ZoomInGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ZoomInGesture), defaultValue: new KeyGesture(Key.Z));

    public static readonly StyledProperty<KeyGesture> ZoomOutGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(ZoomOutGesture), defaultValue: new KeyGesture(Key.Z, KeyModifiers.Shift));

    public static readonly StyledProperty<KeyGesture> FitOriginalGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(FitOriginalGesture), defaultValue: new KeyGesture(Key.D1));

    public static readonly StyledProperty<KeyGesture> FitAllGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(FitAllGesture), defaultValue: new KeyGesture(Key.D2));

    public static readonly StyledProperty<KeyGesture> FitWidthGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(FitWidthGesture), defaultValue: new KeyGesture(Key.D3));

    public static readonly StyledProperty<KeyGesture> FitHeightGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(FitHeightGesture), defaultValue: new KeyGesture(Key.D4));

    public static readonly StyledProperty<KeyGesture> FitBestGestureProperty =
        AvaloniaProperty.Register<PageCanvas, KeyGesture>(nameof(FitBestGesture), defaultValue: new KeyGesture(Key.D5));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(ZoomLevel), defaultValue: ZoomPanMath.MinZoom,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> PanOffsetXProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(PanOffsetX), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> PanOffsetYProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(PanOffsetY), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Fit-mode/rotation controls (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-
    /// controls-design.md). Default values (<see cref="ImageFitMode.Fit"/>/<c>false</c>/<c>0</c>)
    /// exactly reproduce this control's pre-existing behavior, so the Novels PDF reader (which
    /// shares this exact control but has no fit-mode/rotation concept of its own) needs zero
    /// changes - it simply never binds these.
    /// </summary>
    public static readonly StyledProperty<ImageFitMode> FitModeProperty =
        AvaloniaProperty.Register<PageCanvas, ImageFitMode>(nameof(FitMode), defaultValue: ImageFitMode.Fit);

    public static readonly StyledProperty<bool> FitOnlyIfOversizedProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(FitOnlyIfOversized));

    /// <summary>
    /// Plain-wheel scroll/pan speed multiplier, backing <c>AppSettings.MouseWheelSpeed</c>
    /// (docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md - CE:
    /// <c>Settings.MouseWheelSpeed</c>, governs plain-wheel pan, not Ctrl+wheel zoom). Default 1.0
    /// reproduces the fixed constant this replaces, so the Novels PDF reader (shares this control,
    /// never binds this) is unaffected.
    /// </summary>
    public static readonly StyledProperty<double> WheelPanStepProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(WheelPanStep), defaultValue: 1.0);

    /// <summary>0/90/180/270 only - not validated here, the ViewModel owns wrapping the value (same division of responsibility as <see cref="ZoomLevel"/>'s clamp authority).</summary>
    public static readonly StyledProperty<int> ManualRotationDegreesProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(ManualRotationDegrees));

    /// <summary>
    /// Composed with <see cref="ManualRotationDegrees"/> here, not by the ViewModel, since the
    /// landscape-vs-portrait check needs <see cref="Page"/>'s actual decoded pixel size, which this
    /// control already has - the ViewModel only knows a bitmap exists, not its dimensions.
    /// </summary>
    public static readonly StyledProperty<bool> AutoRotateProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(AutoRotate));

    /// <summary>
    /// Per-page persisted rotation override (docs/ce-feature-inventory.md §A) - 0/90/180/270, same
    /// non-validation convention as <see cref="ManualRotationDegrees"/>. Composed additively with the
    /// session-only rotation in <see cref="EffectiveRotationDegrees"/> rather than replacing it, so
    /// the two rotation sources (persisted per-page, and this session's manual/auto rotate) stack
    /// instead of one silently overriding the other. Deliberately scoped to paged reading mode only -
    /// continuous mode renders several visible pages at once from its own layout-window path (not
    /// <see cref="EffectiveRotationDegrees"/>), and giving each visible page its own rotation there is
    /// a larger, separate piece of work; not consulted there yet.
    /// </summary>
    public static readonly StyledProperty<int> PageRotationOverrideDegreesProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(PageRotationOverrideDegrees));

    /// <summary>
    /// Continuous-mode support (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-
    /// chrome-overlays-design.md §4/§5). Default <see cref="ReadingMode.LeftToRight"/> reproduces
    /// today's paged behavior exactly, so the Novels PDF reader (never binds this) is unaffected.
    /// </summary>
    public static readonly StyledProperty<ReadingMode> ReadingModeProperty =
        AvaloniaProperty.Register<PageCanvas, ReadingMode>(nameof(ReadingMode));

    /// <summary>
    /// Continuous mode's page source - unlike paged mode's single <see cref="Page"/> bitmap, this
    /// control pulls whichever pages its layout window needs directly, since which pages are needed
    /// changes continuously with <see cref="ScrollOffset"/> rather than on discrete page-turns.
    /// Never bound/read when <see cref="ReadingMode"/> is a paged mode.
    /// </summary>
    public static readonly StyledProperty<IPageImageDecoder?> DecoderProperty =
        AvaloniaProperty.Register<PageCanvas, IPageImageDecoder?>(nameof(Decoder));

    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(PageCount));

    /// <summary>
    /// Split-page part navigation (docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-
    /// design.md §1) - 0-based index of the current viewport-sized "part" within the current page
    /// (or double-page spread) when it's zoomed past what the viewport shows. No external writer -
    /// recomputed by <see cref="UpdatePartLabel"/> whenever page/zoom/pan/fit changes, so plain
    /// OneWay is enough (unlike <see cref="PageCountProperty"/>'s TwoWay-bound siblings elsewhere in
    /// this file, which the ViewModel itself sets).
    /// </summary>
    public static readonly StyledProperty<int> CurrentPartProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(CurrentPart));

    /// <summary>Total part count for the current page/spread at the current zoom/fit - <c>1</c> when it fits the viewport (no parts to step through). See <see cref="CurrentPartProperty"/>.</summary>
    public static readonly StyledProperty<int> PartCountProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(PartCount), defaultValue: 1);

    /// <summary>
    /// "Part 2/4"-style display text for ReaderScreen.axaml's Navigate cluster, empty when
    /// <see cref="PartCount"/> is <c>1</c> (no parts to show) - a computed string rather than making
    /// the XAML compose <see cref="CurrentPart"/>/<see cref="PartCount"/> itself via a MultiBinding,
    /// since the only consumer is this one label and <c>StringConverters.IsNotNullOrEmpty</c>
    /// already gives the empty-string case a free <c>IsVisible</c> binding with no new converter.
    /// </summary>
    public static readonly StyledProperty<string> PartLabelProperty =
        AvaloniaProperty.Register<PageCanvas, string>(nameof(PartLabel), defaultValue: "");

    /// <summary>Continuous mode's scroll position, in stack space - the main-axis analog of <see cref="PanOffsetX"/>/<see cref="PanOffsetY"/> (which continuous mode reuses for cross-axis pan when zoomed, per <see cref="ReaderLayoutModel.ComputeContinuousLayout"/>'s <c>crossAxisPanOffset</c>).</summary>
    public static readonly StyledProperty<double> ScrollOffsetProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(ScrollOffset), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// "Current page" for continuous mode (docs/superpowers/specs/2026-08-10-reader-polish-
    /// continuous-scroll-chrome-overlays-design.md §6) - whichever page's midpoint is nearest the
    /// viewport center, recomputed every <see cref="PushContinuousVisualData"/> pass via
    /// <see cref="ReaderLayoutModel.NearestPageToViewportCenter"/> and written out TwoWay so
    /// <see cref="ViewModels.ReaderScreenViewModel"/> can drive <c>PageLabel</c>/<c>LastPageRead</c>
    /// off it without this control needing to know anything about persistence. Deliberately not in
    /// <see cref="RenderAffectingProperties"/> - it's an output of a render pass, not an input to
    /// one; including it would just be a harmless no-op given the property-changed filter below, but
    /// leaving it out documents the direction of data flow. Default -1 (no page determined yet,
    /// matches <see cref="ReaderLayoutModel.NearestPageToViewportCenter"/>'s own empty-list sentinel).
    /// </summary>
    public static readonly StyledProperty<int> CurrentContinuousPageIndexProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(CurrentContinuousPageIndex), defaultValue: -1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Live brightness/contrast/saturation/gamma (docs/superpowers/specs/2026-08-10-reader-polish-
    /// continuous-scroll-chrome-overlays-design.md §9) - already the *effective* value
    /// (<c>ViewModels.ReaderScreenViewModel</c>'s global-default-plus-per-issue-override sum, additive
    /// like CE's own <c>BitmapAdjustment.Add</c>), this control has no override concept of its own.
    /// Each is -100..100, the raw Preferences/toolbar slider range (spec §9) - see
    /// <see cref="Services.ImageAdjustmentMath.CreateColorMatrix"/>'s own doc comment for where that
    /// gets normalized. Applies identically to paged and continuous mode
    /// (orthogonal to <see cref="ReadingMode"/>, unlike fit-mode/rotation), so unlike most other
    /// styled properties here these are pushed via their own <see cref="AdjustmentVisualData"/>
    /// message rather than folded into <see cref="PushPagedVisualData"/>/<see cref="PushContinuousVisualData"/>.
    /// </summary>
    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(Brightness));

    public static readonly StyledProperty<double> ContrastProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(Contrast));

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(Saturation));

    public static readonly StyledProperty<double> GammaProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(Gamma));

    /// <summary>
    /// Page margin (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-
    /// overlays-design.md §10) - a separate multiplier applied on top of <see cref="ZoomLevel"/> at
    /// render time only (see <see cref="ViewModels.ReaderScreenViewModel.PageMarginMultiplier"/>'s
    /// own doc comment for why it's not folded into <see cref="ZoomLevel"/> itself). Default 1.0
    /// reproduces today's behavior exactly, so the Novels PDF reader (never binds this) is unaffected.
    /// </summary>
    public static readonly StyledProperty<double> PageMarginMultiplierProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(PageMarginMultiplier), defaultValue: 1.0);

    /// <summary>
    /// Page-turn transition style/duration (docs/superpowers/specs/2026-08-13-reader-page-transition-
    /// animations-design.md §4), backing <c>AppSettings.PageTransitionStyle</c>/
    /// <c>PageTransitionDurationMs</c>. Read directly by <see cref="TryBuildPageTransition"/> at
    /// turn-time rather than added to <see cref="RenderAffectingProperties"/> - same non-reactive-read
    /// precedent as <see cref="WheelPanStep"/>, changing the setting mid-session only needs to affect
    /// the *next* turn, not force an immediate rerender.
    /// </summary>
    public static readonly StyledProperty<PageTransitionStyle> PageTransitionStyleProperty =
        AvaloniaProperty.Register<PageCanvas, PageTransitionStyle>(nameof(PageTransitionStyle), defaultValue: PageTransitionStyle.None);

    public static readonly StyledProperty<int> PageTransitionDurationMsProperty =
        AvaloniaProperty.Register<PageCanvas, int>(nameof(PageTransitionDurationMs), defaultValue: 250);

    /// <summary>
    /// Placeholder aspect ratio for a page the layout model needs positioned but that hasn't been
    /// decoded yet (spec §4's progressive-estimate simplification, named explicitly - there's no
    /// cheap "dimensions only" read in this codebase's engine layer, and decoding every page in a
    /// book just to learn its aspect ratio up front would defeat the entire point of virtualization).
    /// A common comic-page ratio (~1:1.53); corrected to the page's real size the moment it's
    /// actually decoded (<see cref="_knownPageSizes"/>), so the stack "settles" into place as the
    /// user scrolls rather than staying wrong.
    /// </summary>
    private static readonly Size DefaultEstimatedPageSize = new(660, 1010);

    private bool _isDragging;
    private Point _dragStartPointer;
    private double _dragStartPanX;
    private double _dragStartPanY;
    private double _dragStartScrollOffset;
    private Point? _touchPressPosition;
    private DateTime _touchPressTime;
    private CompositionCustomVisual? _visual;

    /// <summary>Set while a coalesced continuous-mode render push is already queued for the next animation frame (see <see cref="RequestContinuousPush"/>).</summary>
    private bool _continuousPushQueued;

    /// <summary>Reused across <see cref="PushContinuousVisualData"/> calls so a drag scroll doesn't allocate a fresh <c>PageCount</c>-sized array on every pointer-move event - regrown only when <see cref="PageCount"/> changes.</summary>
    private Size[] _estimatedSizesBuffer = Array.Empty<Size>();

    /// <summary>
    /// Accumulated wheel-scroll pull past the continuous-mode scroll clamp, signed (positive =
    /// pulled past the end wanting to go forward, negative = past the start wanting to go
    /// backward) - docs/superpowers/specs/2026-08-23-reader-chapter-transition-design.md. Reset to
    /// 0 the moment a scroll actually moves <see cref="ScrollOffset"/> away from whichever boundary
    /// it's currently pinned at, or once <see cref="ChapterBoundaryOverscrollCommand"/> fires.
    /// Mouse-wheel input only this pass - drag/touch overscroll isn't wired to this yet, a known,
    /// named gap rather than an oversight (see the design doc's own scope note).
    /// </summary>
    private double _overscrollPull;

    /// <summary>Wheel-pull distance (see <see cref="_overscrollPull"/>) that triggers <see cref="ChapterBoundaryOverscrollCommand"/> - an arbitrary but generous threshold so a couple of stray wheel notches at the boundary (easy to do while just reading) doesn't accidentally jump a chapter.</summary>
    private const double OverscrollThresholdPixels = 220;

    /// <summary>Visual rubber-band cap (see <see cref="ReaderLayoutModel.ComputeOverscrollBump"/>) - how far the content can visually budge past the clamp regardless of how hard the pull grows.</summary>
    private const double OverscrollMaxBumpPixels = 36;

    /// <summary>Set by <see cref="ExecuteDirectional"/> immediately before invoking <see cref="LeftCommand"/>/<see cref="RightCommand"/> - the only paths adjacent paged-mode navigation takes (spec §3.1), so a pending direction here means the next <see cref="PageProperty"/> change is a turn, not a jump. Cleared after being read, and whenever <see cref="DecoderProperty"/> changes (a different issue was opened).</summary>
    private PageTransitionDirection? _pendingTransitionDirection;

    /// <summary>Wall-clock start of the most recently *animated* transition (spec §3.3) - a new turn only animates if this is far enough in the past, otherwise it falls back to an instant swap, same principle as CE's own rapid-paging throttle.</summary>
    private DateTime? _lastTransitionStartUtc;

    /// <summary>Debounces plain-wheel page turns so one precision-touchpad two-finger swipe turns
    /// roughly one page instead of a dozen (see the class doc). Only consulted on the paged-mode
    /// page-turn branch of <see cref="OnPointerWheelChanged"/>; continuous mode scrolls proportionally
    /// and needs no debounce.</summary>
    private readonly WheelPageTurnAccumulator _wheelPageTurns = new();

    /// <summary>
    /// What was last actually pushed to the visual via <see cref="PushPagedVisualData"/> (docs/
    /// superpowers/specs/2026-08-15-reader-double-page-spread-design.md's implementation-level
    /// decision, Context section) - the "old" side of a transition, replacing the previous
    /// <c>change.OldValue</c>-based approach. Adding <see cref="SecondaryPage"/> broke that: Avalonia
    /// fires <see cref="OnPropertyChanged(AvaloniaPropertyChangedEventArgs)"/> once per changed
    /// property, so there's no single event carrying "old primary + old secondary" together, and
    /// relying on the two events firing in a guaranteed order would be fragile. These two fields sidestep
    /// the ordering question entirely - <see cref="TryBuildPageTransition"/> reads them as "old" and
    /// the current <see cref="Page"/>/<see cref="SecondaryPage"/> values as "new," with no dependency
    /// on which property's change notification happens to fire first. Cleared alongside
    /// <see cref="_pendingTransitionDirection"/> whenever <see cref="DecoderProperty"/> changes.
    /// </summary>
    private Bitmap? _lastRenderedPage;
    private Bitmap? _lastRenderedSecondaryPage;

    private bool _pinchActive;
    private Point _pinchStartOrigin;
    private Point _pinchLastOrigin;
    private double _pinchLastScale = 1.0;
    private DateTime _pinchStartTime;
    private double _pinchStartZoom;
    private double _pinchStartScrollOffset;
    private double _pinchStartPanX;
    private double _pinchStartPanY;

    /// <summary>Progressive-refinement cache for continuous mode's layout (see <see cref="DefaultEstimatedPageSize"/>) - every page this control has actually decoded/rendered at least once, so re-layout after the first pass uses real sizes instead of the estimate. Cleared whenever <see cref="Decoder"/> changes (a new issue was opened).</summary>
    private readonly Dictionary<int, Size> _knownPageSizes = new();

    /// <summary>
    /// Every styled property that used to be registered with <c>AffectsRender&lt;PageCanvas&gt;</c>
    /// under the old <c>ICustomDrawOperation</c> renderer - now checked directly in
    /// <see cref="OnPropertyChanged(AvaloniaPropertyChangedEventArgs)"/> to decide whether a
    /// property change needs to push fresh data to <see cref="ReaderPageVisualHandler"/>, since
    /// composition visuals have no equivalent automatic "this property affects rendering" hookup.
    /// </summary>
    private static readonly AvaloniaProperty[] RenderAffectingProperties =
    [
        PageProperty, SecondaryPageProperty, HighQualityDisplayProperty, ZoomLevelProperty, PanOffsetXProperty, PanOffsetYProperty,
        FitModeProperty, FitOnlyIfOversizedProperty, ManualRotationDegreesProperty, AutoRotateProperty, PageRotationOverrideDegreesProperty,
        ReadingModeProperty, DecoderProperty, PageCountProperty, ScrollOffsetProperty, PageMarginMultiplierProperty
    ];

    /// <summary>
    /// Pushed via their own <see cref="AdjustmentVisualData"/> message (see <see cref="Brightness"/>'s
    /// own doc comment) rather than through <see cref="RenderAffectingProperties"/>/<see cref="PushRenderData"/>
    /// - orthogonal to paged-vs-continuous mode, so there's no reason to rebuild/resend the whole page
    /// list just because a slider moved.
    /// </summary>
    private static readonly AvaloniaProperty[] AdjustmentProperties =
    [
        BrightnessProperty, ContrastProperty, SaturationProperty, GammaProperty
    ];

    static PageCanvas()
    {
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);

        // Real bug, found via manual testing: Avalonia's ClipToBounds defaults to false, and
        // nothing else here was clipping the page draw calls to this control's own Bounds - a page
        // bigger than the canvas (Original/FitWidth/FitHeight/BestFit, or any fit mode once zoomed
        // in) painted straight through into whatever's visually adjacent (the toolbar, thumbnail
        // rail) instead of being cropped to the reader viewport. Panning alone (the CanPan/
        // HasOverflow fix above) only moves *where* the oversized content sits - without this, it
        // still bleeds out regardless of pan offset. Pre-existing gap, not introduced by fit modes -
        // zoom alone could already trigger it, just less commonly hit in practice. Kept when the
        // renderer moved onto CompositionCustomVisualHandler (spec §4) - also set explicitly on the
        // composition visual itself in OnAttachedToVisualTree, since composition child-visual
        // clipping isn't guaranteed to inherit this Control-level property automatically.
        ClipToBoundsProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    /// <summary>
    /// Two-finger touch gestures (user direction): pinch scales zoom, and the pinch origin's own
    /// movement (present even when the user isn't actively changing scale) drives two-finger-drag
    /// navigation - page-turn in paged mode, scroll in continuous/webtoon mode. Uses Avalonia's
    /// built-in <see cref="PinchGestureRecognizer"/>/<see cref="InputElement.PinchEvent"/> rather than
    /// hand-tracking individual touch-point IDs.
    /// </summary>
    public PageCanvas()
    {
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, OnPinchEnded);
    }

    public Bitmap? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public Bitmap? SecondaryPage
    {
        get => GetValue(SecondaryPageProperty);
        set => SetValue(SecondaryPageProperty, value);
    }

    public ICommand? LeftCommand
    {
        get => GetValue(LeftCommandProperty);
        set => SetValue(LeftCommandProperty, value);
    }

    public ICommand? RightCommand
    {
        get => GetValue(RightCommandProperty);
        set => SetValue(RightCommandProperty, value);
    }

    public ICommand? FullscreenToggleCommand
    {
        get => GetValue(FullscreenToggleCommandProperty);
        set => SetValue(FullscreenToggleCommandProperty, value);
    }

    public ICommand? ToggleChromeCommand
    {
        get => GetValue(ToggleChromeCommandProperty);
        set => SetValue(ToggleChromeCommandProperty, value);
    }

    public ICommand? SetFitModeCommand
    {
        get => GetValue(SetFitModeCommandProperty);
        set => SetValue(SetFitModeCommandProperty, value);
    }

    public ICommand? RotateClockwiseCommand
    {
        get => GetValue(RotateClockwiseCommandProperty);
        set => SetValue(RotateClockwiseCommandProperty, value);
    }

    public ICommand? PreviousBookmarkCommand
    {
        get => GetValue(PreviousBookmarkCommandProperty);
        set => SetValue(PreviousBookmarkCommandProperty, value);
    }

    public ICommand? NextBookmarkCommand
    {
        get => GetValue(NextBookmarkCommandProperty);
        set => SetValue(NextBookmarkCommandProperty, value);
    }

    public ICommand? RotateCounterClockwiseCommand
    {
        get => GetValue(RotateCounterClockwiseCommandProperty);
        set => SetValue(RotateCounterClockwiseCommandProperty, value);
    }

    public ICommand? ZoomInCommand
    {
        get => GetValue(ZoomInCommandProperty);
        set => SetValue(ZoomInCommandProperty, value);
    }

    public ICommand? ZoomOutCommand
    {
        get => GetValue(ZoomOutCommandProperty);
        set => SetValue(ZoomOutCommandProperty, value);
    }

    public bool HighQualityDisplay
    {
        get => GetValue(HighQualityDisplayProperty);
        set => SetValue(HighQualityDisplayProperty, value);
    }

    /// <summary>Remappable via Preferences &gt; Reader &gt; Keyboard Shortcuts (docs/Paperbunkr-Roadmap.md P5 follow-up). Defaults to the physical Left arrow.</summary>
    public KeyGesture LeftKey
    {
        get => GetValue(LeftKeyProperty);
        set => SetValue(LeftKeyProperty, value);
    }

    /// <summary>See <see cref="LeftKey"/>. Defaults to the physical Right arrow.</summary>
    public KeyGesture RightKey
    {
        get => GetValue(RightKeyProperty);
        set => SetValue(RightKeyProperty, value);
    }

    public KeyGesture PanLeftGesture
    {
        get => GetValue(PanLeftGestureProperty);
        set => SetValue(PanLeftGestureProperty, value);
    }

    public KeyGesture PanRightGesture
    {
        get => GetValue(PanRightGestureProperty);
        set => SetValue(PanRightGestureProperty, value);
    }

    public KeyGesture PanUpGesture
    {
        get => GetValue(PanUpGestureProperty);
        set => SetValue(PanUpGestureProperty, value);
    }

    public KeyGesture PanDownGesture
    {
        get => GetValue(PanDownGestureProperty);
        set => SetValue(PanDownGestureProperty, value);
    }

    public KeyGesture ScrollLeftGesture
    {
        get => GetValue(ScrollLeftGestureProperty);
        set => SetValue(ScrollLeftGestureProperty, value);
    }

    public KeyGesture ScrollRightGesture
    {
        get => GetValue(ScrollRightGestureProperty);
        set => SetValue(ScrollRightGestureProperty, value);
    }

    public KeyGesture ScrollUpGesture
    {
        get => GetValue(ScrollUpGestureProperty);
        set => SetValue(ScrollUpGestureProperty, value);
    }

    public KeyGesture ScrollDownGesture
    {
        get => GetValue(ScrollDownGestureProperty);
        set => SetValue(ScrollDownGestureProperty, value);
    }

    public KeyGesture ScrollPageUpGesture
    {
        get => GetValue(ScrollPageUpGestureProperty);
        set => SetValue(ScrollPageUpGestureProperty, value);
    }

    public KeyGesture ScrollPageDownGesture
    {
        get => GetValue(ScrollPageDownGestureProperty);
        set => SetValue(ScrollPageDownGestureProperty, value);
    }

    public KeyGesture ScrollToStartGesture
    {
        get => GetValue(ScrollToStartGestureProperty);
        set => SetValue(ScrollToStartGestureProperty, value);
    }

    public KeyGesture ScrollToEndGesture
    {
        get => GetValue(ScrollToEndGestureProperty);
        set => SetValue(ScrollToEndGestureProperty, value);
    }

    public KeyGesture ToggleAutoScrollGesture
    {
        get => GetValue(ToggleAutoScrollGestureProperty);
        set => SetValue(ToggleAutoScrollGestureProperty, value);
    }

    public ICommand? ToggleAutoScrollCommand
    {
        get => GetValue(ToggleAutoScrollCommandProperty);
        set => SetValue(ToggleAutoScrollCommandProperty, value);
    }

    public ICommand? ChapterBoundaryOverscrollCommand
    {
        get => GetValue(ChapterBoundaryOverscrollCommandProperty);
        set => SetValue(ChapterBoundaryOverscrollCommandProperty, value);
    }

    public KeyGesture FullscreenToggleGesture
    {
        get => GetValue(FullscreenToggleGestureProperty);
        set => SetValue(FullscreenToggleGestureProperty, value);
    }

    public KeyGesture RotateClockwiseGesture
    {
        get => GetValue(RotateClockwiseGestureProperty);
        set => SetValue(RotateClockwiseGestureProperty, value);
    }

    public KeyGesture RotateCounterClockwiseGesture
    {
        get => GetValue(RotateCounterClockwiseGestureProperty);
        set => SetValue(RotateCounterClockwiseGestureProperty, value);
    }

    public KeyGesture PreviousBookmarkGesture
    {
        get => GetValue(PreviousBookmarkGestureProperty);
        set => SetValue(PreviousBookmarkGestureProperty, value);
    }

    public KeyGesture NextBookmarkGesture
    {
        get => GetValue(NextBookmarkGestureProperty);
        set => SetValue(NextBookmarkGestureProperty, value);
    }

    public KeyGesture ZoomInGesture
    {
        get => GetValue(ZoomInGestureProperty);
        set => SetValue(ZoomInGestureProperty, value);
    }

    public KeyGesture ZoomOutGesture
    {
        get => GetValue(ZoomOutGestureProperty);
        set => SetValue(ZoomOutGestureProperty, value);
    }

    public KeyGesture FitOriginalGesture
    {
        get => GetValue(FitOriginalGestureProperty);
        set => SetValue(FitOriginalGestureProperty, value);
    }

    public KeyGesture FitAllGesture
    {
        get => GetValue(FitAllGestureProperty);
        set => SetValue(FitAllGestureProperty, value);
    }

    public KeyGesture FitWidthGesture
    {
        get => GetValue(FitWidthGestureProperty);
        set => SetValue(FitWidthGestureProperty, value);
    }

    public KeyGesture FitHeightGesture
    {
        get => GetValue(FitHeightGestureProperty);
        set => SetValue(FitHeightGestureProperty, value);
    }

    public KeyGesture FitBestGesture
    {
        get => GetValue(FitBestGestureProperty);
        set => SetValue(FitBestGestureProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public double PanOffsetX
    {
        get => GetValue(PanOffsetXProperty);
        set => SetValue(PanOffsetXProperty, value);
    }

    public double PanOffsetY
    {
        get => GetValue(PanOffsetYProperty);
        set => SetValue(PanOffsetYProperty, value);
    }

    public double WheelPanStep
    {
        get => GetValue(WheelPanStepProperty);
        set => SetValue(WheelPanStepProperty, value);
    }

    public ImageFitMode FitMode
    {
        get => GetValue(FitModeProperty);
        set => SetValue(FitModeProperty, value);
    }

    public bool FitOnlyIfOversized
    {
        get => GetValue(FitOnlyIfOversizedProperty);
        set => SetValue(FitOnlyIfOversizedProperty, value);
    }

    public int ManualRotationDegrees
    {
        get => GetValue(ManualRotationDegreesProperty);
        set => SetValue(ManualRotationDegreesProperty, value);
    }

    public int PageRotationOverrideDegrees
    {
        get => GetValue(PageRotationOverrideDegreesProperty);
        set => SetValue(PageRotationOverrideDegreesProperty, value);
    }

    public bool AutoRotate
    {
        get => GetValue(AutoRotateProperty);
        set => SetValue(AutoRotateProperty, value);
    }

    public ReadingMode ReadingMode
    {
        get => GetValue(ReadingModeProperty);
        set => SetValue(ReadingModeProperty, value);
    }

    public IPageImageDecoder? Decoder
    {
        get => GetValue(DecoderProperty);
        set => SetValue(DecoderProperty, value);
    }

    public int PageCount
    {
        get => GetValue(PageCountProperty);
        set => SetValue(PageCountProperty, value);
    }

    public int CurrentPart
    {
        get => GetValue(CurrentPartProperty);
        private set => SetValue(CurrentPartProperty, value);
    }

    public int PartCount
    {
        get => GetValue(PartCountProperty);
        private set => SetValue(PartCountProperty, value);
    }

    public string PartLabel
    {
        get => GetValue(PartLabelProperty);
        private set => SetValue(PartLabelProperty, value);
    }

    public double ScrollOffset
    {
        get => GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    public int CurrentContinuousPageIndex
    {
        get => GetValue(CurrentContinuousPageIndexProperty);
        set => SetValue(CurrentContinuousPageIndexProperty, value);
    }

    public double Brightness
    {
        get => GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public double Contrast
    {
        get => GetValue(ContrastProperty);
        set => SetValue(ContrastProperty, value);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public double Gamma
    {
        get => GetValue(GammaProperty);
        set => SetValue(GammaProperty, value);
    }

    public double PageMarginMultiplier
    {
        get => GetValue(PageMarginMultiplierProperty);
        set => SetValue(PageMarginMultiplierProperty, value);
    }

    public PageTransitionStyle PageTransitionStyle
    {
        get => GetValue(PageTransitionStyleProperty);
        set => SetValue(PageTransitionStyleProperty, value);
    }

    public int PageTransitionDurationMs
    {
        get => GetValue(PageTransitionDurationMsProperty);
        set => SetValue(PageTransitionDurationMsProperty, value);
    }

    private bool IsContinuous => ReadingMode is ReadingMode.VerticalContinuous or ReadingMode.HorizontalContinuous or ReadingMode.HorizontalContinuousRightToLeft or ReadingMode.Webtoon;

    /// <summary>
    /// Paged top-to-bottom mode (docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-
    /// design.md) - the paged render path (not <see cref="IsContinuous"/>), but page-turn gestures
    /// run along Y: Up/Down keys, the wheel (already vertical), top/bottom click-and-tap zones, and
    /// vertical flick. Turn animations use <see cref="PageTransitionDirection.Up"/>/
    /// <see cref="PageTransitionDirection.Down"/>.
    /// </summary>
    private bool IsPagedVertical => ReadingMode == ReadingMode.TopToBottom;

    /// <summary>The (command, animation direction) pair for a forward turn, respecting <see cref="IsPagedVertical"/>.</summary>
    private (ICommand? Command, PageTransitionDirection Direction) ForwardTurn =>
        IsPagedVertical ? (RightCommand, PageTransitionDirection.Down) : (RightCommand, PageTransitionDirection.Right);

    /// <summary>The (command, animation direction) pair for a backward turn, respecting <see cref="IsPagedVertical"/>.</summary>
    private (ICommand? Command, PageTransitionDirection Direction) BackwardTurn =>
        IsPagedVertical ? (LeftCommand, PageTransitionDirection.Up) : (LeftCommand, PageTransitionDirection.Left);

    /// <summary>
    /// Runs a resolved zone/flick turn intent (<c>true</c> = forward, <c>false</c> = back).
    /// Split-page part navigation (docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-
    /// design.md §1) intercepts this first: when the current page (or spread) is zoomed past what
    /// the viewport shows, this steps through its viewport-sized "parts" before actually turning the
    /// page - matching ComicRackCE's own <c>DisplayNextPageOrPart</c>, which only falls through to a
    /// real page turn once <c>DisplayPart</c> reports no more parts in that direction.
    /// </summary>
    private bool ExecuteTurn(bool forward)
    {
        if (!IsContinuous && TryStepPart(forward))
        {
            return true;
        }

        var (command, direction) = forward ? ForwardTurn : BackwardTurn;
        bool executed = ExecuteDirectional(command, direction);
        if (executed && !IsContinuous)
        {
            LandOnPartAfterPageTurn(forward);
        }

        return executed;
    }

    /// <summary>
    /// The content the part grid is computed against: the current page's own (rotation-adjusted)
    /// pixel size, or - when a double-page spread is active - the combined virtual spread size
    /// (<see cref="SpreadLayoutMath.ComputeCombinedSize"/>), matching how <see cref="ReaderPageVisualHandler.RenderSpread"/>
    /// already feeds that same combined size through the shared fit/zoom/pan math. Rotation is inert
    /// for spreads (that render path's own documented simplification), so this uses the raw
    /// <see cref="Page"/>/<see cref="SecondaryPage"/> pixel sizes rather than <see cref="EffectivePixelSize"/>
    /// whenever a spread is active.
    /// </summary>
    private PixelSize EffectivePartContentSize() =>
        SecondaryPage is { } secondary && Page is { } primary
            ? SpreadLayoutMath.ComputeCombinedSize(primary.PixelSize, secondary.PixelSize).Combined
            : EffectivePixelSize();

    private (int Cols, int Rows) ComputeCurrentPartGrid() =>
        PagePartMath.ComputePartGrid(Bounds.Size, EffectivePartContentSize(), ZoomLevel, FitMode, FitOnlyIfOversized);

    /// <summary>
    /// Recomputes <see cref="CurrentPart"/>/<see cref="PartCount"/> from the current page/zoom/pan/
    /// fit state - called on every relevant property change (see <see cref="OnPropertyChanged(AvaloniaPropertyChangedEventArgs)"/>)
    /// so the "Part X/Y" label (ReaderScreen.axaml's Navigate cluster) stays live even when pan
    /// changes via a free mouse-drag, not just via <see cref="TryStepPart"/>. No-ops (leaves both at
    /// their current values) in continuous mode or with no page loaded - parts are a paged-mode-only
    /// concept.
    /// </summary>
    private void UpdatePartLabel()
    {
        if (IsContinuous || Page is null)
        {
            PartCount = 1;
            CurrentPart = 0;
            PartLabel = "";
            return;
        }

        var grid = ComputeCurrentPartGrid();
        int count = PagePartMath.PartCount(grid);
        PartCount = count;
        CurrentPart = count <= 1
            ? 0
            : PagePartMath.FindNearestPart(grid, Bounds.Size, EffectivePartContentSize(),
                ZoomPanMath.ComputeBaseScale(Bounds.Size, EffectivePartContentSize(), FitMode, FitOnlyIfOversized) * ZoomLevel,
                PanOffsetX, PanOffsetY, ReadingMode == ReadingMode.RightToLeft);
        PartLabel = count > 1 ? $"Part {CurrentPart + 1}/{count}" : "";
    }

    /// <summary>Attempts to step to the next/previous grid part of the current page/spread. Returns <c>false</c> (no-op) when there's only one part, or the current part is already the last one in the requested direction - the caller then falls through to a real page turn.</summary>
    private bool TryStepPart(bool forward)
    {
        if (Page is null)
        {
            return false;
        }

        var grid = ComputeCurrentPartGrid();
        int count = PagePartMath.PartCount(grid);
        if (count <= 1)
        {
            return false;
        }

        var content = EffectivePartContentSize();
        double scale = ZoomPanMath.ComputeBaseScale(Bounds.Size, content, FitMode, FitOnlyIfOversized) * ZoomLevel;
        bool rtl = ReadingMode == ReadingMode.RightToLeft;
        int current = PagePartMath.FindNearestPart(grid, Bounds.Size, content, scale, PanOffsetX, PanOffsetY, rtl);
        int next = forward ? current + 1 : current - 1;
        if (next < 0 || next >= count)
        {
            return false;
        }

        var (x, y) = PagePartMath.PanForPart(next, grid, Bounds.Size, content, scale, ZoomLevel, rtl, FitMode, FitOnlyIfOversized);
        PanOffsetX = x;
        PanOffsetY = y;
        return true;
    }

    /// <summary>
    /// Sets the just-landed-on page's pan to its first part (forward turn) or last part (backward
    /// turn), matching CE's own landing convention (<c>NavigationOverlay</c>'s
    /// <c>part = (oldPage &gt;= newPage) ? (PartCount - 1) : 0</c>) - a stale pan value carried over
    /// from the previous page could otherwise clamp to a nonsensical spot once the new page's
    /// (possibly differently-shaped) dimensions are known. Only meaningful when the just-completed
    /// turn was <see cref="ExecuteTurn"/>'s own real-page-turn fallback, not <see cref="TryStepPart"/>'s
    /// same-page part step (which already leaves pan exactly where it should be).
    /// </summary>
    private void LandOnPartAfterPageTurn(bool forward)
    {
        if (Page is null)
        {
            return;
        }

        var grid = ComputeCurrentPartGrid();
        int count = PagePartMath.PartCount(grid);
        var content = EffectivePartContentSize();
        double scale = ZoomPanMath.ComputeBaseScale(Bounds.Size, content, FitMode, FitOnlyIfOversized) * ZoomLevel;
        bool rtl = ReadingMode == ReadingMode.RightToLeft;
        int landingPart = forward ? 0 : count - 1;

        var (x, y) = PagePartMath.PanForPart(landingPart, grid, Bounds.Size, content, scale, ZoomLevel, rtl, FitMode, FitOnlyIfOversized);
        PanOffsetX = x;
        PanOffsetY = y;
    }

    private ReaderLayoutModel.Axis ContinuousAxis => ReadingMode is ReadingMode.HorizontalContinuous or ReadingMode.HorizontalContinuousRightToLeft ? ReaderLayoutModel.Axis.Horizontal : ReaderLayoutModel.Axis.Vertical;

    private bool IsContinuousReversed => ReadingMode == ReadingMode.HorizontalContinuousRightToLeft;

    /// <summary>User direction (not the original spec): <see cref="ReadingMode.Webtoon"/> merges pages edge-to-edge (0 gap, the real webtoon/manhwa reading-app convention); <see cref="ReadingMode.VerticalContinuous"/>/<see cref="ReadingMode.HorizontalContinuousRightToLeft"/>/<see cref="ReadingMode.HorizontalContinuous"/> show a visible gap between pages.</summary>
    private double ContinuousMainAxisGap => ReadingMode == ReadingMode.Webtoon ? 0.0 : ContinuousModeGapPixels;

    private const double ContinuousModeGapPixels = 16;

    /// <summary>
    /// User direction: continuous-family zoom is a bounded 0.5x-4x range (matching the toolbar
    /// slider, ReaderScreen.axaml), not paged mode's fixed 1x-4x - supersedes the original spec's
    /// "unclamped upward" language with an explicit finite range once the user gave a concrete
    /// slider range to match.
    /// </summary>
    private const double ContinuousMinZoom = 0.5;

    private const double ContinuousMaxZoom = 4.0;

    /// <summary>Double-tap zoom target for continuous/webtoon modes (user direction) - a second double-tap at this zoom level returns to 100%, matching <see cref="ZoomPanMath.DoubleClickZoom"/>'s paged-mode toggle shape but a different target level.</summary>
    private const double ContinuousDoubleTapZoom = 2.5;

    private int EffectiveRotationDegrees() =>
        (ZoomPanMath.ComposeRotationDegrees(ManualRotationDegrees, AutoRotate, Page?.PixelSize ?? default) + PageRotationOverrideDegrees) % 360;

    /// <summary>
    /// <see cref="Page"/>'s pixel size, swapped width/height for a 90/270 rotation - the shape
    /// gesture math (pan clamping, zoom-anchor, double-click centering) needs to reason about,
    /// since that's what's actually displayed on screen. See <see cref="ReaderPageVisualHandler"/>
    /// for the matching render-time logic.
    /// </summary>
    private PixelSize EffectivePixelSize()
    {
        var pixelSize = Page?.PixelSize ?? default;
        return EffectiveRotationDegrees() is 90 or 270 ? new PixelSize(pixelSize.Height, pixelSize.Width) : pixelSize;
    }

    /// <summary>
    /// The currently-rendered <see cref="Page"/>'s on-screen bounds, in this control's own
    /// coordinate space - added for the PDF area-capture tool (docs/superpowers/specs/2026-09-01-
    /// books-reader-ergonomics-and-annotations-design.md §"PDF area capture"). A read-only accessor
    /// only, deliberately not touching any existing rendering/gesture code in this large, shared
    /// (also used by the comic reader) control - the underlying fit/pan formula is byte-for-byte the
    /// same one <see cref="ReaderPageVisualHandler"/>'s own <c>ComputeDrawPlan</c> already uses to
    /// actually draw the page (verified against that method's source, not re-derived from scratch),
    /// duplicated here rather than shared via a new cross-file dependency to keep this addition to a
    /// single new method with no risk to the existing render path.
    /// </summary>
    public Rect GetCurrentImageBounds()
    {
        var effectivePixelSize = EffectivePixelSize();
        double scale = ZoomPanMath.ComputeBaseScale(Bounds.Size, effectivePixelSize, FitMode, FitOnlyIfOversized) * ZoomLevel;
        var (panX, panY) = ZoomPanMath.ClampPan(Bounds.Size, effectivePixelSize, ZoomLevel, PanOffsetX, PanOffsetY, FitMode, FitOnlyIfOversized);

        double centerX = Bounds.X + (Bounds.Width / 2) + panX;
        double centerY = Bounds.Y + (Bounds.Height / 2) + panY;
        double width = effectivePixelSize.Width * scale;
        double height = effectivePixelSize.Height * scale;

        return new Rect(centerX - (width / 2), centerY - (height / 2), width, height);
    }

    /// <summary>
    /// Real pan-enable gate (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-
    /// design.md follow-up fix) - <c>ZoomLevel &gt; MinZoom</c> alone used to be sufficient before
    /// fit modes existed (the only base scale was always contain-within-bounds), but
    /// <see cref="ImageFitMode.Original"/>/<see cref="ImageFitMode.FitWidth"/>/
    /// <see cref="ImageFitMode.FitHeight"/>/<see cref="ImageFitMode.BestFit"/> can all overflow the
    /// canvas at <c>ZoomLevel == MinZoom</c> too - without checking actual overflow, that content
    /// is unreachable, stuck cut off with no way to pan to it (real bug, found via manual testing).
    /// </summary>
    private bool CanPan() => ZoomLevel > ZoomPanMath.MinZoom || ZoomPanMath.HasOverflow(Bounds.Size, EffectivePixelSize(), ZoomLevel, FitMode, FitOnlyIfOversized);

    /// <summary>
    /// Known-or-estimated size of every page - shared by continuous-mode layout
    /// (<see cref="PushContinuousVisualData"/>) and scroll-input clamping so both reason about the
    /// same numbers. Returns a reused buffer (<see cref="_estimatedSizesBuffer"/>), not a fresh
    /// array: a drag scroll calls this several times per pointer-move (via
    /// <see cref="ClampScrollOffset"/> and friends) and every caller consumes it synchronously
    /// through a pure <see cref="ReaderLayoutModel"/> function before the next rebuild, so there's
    /// no aliasing hazard and no reason to allocate a <see cref="PageCount"/>-sized array each time.
    /// </summary>
    private Size[] EstimatedPageSizes()
    {
        if (_estimatedSizesBuffer.Length != PageCount)
        {
            _estimatedSizesBuffer = new Size[PageCount];
        }

        for (int i = 0; i < PageCount; i++)
        {
            _estimatedSizesBuffer[i] = _knownPageSizes.TryGetValue(i, out var known) ? known : DefaultEstimatedPageSize;
        }

        return _estimatedSizesBuffer;
    }

    /// <summary>
    /// Scrolls <paramref name="pageIndex"/>'s top/leading edge into view (spec §6: "in continuous
    /// mode they instead scroll the target page's top edge into view") - the continuous-mode
    /// counterpart to paged mode's index jump, driven by <see cref="ViewModels.ReaderScreenViewModel.ScrollToPageRequested"/>
    /// via <see cref="ReaderScreen"/>'s code-behind (the ViewModel itself has no page-size
    /// knowledge, that's this control's <see cref="_knownPageSizes"/>/estimate). No-op outside
    /// continuous mode or before layout has real bounds - matches every other scroll-input path's
    /// bounds guard.
    /// </summary>
    public void ScrollToPage(int pageIndex)
    {
        if (!IsContinuous || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double target = ReaderLayoutModel.ComputeStackOffsetOfPage(EstimatedPageSizes(), pageIndex, Bounds.Size, ContinuousAxis, ZoomLevel, ContinuousMainAxisGap);
        ScrollOffset = ClampScrollOffset(target);
    }

    /// <summary>
    /// Applies a continuous-mode wheel-scroll delta, tracking chapter-boundary overscroll pull
    /// (docs/superpowers/specs/2026-08-23-reader-chapter-transition-design.md) alongside the
    /// ordinary clamped scroll. Pinned at a boundary and still pushing further past it accumulates
    /// <see cref="_overscrollPull"/> (and bumps the visual render via <see cref="PushContinuousVisualData"/>,
    /// since setting <see cref="ScrollOffset"/> to the same already-clamped value wouldn't itself
    /// raise a property-changed re-render); scrolling back away from a boundary resets the pull.
    /// </summary>
    private void HandleContinuousWheelScroll(double wheelDeltaY)
    {
        double proposed = ScrollOffset - (wheelDeltaY * WheelPanStep * WheelScrollStepPixels);
        double clamped = ClampScrollOffset(proposed);

        if (clamped != ScrollOffset)
        {
            ScrollOffset = clamped;
            if (_overscrollPull != 0)
            {
                _overscrollPull = 0;
                PushContinuousVisualData();
            }

            return;
        }

        double overshoot = proposed - clamped;
        _overscrollPull += overshoot;

        if (Math.Abs(_overscrollPull) >= OverscrollThresholdPixels)
        {
            bool forward = _overscrollPull > 0;
            _overscrollPull = 0;
            PushContinuousVisualData();
            TryExecuteWithParameter(ChapterBoundaryOverscrollCommand, forward);
            return;
        }

        PushContinuousVisualData();
    }

    /// <summary>Clamps scroll position to <c>[0, total stack size - viewport main-axis size]</c>, so dragging/scrolling past either end just stops rather than running away into empty space.</summary>
    private double ClampScrollOffset(double proposed)
    {
        if (PageCount <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return 0;
        }

        double viewportMainSize = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? Bounds.Height : Bounds.Width;
        double total = ReaderLayoutModel.ComputeTotalMainAxisSize(EstimatedPageSizes(), Bounds.Size, ContinuousAxis, ZoomLevel, ContinuousMainAxisGap);
        double maxScroll = Math.Max(0, total - viewportMainSize);
        return Math.Clamp(proposed, 0, maxScroll);
    }

    /// <summary>Same shape as <see cref="ZoomPanMath.ClampPan"/>'s single-axis overflow clamp, for continuous mode's cross-axis pan (only relevant once <see cref="ZoomLevel"/> makes the zoomed cross-axis size exceed the viewport).</summary>
    private double ClampContinuousCrossAxisPan(double proposed)
    {
        double viewportCrossSize = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? Bounds.Width : Bounds.Height;
        double crossAxisSize = viewportCrossSize * ZoomLevel;
        double maxPan = Math.Max(0, (crossAxisSize - viewportCrossSize) / 2);
        return Math.Clamp(proposed, -maxPan, maxPan);
    }

    /// <summary>
    /// Real bug, found via manual testing after the composition-visual rewrite (spec §4): a
    /// composition child visual attached via <c>ElementComposition.SetElementChildVisual</c>
    /// renders pixels but does NOT by itself make this control hit-testable - Avalonia's pointer
    /// hit-testing for a plain <see cref="Control"/> needs actual classic-rendered content
    /// (<see cref="DrawingContext"/> draw calls) to establish a hit-test region, and this control no
    /// longer has any once all drawing moved to <see cref="ReaderPageVisualHandler"/>. Without this,
    /// <see cref="OnPointerPressed"/>/<see cref="OnPointerWheelChanged"/>/<see cref="OnKeyDown"/>
    /// never fire at all - confirmed via temporary diagnostic logging, not guessed at. A fully
    /// transparent fill is enough to establish the region; the actual page content is still drawn
    /// entirely by the composition visual, this draws nothing visible.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    /// <summary>
    /// Creates and attaches the <see cref="ReaderPageVisualHandler"/>-backed composition visual as
    /// this control's child visual (the standard Avalonia composition attachment pattern - confirmed
    /// via reflection against the actual Avalonia 12.1.1 assembly this project targets, not
    /// assumed: <c>ElementComposition.GetElementVisual</c> → <c>Compositor.CreateCustomVisual</c> →
    /// <c>ElementComposition.SetElementChildVisual</c>). <see cref="OnAttachedToVisualTree"/> can
    /// fire more than once if this control leaves and re-enters the visual tree (the rail-nav screen
    /// switcher toggles visibility rather than destroying screens - docs/superpowers/specs/
    /// 2026-08-06-reader-canvas-alpha-design.md's precedent), so this re-creates the visual fresh
    /// each time rather than assuming it only ever runs once.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var elementVisual = ElementComposition.GetElementVisual(this);
        if (elementVisual is null)
        {
            return;
        }

        _visual = elementVisual.Compositor.CreateCustomVisual(new ReaderPageVisualHandler());
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        _visual.ClipToBounds = true;

        // Re-arm the background-decode subscription: the screen switcher toggles visibility (detach/
        // re-attach) without a DecoderProperty change, so OnPropertyChanged wouldn't re-subscribe
        // after OnDetachedFromVisualTree dropped it. Idempotent (-= then +=).
        if (Decoder is PageDecodeService decodeService)
        {
            decodeService.BackgroundDecodeCompleted -= OnBackgroundDecodeCompleted;
            decodeService.BackgroundDecodeCompleted += OnBackgroundDecodeCompleted;
        }

        PushRenderData();
        PushAdjustmentData();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;

        if (Decoder is PageDecodeService decodeService)
        {
            decodeService.BackgroundDecodeCompleted -= OnBackgroundDecodeCompleted;
        }
    }

    /// <summary>
    /// A background page decode landed (fires on the decode thread). Hop to the UI thread and fold
    /// it into the next frame's coalesced push - a burst of completions collapses to one re-push.
    /// </summary>
    private void OnBackgroundDecodeCompleted(int pageIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsContinuous)
            {
                RequestContinuousPush();
            }
        });
    }

    /// <summary>
    /// Composition visuals have no equivalent of <c>AffectsRender</c>'s automatic "this property
    /// changing means redraw" wiring, so every property in <see cref="RenderAffectingProperties"/> -
    /// plus <see cref="BoundsProperty"/>, which isn't itself a render-affecting *content* property
    /// but does need the visual's <see cref="CompositionVisual.Size"/> kept in sync on resize -
    /// pushes a fresh visual-data message here.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Split-page part navigation (docs/superpowers/specs/2026-09-05-reader-polish-backlog-
        // finish-design.md §1) - recomputed unconditionally, ahead of every early-return branch
        // below, so the "Part X/Y" label stays live for every relevant change (page, zoom, pan, fit,
        // bounds) regardless of which branch a given property change falls into. Cheap no-op for any
        // unrelated property change - UpdatePartLabel itself no-ops in continuous mode or with no
        // page loaded.
        UpdatePartLabel();

        if (change.Property == DecoderProperty)
        {
            // Move the background-decode-completed subscription to the new decoder. Continuous mode
            // draws not-yet-decoded pages as gaps (see PushContinuousVisualData) and relies on this
            // to re-push once the background loop fills one in.
            if (change.OldValue is PageDecodeService oldDecodeService)
            {
                oldDecodeService.BackgroundDecodeCompleted -= OnBackgroundDecodeCompleted;
            }

            if (change.NewValue is PageDecodeService newDecodeService)
            {
                newDecodeService.BackgroundDecodeCompleted += OnBackgroundDecodeCompleted;
            }

            // A new issue was opened (or continuous mode's decoder was swapped) - yesterday's page
            // sizes don't apply to today's book.
            _knownPageSizes.Clear();

            // A pending direction here would mean crossing an issue boundary via
            // NavigateToAdjacentIssue set one right before this decoder swap - clearing it means that
            // transition never animates as if it were a same-book turn (spec §3.1).
            _pendingTransitionDirection = null;

            // Same rationale, for the double-page-spread trigger mechanism (2026-08-15 spec) - a new
            // issue's first page shouldn't transition from the previous issue's last-shown page/pair.
            _lastRenderedPage = null;
            _lastRenderedSecondaryPage = null;

            // A new issue just loaded (e.g. via the chapter-boundary overscroll this pull itself
            // triggered) - yesterday's pull has no meaning against today's book's own boundaries.
            _overscrollPull = 0;

            // Likewise a half-completed touchpad swipe from the previous book shouldn't finish
            // against the new one.
            _wheelPageTurns.Reset();
        }

        // Real page-turn animation trigger (spec §3.1/§3.3): only PageProperty changes in paged mode
        // are candidates - continuous mode never touches Page at all. A transition is only built when
        // ExecuteDirectional left a pending direction (adjacent nav, not a thumbnail-click jump), the
        // style isn't None, and the rapid-paging throttle allows it; otherwise this falls through to
        // the ordinary instant-swap push below, same as every other RenderAffectingProperties change.
        if (change.Property == PageProperty && !IsContinuous)
        {
            var transition = TryBuildPageTransition();
            _pendingTransitionDirection = null;

            if (transition is not null)
            {
                _lastTransitionStartUtc = DateTime.UtcNow;
                // Bookkeeping stays correct for the next turn even though the compositor is still
                // animating toward these values - PushPagedVisualData won't run for this change since
                // we return below, so nothing else updates them.
                _lastRenderedPage = Page;
                _lastRenderedSecondaryPage = SecondaryPage;
                _visual?.SendHandlerMessage(transition);
                return;
            }
        }

        // Real bug, found via manual testing: OnPointerWheelChanged's Ctrl+wheel zoom handler
        // explicitly re-clamps ScrollOffset/cross-axis pan against the new zoom level after changing
        // it (so a smaller zoom can't leave them pointing past the now-shrunk stack) - but that's the
        // only path that did. ZoomLevel is TwoWay-bound directly to the toolbar's zoom slider
        // (ReaderScreen.axaml), which sets it via the property system with no re-clamp at all; a
        // stale ScrollOffset/PanOffsetX/Y left over from a higher zoom level (or just never touched
        // since Load) can end up pointing entirely past the shrunk-down content once zoom drops
        // enough - PushContinuousVisualData's viewport-intersection test then finds no pages at all,
        // and the page appears to vanish, leaving only the canvas background. Centralizing the
        // re-clamp here (rather than duplicating it in every gesture handler that can change
        // ZoomLevel: wheel, pinch, double-tap, and now the slider) closes the gap for all of them at
        // once, not just whichever one happened to remember to call it.
        if (change.Property == ZoomLevelProperty && IsContinuous)
        {
            ScrollOffset = ClampScrollOffset(ScrollOffset);
            PanOffsetX = ClampContinuousCrossAxisPan(PanOffsetX);
            PanOffsetY = ClampContinuousCrossAxisPan(PanOffsetY);
        }

        // Reclamp on ScrollOffset itself too (docs/superpowers/specs/2026-08-16-reader-auto-scroll-
        // design.md) - lets ReaderScreenViewModel's auto-scroll timer propose an optimistic
        // (possibly-past-max) increment each tick without duplicating this control's page-size math;
        // the clamped result round-trips back through the TwoWay binding synchronously, so the
        // ViewModel can tell whether it actually moved by comparing before/after. Safe for every
        // existing ScrollOffset writer too (drag/wheel/pinch/Home/End/ScrollToPage all already
        // pre-clamp before assigning) - reclamping an already-clamped value is a no-op (the `!=`
        // guard below skips the reassignment entirely, unlike the ZoomLevel-triggered reclamp above
        // which unconditionally reassigns). Real bug, found via manual testing: an earlier version
        // of this block reassigned unconditionally with no early return - when the clamp *did*
        // correct something (routine during mode entry, when ScrollOffset is still settling against
        // just-swapped Decoder/PageCount), the nested SetValue recursion already pushes fresh visual
        // data with the corrected value, but execution then fell through to this same method's
        // generic RenderAffectingProperties push at the bottom *again* for the stale outer change -
        // a double PushContinuousVisualData call in immediate succession, landing squarely in this
        // control's own documented fragile spot (see OnKeyDown's continuous-mode comment on stale
        // disposed bitmaps from PageDecodeService's virtualization window) and crashing continuous
        // mode on entry. The early return below - only taken when a correction actually happened -
        // matches the explicit-return shape <see cref="BoundsProperty"/>'s block already uses just
        // below, and leaves the common (already-clamped) case falling through unchanged, exactly as
        // it did before this block existed.
        if (change.Property == ScrollOffsetProperty && IsContinuous)
        {
            double clamped = ClampScrollOffset(ScrollOffset);
            if (clamped != ScrollOffset)
            {
                ScrollOffset = clamped;
                return;
            }
        }

        if (change.Property == BoundsProperty && _visual is not null)
        {
            _visual.Size = new Vector(Bounds.Width, Bounds.Height);
            PushRenderData();
            return;
        }

        if (Array.IndexOf(RenderAffectingProperties, change.Property) >= 0)
        {
            // Scroll/pan change on every pointer-move during a drag - coalesce those to one push
            // per frame (see RequestContinuousPush). Every other render-affecting property (page,
            // zoom, fit mode, reading mode, decoder swap...) is low-frequency and pushes
            // synchronously, so mode entry / page load paints on the same frame with no blank gap.
            bool isContinuousScrollOrPan = IsContinuous && (change.Property == ScrollOffsetProperty
                || change.Property == PanOffsetXProperty
                || change.Property == PanOffsetYProperty);

            if (isContinuousScrollOrPan)
            {
                RequestContinuousPush();
            }
            else
            {
                PushRenderData();
            }
        }

        if (Array.IndexOf(AdjustmentProperties, change.Property) >= 0)
        {
            PushAdjustmentData();
        }
    }

    private void PushRenderData()
    {
        if (IsContinuous)
        {
            PushContinuousVisualData();
        }
        else
        {
            PushPagedVisualData();
        }
    }

    /// <summary>
    /// Coalesces continuous-mode scroll/pan render pushes to at most one per animation frame. A
    /// trackpad or click-drag scroll writes <see cref="ScrollOffset"/> (and usually
    /// <see cref="PanOffsetX"/>/<see cref="PanOffsetY"/> as well, for any cross-axis wobble) on every
    /// pointer-move event - 60-125+ a second - and each write used to run a full
    /// <see cref="PushContinuousVisualData"/> synchronously: a <see cref="PageCount"/>-sized layout
    /// sweep and a decoder pass, often twice per event. Most of those rebuilds landed between two
    /// compose passes and were never shown. Deferring to <see cref="TopLevel.RequestAnimationFrame"/>
    /// collapses them to one push per frame with no added latency - the content still updates every
    /// frame, it just stops rebuilding two or three times per frame. Only scroll/pan goes through
    /// here; every other render-affecting change still pushes synchronously.
    /// </summary>
    private void RequestContinuousPush()
    {
        if (_continuousPushQueued)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            // Not in a window yet (or already detached) - nothing is compositing this visual, so
            // there's no frame to defer to; push directly.
            PushContinuousVisualData();
            return;
        }

        _continuousPushQueued = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _continuousPushQueued = false;
            if (IsContinuous)
            {
                PushContinuousVisualData();
            }
        });
    }

    private void PushAdjustmentData() =>
        _visual?.SendHandlerMessage(new AdjustmentVisualData(Brightness, Contrast, Saturation, Gamma));

    /// <summary>
    /// Synthesizes a Crossfade transition for a double-page layout-mode or reading-direction change
    /// (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §6), reusing the exact
    /// same <see cref="ReaderPageTransitionData"/> pipeline a page-turn uses - built here rather than
    /// in <see cref="ReaderScreenViewModel"/>, which has no <see cref="Bounds"/>/geometry knowledge of
    /// its own. Always Crossfade regardless of <see cref="PageTransitionStyle"/> (a layout/direction
    /// change has no natural slide edge) - still snaps instantly when the user's style is
    /// <see cref="Paperbunkr.Data.Entities.PageTransitionStyle.None"/>, same as every other transition.
    /// <paramref name="oldPrimary"/>/<paramref name="oldSecondary"/>/<paramref name="oldIsRightToLeft"/>
    /// are supplied by the caller rather than read from <see cref="_lastRenderedPage"/>/
    /// <see cref="_lastRenderedSecondaryPage"/> - by the time this runs, the ViewModel's own property
    /// changes (<see cref="Page"/>/<see cref="SecondaryPage"/>) have already fired and pushed an
    /// ordinary instant re-render through <see cref="OnPropertyChanged(AvaloniaPropertyChangedEventArgs)"/>'s
    /// normal <see cref="PageProperty"/> path (no pending direction, so <see cref="TryBuildPageTransition"/>
    /// itself declines), clobbering that bookkeeping to the *new* values already. Named simplification:
    /// this means one already-composited instant frame briefly shows the new state before the
    /// crossfade sent here starts it back from the old one - a single-frame flash, not chased further
    /// given the added cross-layer plumbing (an ordinary-push-suppression signal reaching PageCanvas
    /// before the ViewModel mutation even happens) it'd take to fully avoid.
    /// </summary>
    public void PlayReflowTransition(Bitmap? oldPrimary, Bitmap? oldSecondary, bool oldIsRightToLeft)
    {
        if (PageTransitionStyle == PageTransitionStyle.None || oldPrimary is null || Page is null)
        {
            return;
        }

        bool throttleCleared = _lastTransitionStartUtc is null
            || (DateTime.UtcNow - _lastTransitionStartUtc.Value).TotalMilliseconds >= PageTransitionDurationMs;
        if (!throttleCleared)
        {
            return;
        }

        bool isRightToLeft = ReadingMode == ReadingMode.RightToLeft;
        var transition = new ReaderPageTransitionData(
            new Rect(Bounds.Size), oldPrimary, Page, HighQualityDisplay, ZoomLevel * PageMarginMultiplier, PanOffsetX, PanOffsetY,
            FitMode, FitOnlyIfOversized, EffectiveRotationDegrees(), PageTransitionStyle.Crossfade, TimeSpan.FromMilliseconds(PageTransitionDurationMs),
            PageTransitionDirection.Right, oldSecondary, SecondaryPage, isRightToLeft, oldIsRightToLeft);

        _lastTransitionStartUtc = DateTime.UtcNow;
        _lastRenderedPage = Page;
        _lastRenderedSecondaryPage = SecondaryPage;
        _visual?.SendHandlerMessage(transition);
    }

    /// <summary>
    /// Builds an animated page-turn message for the <see cref="PageProperty"/> change just observed,
    /// or <see langword="null"/> if this turn shouldn't animate (spec §3.1/§3.3): no pending direction
    /// (a jump, not adjacent nav), <see cref="PageTransitionStyle"/> is <see cref="Paperbunkr.Data.Entities.PageTransitionStyle.None"/>,
    /// the rapid-paging throttle hasn't cleared yet, or there's no outgoing bitmap to transition from
    /// (e.g. the very first page of a freshly opened book, tracked via <see cref="_lastRenderedPage"/>
    /// - see that field's own doc comment for why this no longer reads <c>change.OldValue</c>). Both
    /// bitmaps render against the current (already-updated) zoom/pan/fit/rotation state - see
    /// <see cref="ReaderPageTransitionData"/>'s own doc comment for why that's not an attempt to
    /// preserve either page's historical transform. Carries <see cref="SecondaryPage"/> on both sides
    /// too (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §5) - a turn between
    /// solo and paired animates just like a solo-to-solo one, each side's plan(s) built against that
    /// side's own bitmap count downstream in <see cref="ReaderPageVisualHandler"/>.
    /// </summary>
    private ReaderPageTransitionData? TryBuildPageTransition()
    {
        if (_pendingTransitionDirection is not { } direction || PageTransitionStyle == PageTransitionStyle.None)
        {
            return null;
        }

        bool throttleCleared = _lastTransitionStartUtc is null
            || (DateTime.UtcNow - _lastTransitionStartUtc.Value).TotalMilliseconds >= PageTransitionDurationMs;
        if (!throttleCleared)
        {
            return null;
        }

        if (_lastRenderedPage is not { } oldBitmap)
        {
            return null;
        }

        bool isRightToLeft = ReadingMode == ReadingMode.RightToLeft;
        return new ReaderPageTransitionData(
            new Rect(Bounds.Size), oldBitmap, Page, HighQualityDisplay, ZoomLevel * PageMarginMultiplier, PanOffsetX, PanOffsetY,
            FitMode, FitOnlyIfOversized, EffectiveRotationDegrees(), PageTransitionStyle, TimeSpan.FromMilliseconds(PageTransitionDurationMs), direction,
            _lastRenderedSecondaryPage, SecondaryPage, isRightToLeft, OldIsRightToLeft: isRightToLeft);
    }

    private void PushPagedVisualData()
    {
        _visual?.SendHandlerMessage(new ReaderPageVisualData(
            new Rect(Bounds.Size), Page, HighQualityDisplay, ZoomLevel * PageMarginMultiplier, PanOffsetX, PanOffsetY,
            FitMode, FitOnlyIfOversized, EffectiveRotationDegrees(), SecondaryPage, ReadingMode == ReadingMode.RightToLeft));

        _lastRenderedPage = Page;
        _lastRenderedSecondaryPage = SecondaryPage;
    }

    /// <summary>
    /// Builds the layout window (<see cref="ReaderLayoutModel.ComputeContinuousLayout"/>), decodes
    /// whatever's newly needed (<see cref="IPageImageDecoder.GetPage"/> - synchronous, correctness-
    /// guaranteed on a cache miss per <see cref="PageDecodeService"/>'s contract regardless of
    /// whether its background prefetch has caught up), and pushes the resulting page list to the
    /// handler. Also drives <see cref="PageDecodeService.SetVirtualizationWindow"/> when the active
    /// decoder is one (paged-mode's <see cref="PageImageDecoder"/> has no such concept).
    /// </summary>
    private void PushContinuousVisualData()
    {
        if (_visual is null || Decoder is null || PageCount <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _visual?.SendHandlerMessage(new ReaderContinuousVisualData(Bounds.Size, Array.Empty<ContinuousPageEntry>(), HighQualityDisplay));
            return;
        }

        var estimatedSizes = EstimatedPageSizes();

        double crossAxisPan = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? PanOffsetX : PanOffsetY;

        // Chapter-boundary overscroll bump (docs/superpowers/specs/2026-08-23-reader-chapter-
        // transition-design.md) - a purely visual offset layered on top of the real (already-clamped)
        // ScrollOffset, never written back to it, so the rubber-band effect can't itself push the
        // reader's actual position past a real page.
        double bumpMagnitude = ReaderLayoutModel.ComputeOverscrollBump(Math.Abs(_overscrollPull), OverscrollMaxBumpPixels);
        double effectiveScrollOffset = ScrollOffset + (Math.Sign(_overscrollPull) * bumpMagnitude);

        var layout = ReaderLayoutModel.ComputeContinuousLayout(estimatedSizes, effectiveScrollOffset, Bounds.Size, ContinuousAxis,
            zoom: ZoomLevel * PageMarginMultiplier, crossAxisPanOffset: crossAxisPan, mainAxisGap: ContinuousMainAxisGap, reverseMainAxis: IsContinuousReversed);

        if (layout.Count == 0)
        {
            _visual.SendHandlerMessage(new ReaderContinuousVisualData(Bounds.Size, Array.Empty<ContinuousPageEntry>(), HighQualityDisplay));
            return;
        }

        // Position tracking (spec §6) - recomputed every pass off the same rects just built, not a
        // separate pass over the decoder/layout model.
        int nearest = ReaderLayoutModel.NearestPageToViewportCenter(layout, Bounds.Size, ContinuousAxis);
        if (nearest >= 0)
        {
            CurrentContinuousPageIndex = nearest;
        }

        var decodeService = Decoder as PageDecodeService;
        if (decodeService is not null)
        {
            int viewportCrossSize = (int)(ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? Bounds.Width : Bounds.Height);
            decodeService.SetViewportWidth(Math.Max(1, viewportCrossSize));
            decodeService.SetVirtualizationWindow(layout[0].Index, layout[^1].Index);
        }

        // PageDecodeService decodes on a background loop; peek its cache without blocking rather than
        // calling GetPage, which does a full synchronous decode on a cache miss - on the UI thread,
        // inside this per-frame push. That synchronous decode right as a fresh page scrolled into
        // view was the "choppy going page to page" hitch. A page the loop hasn't reached yet is
        // drawn as a gap for a frame or two; BackgroundDecodeCompleted (wired in OnPropertyChanged)
        // re-pushes the moment it lands. SetVirtualizationWindow above already enqueued the whole
        // visible range at high priority, so the decode is already in flight.
        //
        // Exception: on the first push for a freshly-opened decoder the cache is empty and every
        // page would draw as a gap, so decode just the centre page synchronously - the reader opens
        // on content, not a blank frame. Every later push (cache non-empty) is pure non-blocking;
        // mid-scroll the cache is never empty, so this never brings the per-boundary hitch back.
        bool primeCentrePage = decodeService is not null && decodeService.DecodedPageCount == 0;

        var entries = new List<ContinuousPageEntry>(layout.Count);
        foreach (var page in layout)
        {
            Bitmap? bitmap;
            try
            {
                bool blockDecode = decodeService is null || (primeCentrePage && page.Index == nearest);
                bitmap = blockDecode ? Decoder.GetPage(page.Index) : decodeService!.TryGetCachedPage(page.Index);
                if (bitmap is not null)
                {
                    _knownPageSizes[page.Index] = new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                }
            }
            catch
            {
                bitmap = null; // one bad page doesn't break the rest - matches IPageImageDecoder's existing contract
            }

            entries.Add(new ContinuousPageEntry(page.Rect, bitmap));
        }

        _visual.SendHandlerMessage(new ReaderContinuousVisualData(Bounds.Size, entries, HighQualityDisplay));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (IsContinuous)
        {
            // Drag always scrolls in continuous mode - there's no "CanPan" gate the way paged mode
            // has, since there's always more stack to reveal (spec §5).
            _isDragging = true;
            _dragStartPointer = e.GetPosition(this);
            _dragStartScrollOffset = ScrollOffset;
            _dragStartPanX = PanOffsetX;
            _dragStartPanY = PanOffsetY;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleZoom(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        bool isTouch = e.Pointer.Type == PointerType.Touch;
        if (isTouch)
        {
            _touchPressPosition = e.GetPosition(this);
            _touchPressTime = DateTime.UtcNow;
        }

        if (CanPan())
        {
            _isDragging = true;
            _dragStartPointer = e.GetPosition(this);
            _dragStartPanX = PanOffsetX;
            _dragStartPanY = PanOffsetY;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (isTouch)
        {
            InvokeTouchZone(e.GetPosition(this));
        }
        else
        {
            InvokeZoneCommand(e.GetPosition(this));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging)
        {
            return;
        }

        var p = e.GetPosition(this);

        if (IsContinuous)
        {
            double mainDelta = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? p.Y - _dragStartPointer.Y : p.X - _dragStartPointer.X;
            double crossDelta = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? p.X - _dragStartPointer.X : p.Y - _dragStartPointer.Y;

            // Content follows the cursor (touch-scroll feel) - dragging down/right reveals earlier
            // content, so scroll offset decreases as the drag delta increases.
            ScrollOffset = ClampScrollOffset(_dragStartScrollOffset - mainDelta);

            if (ContinuousAxis == ReaderLayoutModel.Axis.Vertical)
            {
                PanOffsetX = ClampContinuousCrossAxisPan(_dragStartPanX + crossDelta);
            }
            else
            {
                PanOffsetY = ClampContinuousCrossAxisPan(_dragStartPanY + crossDelta);
            }

            e.Handled = true;
            return;
        }

        var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, EffectivePixelSize(), ZoomLevel,
            _dragStartPanX + (p.X - _dragStartPointer.X), _dragStartPanY + (p.Y - _dragStartPointer.Y), FitMode, FitOnlyIfOversized);
        PanOffsetX = x;
        PanOffsetY = y;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            e.Pointer.Capture(null);
        }

        _isDragging = false;

        if (e.Pointer.Type == PointerType.Touch && _touchPressPosition is { } start && !CanPan())
        {
            var end = e.GetPosition(this);
            var elapsed = DateTime.UtcNow - _touchPressTime;
            if (elapsed.TotalMilliseconds <= MaxFlickDurationMs
                && PageTurnGestureMath.ResolveFlick(end - start, IsPagedVertical, MinFlickDistance) is { } forward)
            {
                ExecuteTurn(forward);
                e.Handled = true;
            }
        }

        _touchPressPosition = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (IsContinuous)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Free/unclamped upward per spec §5 - no cursor-anchor math for continuous mode this
                // pass (a named simplification: cursor-anchored zoom needs per-page anchor tracking
                // that doesn't exist yet here). Scroll/cross-axis pan re-clamping against the new zoom
                // now happens centrally in OnPropertyChanged whenever ZoomLevel changes, not just here.
                ZoomLevel = ZoomPanMath.ClampZoom(ZoomLevel + (e.Delta.Y * WheelZoomStep), ContinuousMaxZoom, ContinuousMinZoom);
                e.Handled = true;
                return;
            }

            // Plain wheel/touch = document scroll (spec §5), not page-turn - there's no page-turn
            // concept in continuous mode. Scrolling "down" (negative Delta.Y) moves further into the
            // stack, matching typical scroll-reader convention. WheelPanStep is the same
            // AppSettings.MouseWheelSpeed-driven multiplier paged mode's plain-wheel pan already
            // uses; WheelScrollStepPixels gives it the same base-magnitude role KeyPanStep plays for
            // arrow keys below.
            HandleContinuousWheelScroll(e.Delta.Y);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double newZoom = ZoomPanMath.ClampZoom(ZoomLevel + (e.Delta.Y * WheelZoomStep));
            var cursor = e.GetPosition(this);
            var (x, y) = ZoomPanMath.PanToKeepPointFixed(Bounds.Size, EffectivePixelSize(),
                ZoomLevel, new Point(PanOffsetX, PanOffsetY), cursor, newZoom, FitMode, FitOnlyIfOversized);
            ZoomLevel = newZoom;
            PanOffsetX = x;
            PanOffsetY = y;
            e.Handled = true;
            return;
        }

        if (CanPan())
        {
            var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, EffectivePixelSize(), ZoomLevel,
                PanOffsetX - (e.Delta.X * WheelPanStep), PanOffsetY + (e.Delta.Y * WheelPanStep), FitMode, FitOnlyIfOversized);
            PanOffsetX = x;
            PanOffsetY = y;
            _wheelPageTurns.Reset();
        }
        else
        {
            // Debounced so one precision-touchpad swipe (a stream of sub-detent deltas) turns ~one
            // page, not a dozen - a real mouse-wheel detent still passes straight through. See
            // WheelPageTurnAccumulator.
            int turn = _wheelPageTurns.Accumulate(WheelPageTurnAccumulator.ForwardScalar(e.Delta.X, e.Delta.Y));
            if (turn > 0)
            {
                ExecuteTurn(forward: true);
            }
            else if (turn < 0)
            {
                ExecuteTurn(forward: false);
            }
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Always-context commands (docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-
        // design.md §2/§3): checked first, ahead of the paged/continuous split below, since they
        // apply regardless of mode - same precedence fullscreen already had, extended to
        // rotate/zoom/fit. F11 stays a hardcoded secondary fullscreen trigger alongside the
        // remappable FullscreenToggleGesture (an OS-level convention, not a real "shortcut").
        if (e.Key == Key.F11 || FullscreenToggleGesture.Matches(e))
        {
            if (TryExecute(FullscreenToggleCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (RotateClockwiseGesture.Matches(e))
        {
            if (TryExecute(RotateClockwiseCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (RotateCounterClockwiseGesture.Matches(e))
        {
            if (TryExecute(RotateCounterClockwiseCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (PreviousBookmarkGesture.Matches(e))
        {
            if (TryExecute(PreviousBookmarkCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (NextBookmarkGesture.Matches(e))
        {
            if (TryExecute(NextBookmarkCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (ZoomInGesture.Matches(e))
        {
            if (TryExecute(ZoomInCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (ZoomOutGesture.Matches(e))
        {
            if (TryExecute(ZoomOutCommand))
            {
                e.Handled = true;
            }

            return;
        }

        if (TryMatchFitGesture(e, out var fitMode))
        {
            if (SetFitModeCommand?.CanExecute(fitMode) == true)
            {
                SetFitModeCommand.Execute(fitMode);
                e.Handled = true;
            }

            return;
        }

        if (IsContinuous)
        {
            if (ToggleAutoScrollGesture.Matches(e))
            {
                if (TryExecute(ToggleAutoScrollCommand))
                {
                    e.Handled = true;
                }

                return;
            }

            if (ScrollToStartGesture.Matches(e))
            {
                ScrollOffset = 0;
                e.Handled = true;
                return;
            }

            if (ScrollToEndGesture.Matches(e))
            {
                ScrollOffset = ClampScrollOffset(double.MaxValue);
                e.Handled = true;
                return;
            }

            if (TryGetContinuousScrollDelta(e, out double scrollDelta))
            {
                ScrollOffset = ClampScrollOffset(ScrollOffset + scrollDelta);
                e.Handled = true;
                return;
            }

            // Real bug, found via manual testing: falling through to the paged-mode code below
            // touches Page/CanPan()/EffectivePixelSize() unconditionally, but Page can be a stale
            // reference to a bitmap PageDecodeService's virtualization window has already disposed
            // once continuous mode has scrolled away from it (Page still tracks CurrentPage, a
            // paged-mode concept the continuous render path doesn't otherwise touch) - a real crash
            // (ObjectDisposedException), not a theoretical one. No paged-mode fallback for any key
            // continuous mode doesn't specifically handle above.
            return;
        }

        if (CanPan() && TryGetArrowPanDelta(e, out double dx, out double dy))
        {
            var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, EffectivePixelSize(), ZoomLevel, PanOffsetX + dx, PanOffsetY + dy, FitMode, FitOnlyIfOversized);
            PanOffsetX = x;
            PanOffsetY = y;
            e.Handled = true;
            return;
        }

        // Vertical paged mode (docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-
        // design.md §3): bare Up/Down turn the page. Additive - the remappable LeftKey/RightKey
        // gestures below still fire too, routed through ExecuteTurn so they also animate vertically.
        if (IsPagedVertical && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.Up && ExecuteTurn(forward: false))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && ExecuteTurn(forward: true))
            {
                e.Handled = true;
                return;
            }
        }

        if (LeftKey.Matches(e) && ExecuteTurn(forward: false))
        {
            e.Handled = true;
        }
        else if (RightKey.Matches(e) && ExecuteTurn(forward: true))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Fit-mode gesture-to-<see cref="ImageFitMode"/> mapping (docs/superpowers/specs/
    /// 2026-08-16-remappable-reader-shortcuts-design.md §1) - note FitAllGesture maps to
    /// <see cref="ImageFitMode.Fit"/> and FitBestGesture maps to <see cref="ImageFitMode.BestFit"/>,
    /// matching the existing fit-mode toolbar flyout's own labels ("Fit All"/"Best Fit") rather than
    /// the command names themselves.
    /// </summary>
    private bool TryMatchFitGesture(KeyEventArgs e, out ImageFitMode mode)
    {
        if (FitOriginalGesture.Matches(e)) { mode = ImageFitMode.Original; return true; }
        if (FitAllGesture.Matches(e)) { mode = ImageFitMode.Fit; return true; }
        if (FitWidthGesture.Matches(e)) { mode = ImageFitMode.FitWidth; return true; }
        if (FitHeightGesture.Matches(e)) { mode = ImageFitMode.FitHeight; return true; }
        if (FitBestGesture.Matches(e)) { mode = ImageFitMode.BestFit; return true; }
        mode = default;
        return false;
    }

    /// <summary>
    /// Fires continuously through a two-finger touch gesture. <see cref="PinchEventArgs.Scale"/>
    /// drives zoom (relative to the zoom level when the gesture started, not an absolute value -
    /// <c>Scale</c> is itself relative to gesture-start). <see cref="PinchEventArgs.ScaleOrigin"/>'s
    /// own movement (present even during a nearly-1.0-scale gesture, i.e. a two-finger drag rather
    /// than a pinch) drives continuous mode's scroll/cross-pan - the same two-finger-drag-for-
    /// navigation gesture paged mode resolves in <see cref="OnPinchEnded"/> instead, since a page
    /// turn is a discrete action taken once the gesture ends, not something to animate mid-gesture.
    /// </summary>
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (!_pinchActive)
        {
            _pinchActive = true;
            _pinchStartOrigin = e.ScaleOrigin;
            _pinchStartTime = DateTime.UtcNow;
            _pinchStartZoom = ZoomLevel;
            _pinchStartScrollOffset = ScrollOffset;
            _pinchStartPanX = PanOffsetX;
            _pinchStartPanY = PanOffsetY;

            // A second finger joining mid-drag would otherwise leave the one-finger drag handling
            // in OnPointerMoved (started by the first finger's OnPointerPressed) still active
            // alongside this gesture, both writing ScrollOffset/PanOffsetX/Y at once - stop it so
            // the pinch gesture takes over exclusively for the rest of this touch interaction.
            _isDragging = false;
        }

        _pinchLastOrigin = e.ScaleOrigin;
        _pinchLastScale = e.Scale;

        double minZoom = IsContinuous ? ContinuousMinZoom : ZoomPanMath.MinZoom;
        double maxZoom = IsContinuous ? ContinuousMaxZoom : ZoomPanMath.MaxZoom;
        ZoomLevel = ZoomPanMath.ClampZoom(_pinchStartZoom * e.Scale, maxZoom, minZoom);

        if (IsContinuous)
        {
            double originDx = e.ScaleOrigin.X - _pinchStartOrigin.X;
            double originDy = e.ScaleOrigin.Y - _pinchStartOrigin.Y;
            double mainDelta = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? originDy : originDx;
            double crossDelta = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? originDx : originDy;

            ScrollOffset = ClampScrollOffset(_pinchStartScrollOffset - mainDelta);
            if (ContinuousAxis == ReaderLayoutModel.Axis.Vertical)
            {
                PanOffsetX = ClampContinuousCrossAxisPan(_pinchStartPanX + crossDelta);
            }
            else
            {
                PanOffsetY = ClampContinuousCrossAxisPan(_pinchStartPanY + crossDelta);
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// Paged mode's two-finger-drag page-turn (user direction): resolved here, once the gesture
    /// ends, rather than mid-gesture - if the two fingers moved together (scale stayed close to 1,
    /// so this wasn't really a pinch) far and fast enough, same threshold shape as the existing
    /// single-finger flick (<see cref="OnPointerReleased"/>'s <see cref="MinFlickDistance"/>/
    /// <see cref="MaxFlickDurationMs"/>).
    /// </summary>
    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        if (!_pinchActive)
        {
            return;
        }

        if (!IsContinuous)
        {
            var delta = _pinchLastOrigin - _pinchStartOrigin;
            var elapsed = DateTime.UtcNow - _pinchStartTime;
            bool wasPrimarilyADrag = Math.Abs(_pinchLastScale - 1.0) < 0.15;
            if (wasPrimarilyADrag && elapsed.TotalMilliseconds <= MaxFlickDurationMs
                && PageTurnGestureMath.ResolveFlick(delta, IsPagedVertical, MinFlickDistance) is { } forward)
            {
                ExecuteTurn(forward);
            }
        }

        _pinchActive = false;
        e.Handled = true;
    }

    /// <summary>
    /// Continuous/webtoon mode's double-tap zoom (user direction): 250% on the way in, back to 100%
    /// on a second double-tap - a different target than paged mode's existing mouse-double-click
    /// toggle (<see cref="ZoomPanMath.DoubleClickZoom"/>, 200%, handled in <see cref="OnPointerPressed"/>'s
    /// <c>ClickCount == 2</c> branch), which this doesn't touch - paged mode returns immediately here.
    /// </summary>
    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        if (!IsContinuous)
        {
            return;
        }

        ZoomLevel = ZoomLevel > ZoomPanMath.MinZoom ? 1.0 : ContinuousDoubleTapZoom;
        if (ZoomLevel <= ZoomPanMath.MinZoom)
        {
            PanOffsetX = 0;
            PanOffsetY = 0;
        }

        e.Handled = true;
    }

    private void ToggleZoom(Point clickPoint)
    {
        if (ZoomLevel > ZoomPanMath.MinZoom)
        {
            ZoomLevel = ZoomPanMath.MinZoom; // cascade (VM setter) zeroes pan
            return;
        }

        var (x, y) = ZoomPanMath.PanToCenterOn(Bounds.Size, EffectivePixelSize(), ZoomPanMath.DoubleClickZoom, clickPoint, FitMode, FitOnlyIfOversized);
        ZoomLevel = ZoomPanMath.DoubleClickZoom;
        PanOffsetX = x;
        PanOffsetY = y;
    }

    private void InvokeTouchZone(Point p)
    {
        // Left/right thirds (top/bottom in vertical paged mode); middle third toggles chrome
        // visibility (docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-design.md §3) -
        // originally a reserved no-op per the 3-zone tap spec, back when chrome didn't exist yet.
        if (PageTurnGestureMath.ResolveZone(p, Bounds.Size, IsPagedVertical, divisions: 3) is { } forward)
        {
            ExecuteTurn(forward);
        }
        else
        {
            TryExecute(ToggleChromeCommand);
        }
    }

    private void InvokeZoneCommand(Point p)
    {
        if (PageTurnGestureMath.ResolveZone(p, Bounds.Size, IsPagedVertical, divisions: 2) is { } forward)
        {
            ExecuteTurn(forward);
        }
    }

    /// <summary>
    /// Continuous mode's arrow/Page-Up/Page-Down scroll step (spec §5 - Home/End are handled
    /// separately in <see cref="OnKeyDown"/> as absolute jumps, not deltas). The forward-scrolling
    /// key (Down for vertical, Right for horizontal) increases <see cref="ScrollOffset"/>; its
    /// opposite decreases it.
    /// </summary>
    private bool TryGetContinuousScrollDelta(KeyEventArgs e, out double delta)
    {
        double pageJump = (ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? Bounds.Height : Bounds.Width) * PageJumpFraction;
        bool isVertical = ContinuousAxis == ReaderLayoutModel.Axis.Vertical;

        if ((isVertical && ScrollDownGesture.Matches(e)) || (!isVertical && ScrollRightGesture.Matches(e)))
        {
            delta = WheelScrollStepPixels;
            return true;
        }

        if ((isVertical && ScrollUpGesture.Matches(e)) || (!isVertical && ScrollLeftGesture.Matches(e)))
        {
            delta = -WheelScrollStepPixels;
            return true;
        }

        if (ScrollPageDownGesture.Matches(e))
        {
            delta = pageJump;
            return true;
        }

        if (ScrollPageUpGesture.Matches(e))
        {
            delta = -pageJump;
            return true;
        }

        delta = 0;
        return false;
    }

    private bool TryGetArrowPanDelta(KeyEventArgs e, out double dx, out double dy)
    {
        dx = dy = 0;
        if (PanLeftGesture.Matches(e)) { dx = KeyPanStep; return true; }
        if (PanRightGesture.Matches(e)) { dx = -KeyPanStep; return true; }
        if (PanUpGesture.Matches(e)) { dy = KeyPanStep; return true; }
        if (PanDownGesture.Matches(e)) { dy = -KeyPanStep; return true; }
        return false;
    }

    private static bool TryExecute(ICommand? command)
    {
        if (command?.CanExecute(null) != true)
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private static bool TryExecuteWithParameter(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) != true)
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    /// <summary>
    /// Every adjacent paged-mode navigation path funnels through here instead of calling
    /// <see cref="TryExecute"/> directly (spec §3.1) - records <paramref name="direction"/> so the
    /// <see cref="PageProperty"/> change this command is about to cause (via <see cref="LeftCommand"/>/
    /// <see cref="RightCommand"/> → <c>ReaderScreenViewModel.GoToPage</c>) is recognized as a turn, not
    /// a jump, in <see cref="OnPropertyChanged"/>.
    /// </summary>
    private bool ExecuteDirectional(ICommand? command, PageTransitionDirection direction)
    {
        _pendingTransitionDirection = direction;
        return TryExecute(command);
    }
}

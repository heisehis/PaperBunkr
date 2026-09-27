using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

public partial class ReaderScreen : UserControl
{
    private ReaderScreenViewModel? _viewModel;
    private GamepadPoller? _gamepad;
    private Window? _hostWindow;

    /// <summary>
    /// The rail-nav switcher only toggles <c>IsVisible</c> on this screen's host and Avalonia 12 raises no public change notification for <c>IsEffectivelyVisible</c>, so a
    /// half-second supervisor decides whether the 60 Hz poller should run (visible, window active, setting on). It costs one property read twice a second.
    /// </summary>
    private DispatcherTimer? _gamepadSupervisor;

    public ReaderScreen()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        InitializePinPanel();
        ClipOverlay.RegionCaptured += OnClipRegionCaptured;

        // Dock-style hover magnify on the page-turn dot strip (on-screen feedback) - AddHandler with
        // handledEventsToo: true, not the plain XAML attribute, since "not seeing it at all" traced
        // to individual pageDot Buttons already marking PointerMoved/PointerExited handled before
        // it bubbles up to the ItemsControl. Same fix shape as the Ctrl+Shift+P handler right below.
        PageDotsItemsControl.AddHandler(PointerMovedEvent, OnPageDotsPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        PageDotsItemsControl.AddHandler(PointerExitedEvent, OnPageDotsPointerExited, RoutingStrategies.Bubble, handledEventsToo: true);

        // Ctrl+Shift+P -> perf overlay (docs/superpowers/specs/2026-09-08-reader-decode-cache-
        // prefetch-pipeline-design.md §10). Handled here rather than via UserControl.KeyBindings so
        // it fires even though PageCanvas is the focused element - handledEventsToo covers the case
        // where PageCanvas already marked an unrelated modifier chord handled.
        // Tunnel only: with Tunnel | Bubble and handledEventsToo the handler ran twice per press (the tunnel pass handled it, the bubble pass ran again) and toggled the overlay back off.
        AddHandler(KeyDownEvent, OnReaderKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        // "Auto in 5s - any key cancels" on the end-of-issue card. Tunnel only: the key press that opens the card
        // must not cancel the countdown the same press just started (a bubble handler would run after the command).
        AddHandler(KeyDownEvent, OnEndCardKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Bad-page reason picker (page intelligence design 4): while it is open every key is swallowed here, before
        // PageCanvas sees it - 1-4 pick a reason, Esc cancels, anything else does nothing (no page turns under it).
        AddHandler(KeyDownEvent, OnReportPickerKeyDown, RoutingStrategies.Tunnel);

        // Reading session clock (comfort design 1): any key, click or wheel turn over the reader counts as the reader being present and reading.
        AddHandler(KeyDownEvent, (_, _) => _viewModel?.NoteReaderInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => _viewModel?.NoteReaderInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => _viewModel?.NoteReaderInput(), RoutingStrategies.Tunnel, handledEventsToo: true);

        // Command palette / go-to-page (reach design 5): Up/Down/Enter/Esc (and Ctrl+K / Ctrl+G to close) are taken here, everything else
        // reaches the query TextBox that owns focus while the palette is open.
        AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel);
    }

    // ===================== Gamepad (reach design 3): polled only while this screen is visible and its window active =====================

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _hostWindow = TopLevel.GetTopLevel(this) as Window;
        if (_hostWindow is not null)
        {
            _hostWindow.PropertyChanged += OnHostWindowPropertyChanged;
        }

        UpdateGamepadRunning();
        _gamepadSupervisor ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _gamepadSupervisor.Tick -= OnGamepadSupervisorTick;
        _gamepadSupervisor.Tick += OnGamepadSupervisorTick;
        _gamepadSupervisor.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gamepadSupervisor?.Stop();
        if (_hostWindow is not null)
        {
            _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;
            _hostWindow = null;
        }

        _gamepad?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnGamepadSupervisorTick(object? sender, EventArgs e) => UpdateGamepadRunning();

    private void OnHostWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty)
        {
            UpdateGamepadRunning();
        }
    }

    private void UpdateGamepadRunning()
    {
        bool present = IsEffectivelyVisible && _hostWindow is { IsActive: true };
        _viewModel?.SetUserPresent(present);
        bool run = _viewModel is { GamepadEnabled: true } && present;
        if (run)
        {
            _gamepad ??= new GamepadPoller(new XInputSource(), OnGamepadFrame);
            _gamepad.Start();
        }
        else
        {
            _gamepad?.Stop();
        }
    }

    private void OnGamepadFrame(GamepadFrame frame, TimeSpan elapsed)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        vm.NoteReaderInput();
        if (frame.HasAny && (frame.Next || frame.Previous || frame.Palette || frame.Leave || frame.ToggleChrome || frame.Fullscreen))
        {
            Services.Reader.ReaderPerfStats.Current.RecordInput("gamepad button");
        }

        var palette = vm.Palette;
        if (frame.Palette)
        {
            palette.Toggle();
            return;
        }

        if (palette.IsOpen)
        {
            // While the palette is open the pad drives it: D-pad/left stick up and down move, A runs, B closes.
            if (frame.Up)
            {
                palette.MoveSelection(-1);
            }

            if (frame.Down)
            {
                palette.MoveSelection(1);
            }

            if (frame.Next)
            {
                palette.ExecuteSelectedCommand.Execute(null);
            }
            else if (frame.Previous)
            {
                palette.Close();
            }

            return;
        }

        if (vm.IsReportPickerOpen)
        {
            if (frame.Previous)
            {
                vm.CancelReportPickerCommand.Execute(null);
            }

            return;
        }

        if (frame.Leave)
        {
            vm.GoBackCommand.Execute(null);
            return;
        }

        if (frame.ToggleChrome)
        {
            vm.ToggleChromeCommand.Execute(null);
        }

        if (frame.Fullscreen)
        {
            vm.ToggleFullscreenCommand.Execute(null);
        }

        if (frame.Next)
        {
            PageCanvasControl.GamepadTurn(forward: true);
        }

        if (frame.Previous)
        {
            PageCanvasControl.GamepadTurn(forward: false);
        }

        int dx = (frame.Right ? 1 : 0) - (frame.Left ? 1 : 0);
        int dy = (frame.Down ? 1 : 0) - (frame.Up ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            PageCanvasControl.GamepadDirection(dx, dy);
        }

        if (frame.PanX != 0 || frame.PanY != 0)
        {
            PageCanvasControl.GamepadAnalog(frame.PanX, frame.PanY, elapsed);
        }

        if (frame.Zoom != 0)
        {
            PageCanvasControl.GamepadZoom(frame.Zoom, elapsed);
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { Palette: { IsOpen: true } palette } vm)
        {
            return;
        }

        if (vm.CommandPaletteKey.Any(g => g.Matches(e)) || vm.GoToPageKey.Any(g => g.Matches(e)))
        {
            palette.Close();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                palette.Close();
                e.Handled = true;
                break;
            case Key.Down:
                palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                palette.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Palette just opened: focus its query box; just closed: hand focus back to the canvas so reader keys work again.</summary>
    private void OnPalettePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReaderCommandPaletteViewModel.IsOpen) || sender is not ReaderCommandPaletteViewModel palette)
        {
            return;
        }

        if (palette.IsOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PaletteQueryBox.Focus();
                PaletteQueryBox.SelectAll();
            });
        }
        else
        {
            Dispatcher.UIThread.Post(() => PageCanvasControl.Focus());
        }
    }

    private void OnReportPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsReportPickerOpen: true })
        {
            return;
        }

        PageReportReason? reason = e.Key switch
        {
            Key.D1 or Key.NumPad1 => PageReportReason.Corrupt,
            Key.D2 or Key.NumPad2 => PageReportReason.Blank,
            Key.D3 or Key.NumPad3 => PageReportReason.LowRes,
            Key.D4 or Key.NumPad4 => PageReportReason.Other,
            _ => null,
        };

        if (reason is { } chosen)
        {
            _viewModel.ReportPageReasonCommand.Execute(chosen);
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.CancelReportPickerCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnEndCardKeyDown(object? sender, KeyEventArgs e) => _viewModel?.CancelEndCardCountdown();

    private void OnReaderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _viewModel?.TogglePerfOverlayCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Known-gap fix (docs/Paperbunkr-Roadmap.md P1): <see cref="PageCanvas"/> previously needed a
    /// manual click before arrow keys registered, because the rail-nav screen switcher never
    /// destroys/recreates screens - it just toggles a <c>ContentControl</c>'s <c>IsVisible</c>
    /// (MainWindow.axaml), so <c>Loaded</c>/<c>AttachedToVisualTree</c> only ever fire once, at
    /// app startup, long before the Reader screen is ever shown. Reacting to
    /// <see cref="ReaderScreenViewModel.CurrentPage"/> changing instead - which fires every time
    /// an issue is (re)loaded, i.e. exactly when the user actually navigates into the Reader -
    /// sidesteps that entirely.
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ScrollToPageRequested -= OnScrollToPageRequested;
            _viewModel.NoteFocusRequested -= OnNoteFocusRequested;
            _viewModel.ZoomStepRequested -= OnZoomStepRequested;
            _viewModel.ZoomResetRequested -= OnZoomResetRequested;
            _viewModel.CurrentPageIndexChanged -= OnCurrentPageIndexChanged;
            _viewModel.ReflowTransitionRequested -= OnReflowTransitionRequested;
            _viewModel.Palette.PropertyChanged -= OnPalettePropertyChanged;
        }

        _viewModel = DataContext as ReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ScrollToPageRequested += OnScrollToPageRequested;
            _viewModel.NoteFocusRequested += OnNoteFocusRequested;
            _viewModel.ZoomStepRequested += OnZoomStepRequested;
            _viewModel.ZoomResetRequested += OnZoomResetRequested;
            _viewModel.CurrentPageIndexChanged += OnCurrentPageIndexChanged;
            _viewModel.ReflowTransitionRequested += OnReflowTransitionRequested;
            _viewModel.Palette.PropertyChanged += OnPalettePropertyChanged;
        }

        UpdateGamepadRunning();
    }

    /// <summary>
    /// Continuous mode's thumbnail-rail-click handler (docs/superpowers/specs/2026-08-10-reader-
    /// polish-continuous-scroll-chrome-overlays-design.md §6) - <see cref="ReaderScreenViewModel"/>
    /// raises this instead of computing a scroll offset itself, since only <see cref="PageCanvas"/>
    /// knows real/estimated per-page sizes.
    /// </summary>
    private void OnScrollToPageRequested(int pageIndex) => PageCanvasControl.ScrollToPage(pageIndex);

    // ===================== Notes and clips =====================

    /// <summary>The capture overlay's drag rectangle (canvas coordinates) as fractions of the displayed page, then the view model cuts the clip.</summary>
    private void OnClipRegionCaptured(object? sender, Rect e)
    {
        var imageBounds = PageCanvasControl.GetCurrentImageBounds();
        if (_viewModel is null || imageBounds.Width <= 0 || imageBounds.Height <= 0)
        {
            return;
        }

        double x = Math.Clamp((e.X - imageBounds.X) / imageBounds.Width, 0, 1);
        double y = Math.Clamp((e.Y - imageBounds.Y) / imageBounds.Height, 0, 1);
        double width = Math.Clamp(e.Width / imageBounds.Width, 0, 1 - x);
        double height = Math.Clamp(e.Height / imageBounds.Height, 0, 1 - y);
        _viewModel.CaptureClip(new Rect(x, y, width, height));
    }

    /// <summary>Puts the cursor in the note box once the drawer section has had a chance to show it.</summary>
    private void OnNoteFocusRequested() => Dispatcher.UIThread.Post(() =>
    {
        NoteBox.Focus();
        NoteBox.CaretIndex = NoteBox.Text?.Length ?? 0;
    });

    private void OnClipCaptionLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: NoteListItem item } box)
        {
            _viewModel?.CommitClipCaption(item, box.Text);
        }
    }

    // ===================== Pinned reference page: drag to a corner, wheel to resize =====================

    private readonly TranslateTransform _pinDrag = new();
    private bool _pinDragging;
    private Point _pinDragStart;

    private void InitializePinPanel()
    {
        PinPanel.RenderTransform = _pinDrag;
        PinPanel.PointerPressed += OnPinPressed;
        PinPanel.PointerMoved += OnPinMoved;
        PinPanel.PointerReleased += OnPinReleased;
        PinPanel.PointerCaptureLost += (_, _) => EndPinDrag();
        PinPanel.PointerWheelChanged += OnPinWheel;
    }

    private void OnPinPressed(object? sender, PointerPressedEventArgs e)
    {
        // A press on the size or unpin button is the button's own.
        if (!e.GetCurrentPoint(PinPanel).Properties.IsLeftButtonPressed || (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null))
        {
            return;
        }

        _pinDragging = true;
        _pinDragStart = e.GetPosition(this);
        e.Pointer.Capture(PinPanel);
        e.Handled = true;
    }

    private void OnPinMoved(object? sender, PointerEventArgs e)
    {
        if (!_pinDragging)
        {
            return;
        }

        var point = e.GetPosition(this);
        _pinDrag.X = point.X - _pinDragStart.X;
        _pinDrag.Y = point.Y - _pinDragStart.Y;
        e.Handled = true;
    }

    private void OnPinReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pinDragging)
        {
            return;
        }

        var point = e.GetPosition(this);
        e.Pointer.Capture(null);
        EndPinDrag();
        _viewModel?.SetPinCorner(Services.Reader.ReaderPinMath.NearestCorner(point, Bounds.Size));
        e.Handled = true;
    }

    private void EndPinDrag()
    {
        _pinDragging = false;
        _pinDrag.X = 0;
        _pinDrag.Y = 0;
    }

    private void OnPinWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y != 0)
        {
            _viewModel?.StepPinSize(e.Delta.Y > 0);
            e.Handled = true;
        }
    }

    /// <summary>The zoom buttons, keys and palette glide to the new zoom (about the middle of the page) instead of jumping to it.</summary>
    private void OnZoomStepRequested(double factor) => PageCanvasControl.SmoothZoomBy(factor);

    private void OnZoomResetRequested() => PageCanvasControl.SmoothZoomToFit();

    /// <summary>Double-page layout-mode/reading-direction reflow (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §6) - same "ViewModel raises, View has the geometry" split as <see cref="OnScrollToPageRequested"/> above.</summary>
    private void OnReflowTransitionRequested(Bitmap? oldPrimary, Bitmap? oldSecondary, bool oldIsRightToLeft) =>
        PageCanvasControl.PlayReflowTransition(oldPrimary, oldSecondary, oldIsRightToLeft);

    /// <summary>
    /// User direction: the thumbnail rail should keep the current page's thumbnail scrolled into
    /// view as continuous-mode scrolling progresses, "follows along, but it's not really bound to
    /// it" - a nudge-into-view on change (<see cref="ControlExtensions.BringIntoView(Control)"/>
    /// scrolls the minimum distance needed, it doesn't force-center or lock the rail's scroll
    /// position to the canvas's), not a tight two-way binding between the two scroll positions.
    /// Deferred a dispatcher cycle - same reason <see cref="OnViewModelPropertyChanged"/>'s own
    /// <c>Focus()</c> call already is: the container for a just-added/just-selected thumbnail index
    /// isn't guaranteed realized by <see cref="ItemsControl.ContainerFromIndex"/> until the next
    /// layout pass has actually run.
    /// </summary>
    private void OnCurrentPageIndexChanged(int pageIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ThumbnailsItemsControl.ContainerFromIndex(pageIndex) is Control container)
            {
                container.BringIntoView();
            }
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderScreenViewModel.CurrentPage))
        {
            // Deferred to the next dispatcher cycle: Load() runs before MainViewModel flips
            // CurrentScreen to "reader" and the IsVisible binding propagates, so calling Focus()
            // synchronously here would target a not-yet-effectively-visible control and silently
            // no-op - the same failure mode as the bug this fixes.
            Dispatcher.UIThread.Post(() => PageCanvasControl.Focus());
            return;
        }

        if (e.PropertyName == nameof(ReaderScreenViewModel.IsPanelFlashVisible) && _viewModel is { IsPanelFlashVisible: true } flashVm)
        {
            // The overlay draws over the page, so it needs where the page is on the canvas right now and the panels in the orientation shown.
            PanelFlashOverlay.PageRect = PageCanvasControl.GetPageScreenRect();
            PanelFlashOverlay.Panels = PageCanvasControl.GetShownPanels();
            PanelFlashOverlay.Confident = flashVm.CurrentPagePanels?.Confident ?? false;
            return;
        }

        if (e.PropertyName == nameof(ReaderScreenViewModel.GamepadEnabled))
        {
            UpdateGamepadRunning();
            return;
        }

        if (e.PropertyName == nameof(ReaderScreenViewModel.IsReportPickerOpen) && _viewModel is { IsReportPickerOpen: false })
        {
            // The picker just hid (possibly with a clicked Button holding focus) - hand focus back so reader keys keep working.
            Dispatcher.UIThread.Post(() => PageCanvasControl.Focus());
            return;
        }

        if (e.PropertyName == nameof(ReaderScreenViewModel.IsFullscreen) && _viewModel is not null)
        {
            ApplyFullscreenState(_viewModel.IsFullscreen);

            // Real bug, found via manual testing: toggling fullscreen via the toolbar button (rather
            // than the F/F11 keys) moves focus onto that Button - which then immediately collapses
            // along with the rest of the toolbar, leaving nothing with keyboard focus at all. Without
            // this, F/F11 (and every other Reader key) silently stop responding the moment someone
            // enters fullscreen by clicking rather than pressing a key - the collapsed Button has no
            // way to hand focus back on its own. Deferred a dispatcher cycle for the same reason
            // every other post-visibility-change Focus() call in this file already is.
            Dispatcher.UIThread.Post(() => PageCanvasControl.Focus());
        }
    }

    /// <summary>
    /// The actual Window-level effect of the fullscreen toggle - the ViewModel only tracks the
    /// boolean, this is the one place that touches <c>Window.WindowState</c>. The thumbnail rail no
    /// longer collapses here (docs/superpowers/specs/2026-08-25-reader-chrome-design.md) - it's
    /// persistent in both windowed and fullscreen now, unlike the old top-toolbar/bottom-bar rows
    /// this replaced. Same <see cref="Paperbunkr.App.Services.FilePickerService"/>-precedented way
    /// of reaching the app's single window (<c>IClassicDesktopStyleApplicationLifetime.MainWindow</c>),
    /// since this app is one window with rail-nav content-switching, not one window per screen.
    /// </summary>
    private void ApplyFullscreenState(bool isFullscreen)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            window.WindowState = isFullscreen ? WindowState.FullScreen : WindowState.Normal;
        }
    }

    /// <summary>
    /// Two swappable chrome-reveal styles (docs/paperbunkr-todo.md 2026-09-16 - user asked for the
    /// original ambient behavior back alongside the newer per-cluster one, not as a replacement),
    /// picked per <see cref="ReaderScreenViewModel.ChromeHoverMode"/> (Preferences > Reader):
    ///
    /// <see cref="ReaderChromeHoverMode.PerCluster"/> - each corner cluster reveals only while the
    /// pointer is over its own zone, computed directly from pointer position on every real move
    /// rather than per-element PointerEntered/Exited - two separate attempts at the latter
    /// (2026-09-16) both left a cluster stuck visible forever once shown, and a live on-screen
    /// retest of the second (wiring Entered/Exited on the real cluster Border too, not just a
    /// separate hotspot underneath) showed literally zero change, ruling out the specific occlusion
    /// theory that second attempt was built on - this switches to computing "is the pointer near
    /// this corner" directly from the exact same event that was already reliably firing all session
    /// for the (unrelated) shortcut-hint refresh below, sidestepping Avalonia's Enter/Exit-on-
    /// dynamically-hit-testable-elements behavior entirely - no hotspots, no Entered/Exited
    /// handlers, just arithmetic against the Grid's own real-time Bounds. Zone sizes are the same
    /// rough dimensions the retired hotspots used, not pixel-exact to each cluster's real content -
    /// a real tuning target if a corner feels off, not a sign of a deeper bug.
    ///
    /// <see cref="ReaderChromeHoverMode.Ambient"/> - the original behavior this replaced: any
    /// pointer movement over the reading canvas reveals every cluster at once
    /// (<see cref="ReaderScreenViewModel.NotifyCursorActivity"/>, which also arms the idle-fade
    /// timer), no per-corner zones at all.
    /// </summary>
    private const double NavigateZoneWidth = 300, NavigateZoneHeight = 60;
    private const double ActionsZoneWidth = 220, ActionsZoneHeight = 60;
    private const double ViewZoneWidth = 300, ViewZoneHeight = 80;
    private const double PageTurnZoneWidth = 340, PageTurnZoneHeight = 70;

    private void OnReaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null || sender is not Visual rootVisual)
        {
            return;
        }

        if (_viewModel.ChromeHoverMode == ReaderChromeHoverMode.Ambient)
        {
            _viewModel.NotifyCursorActivity();
            return;
        }

        Point pos = e.GetPosition(rootVisual);
        Size bounds = ((Control)rootVisual).Bounds.Size;

        _viewModel.IsNavigateClusterHovered = pos.X < NavigateZoneWidth && pos.Y < NavigateZoneHeight;
        _viewModel.IsActionsClusterHovered = pos.X > bounds.Width - ActionsZoneWidth && pos.Y < ActionsZoneHeight;
        _viewModel.IsViewClusterHovered = pos.X < ViewZoneWidth && pos.Y > bounds.Height - ViewZoneHeight;
        double pageTurnCenter = bounds.Width / 2;
        _viewModel.IsPageTurnClusterHovered = pos.Y > bounds.Height - PageTurnZoneHeight
            && pos.X > pageTurnCenter - (PageTurnZoneWidth / 2) && pos.X < pageTurnCenter + (PageTurnZoneWidth / 2);

        // Unrelated pre-existing side effect (keeps keyboard-shortcut tooltips fresh after a
        // Preferences remap) that piggybacked on this same event purely as a convenient "something
        // happened" trigger - kept independent of the chrome-reveal logic above it. Ambient mode's
        // branch above gets this for free too, since NotifyCursorActivity already calls it.
        _viewModel.RefreshShortcutHints();
    }

    /// <summary>Drives IsViewClusterCollapsed (docs/superpowers/specs/2026-08-25-reader-chrome-design.md) - the ~720px threshold below which the View cluster's fit-mode/zoom controls would start crowding the Page-turn cluster, derived from the two clusters' real content widths during that phase's brainstorm.</summary>
    private void OnReaderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IsViewClusterCollapsed = e.NewSize.Width < 720;
            _viewModel.NotifyCanvasResized((int)e.NewSize.Width, (int)e.NewSize.Height);
        }
    }

    /// <summary>P6 fix (docs/paperbunkr-todo.md) - click-to-jump on the thumbnail rail.</summary>
    private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ReaderThumbnailSample thumbnail } && DataContext is ReaderScreenViewModel viewModel)
        {
            viewModel.SelectThumbnailCommand.Execute(thumbnail);
        }
    }

    // ===================== Dock-style hover magnify on the page-turn dot strip (on-screen feedback:
    // "the macOS dock... can we add that to the progress bar") - each dot's ScaleTransform is driven
    // live from pointer X distance, not a Style trigger (a pseudo-class can't express "how close is
    // the cursor to THIS one among ~100 siblings"). Falloff: 1.0 scale at MagnifyRadius+ away, up to
    // MagnifyMaxScale directly under the cursor, eased (squared) rather than linear so it reads as a
    // gentle ramp near the peak like the real Dock, not a sharp cone. =====================

    private const double MagnifyRadius = 34.0;
    private const double MagnifyMaxScale = 2.2;

    private void OnPageDotsPointerMoved(object? sender, PointerEventArgs e)
    {
        double pointerX = e.GetPosition(PageDotsItemsControl).X;
        foreach (var dot in PageDotsItemsControl.GetVisualDescendants().OfType<Button>())
        {
            if (!dot.Classes.Contains("pageDot"))
            {
                continue;
            }

            Point? center = dot.TranslatePoint(new Point(dot.Bounds.Width / 2, dot.Bounds.Height / 2), PageDotsItemsControl);
            double distance = center is { } c ? Math.Abs(pointerX - c.X) : double.MaxValue;
            double t = Math.Clamp(1.0 - distance / MagnifyRadius, 0.0, 1.0);
            double scale = 1.0 + (MagnifyMaxScale - 1.0) * (t * t);
            dot.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    private void OnPageDotsPointerExited(object? sender, PointerEventArgs e)
    {
        foreach (var dot in PageDotsItemsControl.GetVisualDescendants().OfType<Button>())
        {
            if (dot.Classes.Contains("pageDot"))
            {
                dot.RenderTransform = null;
            }
        }
    }
}

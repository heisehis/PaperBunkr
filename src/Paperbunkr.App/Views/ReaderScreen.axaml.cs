using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class ReaderScreen : UserControl
{
    private ReaderScreenViewModel? _viewModel;

    public ReaderScreen()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
            _viewModel.CurrentPageIndexChanged -= OnCurrentPageIndexChanged;
            _viewModel.ReflowTransitionRequested -= OnReflowTransitionRequested;
        }

        _viewModel = DataContext as ReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ScrollToPageRequested += OnScrollToPageRequested;
            _viewModel.CurrentPageIndexChanged += OnCurrentPageIndexChanged;
            _viewModel.ReflowTransitionRequested += OnReflowTransitionRequested;
        }
    }

    /// <summary>
    /// Continuous mode's thumbnail-rail-click handler (docs/superpowers/specs/2026-08-10-reader-
    /// polish-continuous-scroll-chrome-overlays-design.md §6) - <see cref="ReaderScreenViewModel"/>
    /// raises this instead of computing a scroll offset itself, since only <see cref="PageCanvas"/>
    /// knows real/estimated per-page sizes.
    /// </summary>
    private void OnScrollToPageRequested(int pageIndex) => PageCanvasControl.ScrollToPage(pageIndex);

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

    /// <summary>Feeds <see cref="ReaderScreenViewModel.NotifyCursorActivity"/> - applies in both windowed and fullscreen now (docs/superpowers/specs/2026-08-25-reader-chrome-design.md), unlike the fullscreen-only guard this replaced.</summary>
    private void OnReaderPointerMoved(object? sender, PointerEventArgs e) => _viewModel?.NotifyCursorActivity();

    /// <summary>Drives IsViewClusterCollapsed (docs/superpowers/specs/2026-08-25-reader-chrome-design.md) - the ~720px threshold below which the View cluster's fit-mode/zoom controls would start crowding the Page-turn cluster, derived from the two clusters' real content widths during that phase's brainstorm.</summary>
    private void OnReaderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IsViewClusterCollapsed = e.NewSize.Width < 720;
        }
    }

    /// <summary>P6 fix (docs/alpha-todo.md) - click-to-jump on the thumbnail rail.</summary>
    private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ReaderThumbnailSample thumbnail } && DataContext is ReaderScreenViewModel viewModel)
        {
            viewModel.SelectThumbnailCommand.Execute(thumbnail);
        }
    }

    // ===================== Thumbnail rail auto-hide (docs/superpowers/specs/2026-08-25-reader-
    // chrome-design.md follow-up) - pure hover UI state, not modeled in the ViewModel since nothing
    // outside this view cares about it. Two independent triggers (the edge strip and the rail
    // itself) both keep it open; it only collapses once the pointer has left both. =====================

    private bool _hoveringRailTrigger;
    private bool _hoveringRailOverlay;

    private void OnRailHoverEntered(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(sender, RailEdgeTrigger))
        {
            _hoveringRailTrigger = true;
        }
        else
        {
            _hoveringRailOverlay = true;
        }

        RailOverlay.Classes.Remove("hidden");
    }

    private void OnRailHoverExited(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(sender, RailEdgeTrigger))
        {
            _hoveringRailTrigger = false;
        }
        else
        {
            _hoveringRailOverlay = false;
        }

        UpdateRailVisibility();
    }

    private void UpdateRailVisibility()
    {
        if (!_hoveringRailTrigger && !_hoveringRailOverlay)
        {
            RailOverlay.Classes.Add("hidden");
        }
    }
}

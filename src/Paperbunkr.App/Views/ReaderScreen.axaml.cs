using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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
    /// Known-gap fix (docs/alpha-roadmap.md P1): <see cref="PageCanvas"/> previously needed a
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
        }

        _viewModel = DataContext as ReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ScrollToPageRequested += OnScrollToPageRequested;
            _viewModel.CurrentPageIndexChanged += OnCurrentPageIndexChanged;
        }
    }

    /// <summary>
    /// Continuous mode's thumbnail-rail-click handler (docs/superpowers/specs/2026-08-10-reader-
    /// polish-continuous-scroll-chrome-overlays-design.md §6) - <see cref="ReaderScreenViewModel"/>
    /// raises this instead of computing a scroll offset itself, since only <see cref="PageCanvas"/>
    /// knows real/estimated per-page sizes.
    /// </summary>
    private void OnScrollToPageRequested(int pageIndex) => PageCanvasControl.ScrollToPage(pageIndex);

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

            // Same follow behavior, extended to the fullscreen scrubber overlay (§7) - it's a second,
            // independent thumbnail strip over the same Thumbnails collection, not linked to the side
            // rail's own scroll position.
            if (ScrubberItemsControl.ContainerFromIndex(pageIndex) is Control scrubberContainer)
            {
                scrubberContainer.BringIntoView();
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
    /// The actual Window-level effect of docs/superpowers/specs/2026-08-10-reader-polish-continuous-
    /// scroll-chrome-overlays-design.md §7's fullscreen toggle - the ViewModel only tracks the
    /// boolean, this is the one place that touches <c>Window.WindowState</c> and the rail's pixel
    /// width (a fixed-width column doesn't collapse just because its content's <c>IsVisible</c>
    /// does, unlike the toolbar/bottom-bar's <c>Auto</c>-sized rows).
    /// Same <see cref="Paperbunkr.App.Services.FilePickerService"/>-precedented way of reaching the app's single window
    /// (<c>IClassicDesktopStyleApplicationLifetime.MainWindow</c>), since this app is one window
    /// with rail-nav content-switching, not one window per screen.
    /// </summary>
    private void ApplyFullscreenState(bool isFullscreen)
    {
        ReaderBodyGrid.ColumnDefinitions[0].Width = isFullscreen ? new GridLength(0) : new GridLength(96);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            window.WindowState = isFullscreen ? WindowState.FullScreen : WindowState.Normal;
        }
    }

    /// <summary>Feeds <see cref="ReaderScreenViewModel.NotifyCursorActivity"/> (spec §7's "reappearing on mouse move") - a no-op outside fullscreen, guarded on the ViewModel side.</summary>
    private void OnReaderPointerMoved(object? sender, PointerEventArgs e) => _viewModel?.NotifyCursorActivity();

    /// <summary>P6 fix (docs/alpha-todo.md) - click-to-jump on the thumbnail rail. Also handles the fullscreen scrubber overlay's thumbnails (§7), which reuse the same <see cref="ReaderThumbnailSample"/> DataContext shape and click behavior.</summary>
    private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ReaderThumbnailSample thumbnail } && DataContext is ReaderScreenViewModel viewModel)
        {
            viewModel.SelectThumbnailCommand.Execute(thumbnail);
        }
    }
}

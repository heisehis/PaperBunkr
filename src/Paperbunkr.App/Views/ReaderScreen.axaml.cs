using System;
using System.ComponentModel;
using Avalonia.Controls;
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
        }

        _viewModel = DataContext as ReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReaderScreenViewModel.CurrentPage))
        {
            return;
        }

        // Deferred to the next dispatcher cycle: Load() runs before MainViewModel flips
        // CurrentScreen to "reader" and the IsVisible binding propagates, so calling Focus()
        // synchronously here would target a not-yet-effectively-visible control and silently
        // no-op - the same failure mode as the bug this fixes.
        Dispatcher.UIThread.Post(() => PageCanvasControl.Focus());
    }

    /// <summary>P6 fix (docs/alpha-todo.md) - click-to-jump on the thumbnail rail.</summary>
    private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ReaderThumbnailSample thumbnail } && DataContext is ReaderScreenViewModel viewModel)
        {
            viewModel.SelectThumbnailCommand.Execute(thumbnail);
        }
    }
}

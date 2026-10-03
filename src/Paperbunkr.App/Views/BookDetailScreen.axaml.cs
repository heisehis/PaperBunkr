using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;
using System;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for <see cref="BookDetailScreen"/>.
/// </summary>
public partial class BookDetailScreen : UserControl
{
    private readonly FocusReclaimer _focus;
    private BookDetailScreenViewModel? _viewModel;

    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public BookDetailScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.Detail, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.DetailContinue] = () => DataContext is BookDetailScreenViewModel vm && vm.ContinueCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.ContinueCommand.Execute(null)),
            [InputActionIds.DetailEdit] = () => DataContext is BookDetailScreenViewModel vm && vm.EditCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.EditCommand.Execute(null)),
        });
        DetailCosmetics.Attach(this);
        _focus = new FocusReclaimer(this, () => _viewModel is not null, FocusDefault);
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => _focus.Reclaim();
    }

    private void Subscribe()
    {
        if (_viewModel is { } old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
            old.SeriesBooks.CollectionChanged -= OnContentChanged;
            old.Chapters.CollectionChanged -= OnContentChanged;
        }

        _viewModel = DataContext as BookDetailScreenViewModel;
        if (_viewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.SeriesBooks.CollectionChanged += OnContentChanged;
            vm.Chapters.CollectionChanged += OnContentChanged;
        }
    }

    private void OnContentChanged(object? sender, NotifyCollectionChangedEventArgs e) => _focus.ReclaimIfFocusWithinOrNowhere();

    // Book and series mode are sibling panels toggled by IsVisible, so a mode switch hides the focused control without detaching it.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookDetailScreenViewModel.Mode))
        {
            _focus.ReclaimIfFocusLost();
        }
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Visual row && FocusReclaimer.TryStepVertically(row, e))
        {
            e.Handled = true;
        }
    }

    // The back link is always first in tab order, so skip it: the first content action is the useful landing spot.
    private void FocusDefault() => FocusReclaimer.FocusFirstButton(this, b => !b.Classes.Contains("backLink"));

    /// <summary>Spatial arrow-key navigation across the series-mode book-card grid
    /// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md), mirroring
    /// <c>LibraryScreen.axaml.cs</c>'s own <c>OnCardKeyDown</c>.</summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Button { DataContext: not null } button ||
            button.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return;
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(itemsControl, button, e.Key))
        {
            e.Handled = true;
        }
    }
}

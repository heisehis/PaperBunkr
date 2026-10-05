using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using System;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Views;

public partial class SmartScreen : UserControl
{
    private readonly TypeAheadSearch.Buffer _typeAheadBuffer = new();
    private readonly FocusReclaimer _focus;
    private SmartScreenViewModel? _viewModel;
    private object? _focusBeforeGroupedReview;

    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public SmartScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.SmartLists, new Dictionary<string, Func<bool>>
        {
            // Each of these rewrites the lists the sidebar and the result grids are bound to, so they run once the key press has finished routing.
            [InputActionIds.NewItem] = () => _viewModel is { } vm && ScreenInput.Deferred(() => vm.CreateNewCommand.Execute(null)),
            [InputActionIds.Save] = () => _viewModel is { CanSaveList: true } vm && ScreenInput.Deferred(() => vm.SaveCommand.Execute(null)),
            [InputActionIds.SmartDuplicate] = () => _viewModel is { CanDuplicateList: true } vm && ScreenInput.Deferred(() => vm.DuplicateCommand.Execute(null)),
            [InputActionIds.Refresh] = () => _viewModel is { } vm && ScreenInput.Deferred(vm.RefreshSidebar),
        });
        // Type-ahead (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md) -
        // no multi-select here to clear (this screen is read-only browse, unlike Library/Books), so
        // the clear-selection callback is a no-op.
        AddHandler(TextInputEvent, OnScreenTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnScreenKeyDownForBackspace, RoutingStrategies.Tunnel);

        // Keyboard reach: the three result grids swap by IsVisible and are cleared-and-refilled in place on every list switch or run, which
        // detaches whichever card held focus. See FocusReclaimer.
        _focus = new FocusReclaimer(this, () => _viewModel is { HasResults: true }, FocusFirstResult);
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        DataContextChanged += (_, _) =>
        {
            Subscribe(_viewModel, subscribe: false);
            _viewModel = DataContext as SmartScreenViewModel;
            Subscribe(_viewModel, subscribe: true);
        };
        AttachedToVisualTree += (_, _) => _focus.Reclaim();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                _focus.Reclaim();
            }
        };
    }

    private void Subscribe(SmartScreenViewModel? vm, bool subscribe)
    {
        if (vm is null)
        {
            return;
        }

        foreach (INotifyCollectionChanged collection in new INotifyCollectionChanged[] { vm.Results, vm.SeriesResults, vm.NovelResults })
        {
            if (subscribe)
            {
                collection.CollectionChanged += OnResultsChanged;
            }
            else
            {
                collection.CollectionChanged -= OnResultsChanged;
            }
        }

        if (subscribe)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        else
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) => _focus.ReclaimIfFocusWithinOrNowhere();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SmartScreenViewModel.IsGroupedReviewOpen) || _viewModel is null)
        {
            return;
        }

        // The Grouped Review overlay is a modal: focus goes into it when it opens and back to where it was when it closes.
        if (_viewModel.IsGroupedReviewOpen)
        {
            _focusBeforeGroupedReview = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => FocusReclaimer.FocusFirstButton(GroupedReviewPanel), Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            var previous = _focusBeforeGroupedReview as InputElement;
            _focusBeforeGroupedReview = null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (previous is { IsEffectivelyVisible: true } && TopLevel.GetTopLevel(previous) is not null)
                {
                    previous.Focus(NavigationMethod.Directional);
                }
                else
                {
                    _focus.Reclaim();
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void FocusFirstResult()
    {
        ItemsControl? list = new[] { ResultsList, SeriesResultsList, NovelResultsList }.FirstOrDefault(l => l.IsEffectivelyVisible && l.ItemCount > 0);
        if (list is null)
        {
            return;
        }

        if (!GridFocusHelper.FocusItem(list, list.Items[0]!))
        {
            FocusReclaimer.FocusFirstButton(list);
        }
    }

    private void OnGroupedReviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _viewModel is not null)
        {
            _viewModel.CloseGroupedReviewCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HandleTypeAhead(char typedChar, object? source)
    {
        if (source is not Control control || control.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return false;
        }

        return TypeAheadSearch.TryHandleTextInput<object>(
            _typeAheadBuffer, typedChar, itemsControl,
            item => item switch { IssueCardSample issue => issue.Title, SeriesCardSample card => card.Name, BookCardSample book => book.Title, _ => string.Empty },
            clearSelection: () => { });
    }

    private void OnScreenTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is TextBox || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        bool handled = false;
        foreach (char c in e.Text)
        {
            handled = HandleTypeAhead(c, e.Source);
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    private void OnScreenKeyDownForBackspace(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && HandleTypeAhead('\b', e.Source))
        {
            e.Handled = true;
        }
    }

    /// <summary>Backdrop click-to-close for the Grouped Review overlay (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §3) - same pattern as LibraryScreen's Add-issue overlay backdrop.</summary>
    private void OnGroupedReviewBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SmartScreenViewModel vm)
        {
            vm.CloseGroupedReviewCommand.Execute(null);
        }
    }

    /// <summary>
    /// Spatial arrow-key navigation across the virtualized results grid (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md). <see cref="GridKeyboardNavigation.TryHandleArrowKey"/>
    /// now handles virtualized panels itself (generalized from this handler's own original
    /// implementation, once Library's main card grid turned out to need the exact same fix - see
    /// that method's own doc comment), so this is just the standard wiring every other grid uses.
    /// </summary>
    private void OnResultCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control fromControl ||
            fromControl.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return;
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(itemsControl, fromControl, e.Key))
        {
            e.Handled = true;
        }
    }
}

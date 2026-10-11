using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The "New Smart List" gallery dialog. A modal: focus goes into it when it opens and back to where it was when it closes; Esc closes it.
/// Creating a list reloads the Smart screen's sidebar and editor, so the create runs one dispatcher tick after the click that asked for it
/// (a control must not be detached from inside the routed event it is still raising).
/// </summary>
public partial class SmartTemplateGallery : UserControl
{
    private SmartTemplateGalleryViewModel? _viewModel;
    private IInputElement? _focusBefore;

    public SmartTemplateGallery()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as SmartTemplateGalleryViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
        AddHandler(KeyDownEvent, OnGalleryKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SmartTemplateGalleryViewModel.IsOpen) || _viewModel is null)
        {
            return;
        }

        if (_viewModel.IsOpen)
        {
            _focusBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() => FocusReclaimer.FocusFirstButton(CardList), DispatcherPriority.Loaded);
        }
        else
        {
            var previous = _focusBefore as InputElement;
            _focusBefore = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (previous is { IsEffectivelyVisible: true } && TopLevel.GetTopLevel(previous) is not null)
                {
                    previous.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnGalleryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _viewModel is { IsOpen: true } vm)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e) => CreateDeferred();

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SmartTemplateCard card } && _viewModel is { } vm)
        {
            vm.SelectCardCommand.Execute(card);
            CreateDeferred();
        }
    }

    private void CreateDeferred()
    {
        if (_viewModel is { } vm)
        {
            Dispatcher.UIThread.Post(() => vm.CreateCommand.Execute(null));
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class ReadingListReviewView : UserControl
{
    private readonly AttachedInputRegistration _input;
    private readonly FocusReclaimer _focus;
    private ReadingListReviewViewModel? _observed;
    public IInputService InputService { get => _input.Service; set => _input.Service = value; }
    public ReadingListReviewView()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => DataContext is ReadingListReviewViewModel { IsBusy: false }, () =>
        {
            if (EntryQueue.IsEffectivelyVisible) EntryQueue.Focus(NavigationMethod.Tab);
            else if (ReviewSearch.IsEffectivelyVisible) ReviewSearch.Focus(NavigationMethod.Tab);
            else if (HistoryList.IsEffectivelyVisible) FocusReclaimer.FocusFirstButton(HistoryList);
            else this.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.IsEffectivelyVisible)?.Focus(NavigationMethod.Tab);
        });
        DataContextChanged += (_, _) =>
        {
            if (_observed is not null) _observed.PropertyChanged -= ReviewStateChanged;
            _observed = DataContext as ReadingListReviewViewModel;
            if (_observed is not null) _observed.PropertyChanged += ReviewStateChanged;
        };
        // Item navigation belongs to the focused list; take it before ItemsControl can drop a nested button's focus.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Source is not Control source || source is TextBox) return;
            foreach (var list in new[] { CandidateList, ListSelector, HistoryList })
                if (source.GetVisualAncestors().Contains(list) && ReadingListKeyboard.HandleNavigationKey(list, e, 1, 0))
                { e.Handled = true; return; }
        }, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => { if (DataContext is ReadingListReviewViewModel vm) vm.Compact = Bounds.Width < 900; };
        _input = ScreenInput.Attach(this, InputScope.ReadingLists, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.FocusSearch] = () => ScreenInput.FocusTextBox(this, "ReadingListReviewSearch"),
            [InputActionIds.Save] = () => DataContext is ReadingListReviewViewModel { IsBusy: false } vm && ScreenInput.Deferred(() => vm.ApplyCommand.Execute(null)),
            [InputActionIds.Refresh] = () => DataContext is ReadingListReviewViewModel { IsBusy: false } vm && ScreenInput.Deferred(() => vm.RecheckCommand.Execute(null)),
        });
    }

    private void ReviewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReadingListReviewViewModel.IsBusy) or nameof(ReadingListReviewViewModel.IsHistory)
            or nameof(ReadingListReviewViewModel.ShowDetail) or nameof(ReadingListReviewViewModel.IsSelector))
            _focus.ReclaimIfFocusLost();
    }
}

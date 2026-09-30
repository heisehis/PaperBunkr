using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class MangaDetailScreen : UserControl
{
    private readonly FocusReclaimer _focus;
    private MangaDetailScreenViewModel? _viewModel;

    public MangaDetailScreen()
    {
        InitializeComponent();
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
            old.ChapterGroups.CollectionChanged -= OnChaptersChanged;
        }

        _viewModel = DataContext as MangaDetailScreenViewModel;
        if (_viewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ChapterGroups.CollectionChanged += OnChaptersChanged;
        }
    }

    private void OnChaptersChanged(object? sender, NotifyCollectionChangedEventArgs e) => _focus.ReclaimIfFocusWithinOrNowhere();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MangaDetailScreenViewModel.MangaActiveTab))
        {
            _focus.ReclaimIfFocusLost();
        }
    }

    private void OnChapterKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Visual row && FocusReclaimer.TryStepVertically(row, e))
        {
            e.Handled = true;
        }
    }

    // The first chapter row on the Chapters tab; on Related/Details/Activity (whose bodies swap in by IsVisible) the active tab header.
    private void FocusDefault()
    {
        var row = this.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("chapterRow") && b.IsEffectivelyVisible && b.IsEffectivelyEnabled);
        if (row is not null)
        {
            row.Focus(NavigationMethod.Directional);
            FocusReclaimer.BringIntoViewWithRing(row);
            return;
        }

        FocusReclaimer.FocusFirstButton(this, b => b.Classes.Contains("tab") && b.Classes.Contains("active"));
    }
}

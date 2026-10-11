using System;
using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Live preview panel for the Library Master-Detail layout (docs/superpowers/specs/2026-09-26-library-preview-panel-v2-design.md,
/// replacing the content of 2026-09-14-library-visual-redesign-design.md §4). State comes from <c>LibraryScreenViewModel</c>'s
/// computed properties; this file only opens the shared flyouts for the two menu buttons and cross-fades the hero on a selection change.
/// </summary>
public partial class LibraryPreviewPanel : UserControl
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(120);

    /// <summary>A second change inside this window snaps instead of animating, so the panel never lags arrow-key browsing.</summary>
    private static readonly TimeSpan RapidWindow = TimeSpan.FromMilliseconds(180);

    private LibraryScreenViewModel? _vm;
    private DateTime _lastSwapUtc = DateTime.MinValue;

    public LibraryPreviewPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private void AttachViewModel()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as LibraryScreenViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryScreenViewModel.PreviewSeries)
            or nameof(LibraryScreenViewModel.PreviewIssue)
            or nameof(LibraryScreenViewModel.PreviewDrillIssue))
        {
            // The strip is rebuilt (or hidden, on a drill-in) with the previewed item; a popup left open would point at a chip that is gone.
            IssueChipPopup.IsOpen = false;
            FadeHeroes();
        }
    }

    // ---- Issue strip popup (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 3). One shared Popup moved from chip to
    // chip, the same idea as LibraryScreen's hover tooltip, so a long series does not carry a popup per chip. It opens on hover and on
    // keyboard focus alike. Closing it here is safe to do inline: the chip raising the event is not inside the popup. ----

    private void ShowIssueChipPopup(object? sender)
    {
        if (sender is Control { DataContext: Models.PreviewIssueChip chip } anchor)
        {
            IssueChipPopup.IsOpen = false;
            IssueChipPopup.PlacementTarget = anchor;
            if (IssueChipPopup.Child is { } content)
            {
                content.DataContext = chip;
            }

            IssueChipPopup.IsOpen = true;
        }
    }

    private void HideIssueChipPopup(object? sender)
    {
        if (ReferenceEquals(IssueChipPopup.PlacementTarget, sender))
        {
            IssueChipPopup.IsOpen = false;
        }
    }

    private void OnIssueChipPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e) => ShowIssueChipPopup(sender);

    private void OnIssueChipPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) => HideIssueChipPopup(sender);

    private void OnIssueChipGotFocus(object? sender, RoutedEventArgs e) => ShowIssueChipPopup(sender);

    private void OnIssueChipLostFocus(object? sender, RoutedEventArgs e) => HideIssueChipPopup(sender);

    /// <summary>
    /// ~120 ms cross-fade of the hero (backdrop + cover) when the previewed item changes. The transition is attached only for the
    /// fade-in, so dropping the opacity to 0 is instant instead of a fade-out first; and if another change lands inside
    /// <see cref="RapidWindow"/> (arrowing through the grid) nothing animates at all.
    /// </summary>
    private void FadeHeroes()
    {
        var now = DateTime.UtcNow;
        bool rapid = now - _lastSwapUtc < RapidWindow;
        _lastSwapUtc = now;
        if (rapid || MotionTokens.IsReducedMotion())
        {
            return;
        }

        foreach (var hero in new Control[] { SeriesHero, IssueHero })
        {
            hero.Transitions = null;
            hero.Opacity = 0;
            Dispatcher.UIThread.Post(() =>
            {
                hero.Transitions = new Transitions
                {
                    new DoubleTransition { Property = OpacityProperty, Duration = FadeDuration, Easing = new CubicEaseOut() },
                };
                hero.Opacity = 1;
            }, DispatcherPriority.Render);
        }
    }

    /// <summary>"＋" button: the collection list for whatever the panel is showing, in the shared MenuFlyout.</summary>
    private void OnAddToCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor && _vm is not null)
        {
            var entries = new LibraryContextMenuBuilder(_vm).BuildPreviewCollectionMenu(CurrentTarget());
            if (entries.Count > 0)
            {
                ContextMenuHost.ShowMenu(anchor, entries);
            }
        }
    }

    /// <summary>"⋯" button: the rarely used actions for whatever the panel is showing.</summary>
    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor && _vm is not null)
        {
            var entries = new LibraryContextMenuBuilder(_vm).BuildPreviewOverflow(CurrentTarget());
            if (entries.Count > 0)
            {
                ContextMenuHost.ShowMenu(anchor, entries);
            }
        }
    }

    private object? CurrentTarget() =>
        _vm is null ? null : _vm.ShowIssuePreview ? _vm.ActivePreviewIssue : _vm.ShowSeriesPreview ? _vm.PreviewSeries : null;
}

using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Home;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Views;

/// <summary>
/// Home view (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md / -improvements-design.md, spotlight per
/// 2026-09-29-home-spotlight-accordion-design.md). Only view mechanics live here: the per-key section template lookup, stopping
/// spotlight rotation while Home is off screen, and the first-display shelf entrance. The spotlight accordion animates itself
/// (<see cref="Controls.AccordionPanel"/>).
/// </summary>
public partial class HomeScreen : UserControl
{
    private HomeScreenViewModel? _viewModel;
    private bool _firstReadyEffectsStarted;
    private readonly FocusReclaimer _focus;
    private bool _pausedByFocus;
    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public HomeScreen()
    {
        InitializeComponent();
        SectionsHost.ItemTemplate = new HomeSectionTemplateSelector(
            key => this.TryFindResource(key, out var value) ? value as IDataTemplate : null);

        _focus = new FocusReclaimer(this, () => _viewModel is { Sections.Count: > 0 }, FocusDefault);
        KeyDown += OnHomeKeyDown;
        _screenInput = ScreenInput.Attach(this, InputScope.Home, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.FocusSearch] = () => ScreenInput.FocusTextBox(this, "HomeSearchBox"),
            [InputActionIds.Refresh] = () => _viewModel is { } vm && vm.RefreshCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.RefreshCommand.Execute(null)),

            // The bumpers (and Ctrl+PageUp/PageDown) step the spotlight carousel, which is Home's only set of tabs.
            [InputActionIds.TabNext] = () => _viewModel is { } vm && ScreenInput.Deferred(() => vm.NextSpotlightCommand.Execute(null)),
            [InputActionIds.TabPrevious] = () => _viewModel is { } vm && ScreenInput.Deferred(() => vm.PreviousSpotlightCommand.Execute(null)),
        });
        AddHandler(GotFocusEvent, OnHomeGotFocus, Avalonia.Interactivity.RoutingStrategies.Bubble);

        DataContextChanged += (_, _) =>
        {
            SubscribeToContentResets(_viewModel, subscribe: false);
            _viewModel = DataContext as HomeScreenViewModel;
            SubscribeToContentResets(_viewModel, subscribe: true);
            TryStartFirstReadyEffects();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _viewModel?.SetOnScreen(IsVisible);
            TryStartFirstReadyEffects();
            _focus.Reclaim();
        };
        DetachedFromVisualTree += (_, _) => _viewModel?.SetOnScreen(false);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && this.IsAttachedToVisualTree())
            {
                _viewModel?.SetOnScreen(IsVisible);
                if (IsVisible)
                {
                    _focus.Reclaim();
                }
            }
        };
    }

    // Every visit (and Refresh) rebuilds Sections and clears-and-refills the shelf collections, which detaches whichever tile held
    // focus; without this the next arrow key goes nowhere. See FocusReclaimer.
    private void SubscribeToContentResets(HomeScreenViewModel? vm, bool subscribe)
    {
        if (vm is null)
        {
            return;
        }

        foreach (INotifyCollectionChanged collection in new INotifyCollectionChanged[]
                 { vm.Sections, vm.ContinueReading, vm.RecentlyAdded, vm.BecauseYouRead, vm.Collections, vm.SpotlightPanels })
        {
            if (subscribe)
            {
                collection.CollectionChanged += OnContentChanged;
            }
            else
            {
                collection.CollectionChanged -= OnContentChanged;
            }
        }
    }

    private void OnContentChanged(object? sender, NotifyCollectionChangedEventArgs e) => _focus.ReclaimIfFocusWithinOrNowhere();

    // The spotlight carousel is one group: Left/Right switch panels (AccordionPanel handles them), Up/Down leave it for the row above or
    // below instead of bouncing between its panels, nav buttons and Read now. Every other arrow press falls back to the screen-wide
    // directional move.
    private void OnHomeKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down
            && e.Source is Visual source && SpotlightRootOf(source) is { } spotlight
            && TopLevel.GetTopLevel(this) is { } top && spotlight.TranslatePoint(default, top) is { } origin)
        {
            var options = new FindNextElementOptions { ExclusionRect = new Rect(origin, spotlight.Bounds.Size), SearchRoot = this, IgnoreOcclusivity = true };
            var direction = e.Key == Key.Up ? NavigationDirection.Up : NavigationDirection.Down;
            if (top.FocusManager?.FindNextElement(direction, options) is Control next && next.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, this)))
            {
                next.Focus(NavigationMethod.Directional);
                FocusReclaimer.BringIntoViewWithRing(next);
                e.Handled = true;
                return;
            }
        }

        e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
    }

    // Arriving in the carousel from another row lands on its open panel rather than whichever panel or nav button is geometrically
    // nearest (focus alone opens a panel, which would silently change the spotlight). Rotation stands still while focus is inside it.
    private void OnHomeGotFocus(object? sender, FocusChangedEventArgs e)
    {
        var spotlight = e.NewFocusedElement is Visual now ? SpotlightRootOf(now) : null;
        if (spotlight is not null)
        {
            if (!_pausedByFocus)
            {
                _pausedByFocus = true;
                _viewModel?.PauseSpotlightRotation();
            }

            bool cameFromOutside = e.OldFocusedElement is not Visual old || !ReferenceEquals(SpotlightRootOf(old), spotlight);
            if (e.NavigationMethod == NavigationMethod.Directional && cameFromOutside
                && OpenSpotlightPanel(spotlight) is { } open && !ReferenceEquals(open, e.NewFocusedElement))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => open.Focus(NavigationMethod.Directional), Avalonia.Threading.DispatcherPriority.Input);
            }
        }
        else if (_pausedByFocus)
        {
            _pausedByFocus = false;
            _viewModel?.ResumeSpotlightRotation();
        }
    }

    /// <summary>The section container that holds the spotlight accordion, when <paramref name="from"/> is anywhere inside it.</summary>
    private Control? SpotlightRootOf(Visual from)
    {
        var panel = SectionsHost.ItemsPanelRoot;
        for (Visual? v = from; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v.GetVisualParent(), panel))
            {
                return v is Control c && c.GetVisualDescendants().OfType<Controls.AccordionPanel>().Any() ? c : null;
            }
        }

        return null;
    }

    private static Button? OpenSpotlightPanel(Control spotlight) =>
        spotlight.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("spotlightPanel") && b.Classes.Contains("open") && b.IsEffectivelyVisible);

    /// <summary>The spotlight carousel's open panel when there is one (it is the first thing on the page), else the first tile or button, in reading order; the masthead's own controls are reached by Tab/Shift+Tab.</summary>
    private void FocusDefault()
    {
        var target = SectionsHost.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("spotlightPanel") && b.Classes.Contains("open") && b.IsEffectivelyEnabled && b.IsEffectivelyVisible) as Control
            ?? SectionsHost.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c is Button or Border { Focusable: true } && !c.Classes.Contains("spotlightNav") && c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
        if (target is not null)
        {
            target.Focus(NavigationMethod.Directional);
        }
        else
        {
            FocusReclaimer.FocusFirstButton(this);
        }
    }

    /// <summary>Flips the shelf entrance on once both the view and its ViewModel are ready - raising it from the ViewModel's own
    /// constructor would fire before this view subscribed.</summary>
    private void TryStartFirstReadyEffects()
    {
        if (_firstReadyEffectsStarted || _viewModel is null || !this.IsAttachedToVisualTree())
        {
            return;
        }

        _firstReadyEffectsStarted = true;
        _viewModel.PlayEntranceAnimation = true;
    }
}

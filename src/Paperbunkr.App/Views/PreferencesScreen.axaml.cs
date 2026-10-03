using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Views;

public partial class PreferencesScreen : UserControl
{
    private PreferencesScreenViewModel? _vm;
    private DispatcherTimer? _pulseTimer;
    private Control? _pulsing;

    /// <summary>Keeps keyboard focus inside the screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md):
    /// the sections are permanently attached and toggled by <c>IsVisible</c>, and opening a search result hides the very row that was
    /// clicked. Falls back to the active nav item, or the first search result while searching.</summary>
    private readonly FocusReclaimer _focus;

    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public PreferencesScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.Preferences, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.FocusSearch] = () => ScreenInput.FocusTextBox(this, "PreferencesSearchBox"),

            // The bumpers (and Ctrl+PageUp/PageDown) step through the sections in the list on the left.
            [InputActionIds.TabNext] = () => TabStrip.Step(this, 1),
            [InputActionIds.TabPrevious] = () => TabStrip.Step(this, -1),
        });
        _focus = new FocusReclaimer(this, () => DataContext is PreferencesScreenViewModel, FocusFallback);
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                _focus.Reclaim();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _focus.Reclaim();
    }

    private void FocusFallback()
    {
        if (_vm?.IsSearching == true)
        {
            if (this.FindControl<ItemsControl>("SearchResultsList") is { } results)
            {
                FocusReclaimer.FocusFirstButton(results);
            }

            return;
        }

        FocusReclaimer.FocusFirstButton(this, b => b.Classes.Contains("prefNavItem") && b.Classes.Contains("active"));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollToAnchorRequested -= OnScrollToAnchorRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as PreferencesScreenViewModel;

        if (_vm is not null)
        {
            _vm.ScrollToAnchorRequested += OnScrollToAnchorRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreferencesScreenViewModel.ActiveSection) or nameof(PreferencesScreenViewModel.IsSearching))
        {
            _focus.ReclaimIfFocusWithinOrNowhere();
        }
    }

    private void OnScrollToAnchorRequested(string anchorKey)
    {
        // Two hops: let the section's IsVisible binding flip and its layout pass run before we
        // walk the tree for the anchored group.
        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(() => ScrollToAnchor(anchorKey), DispatcherPriority.Background),
            DispatcherPriority.Background);
    }

    private void ScrollToAnchor(string anchorKey)
    {
        var host = this.FindControl<Panel>("ContentHost");
        if (host is null)
        {
            return;
        }

        var target = host.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Tag as string == anchorKey);

        if (target is null)
        {
            return;
        }

        target.BringIntoView();

        if (target is Border border && _vm?.ReducedMotion != true)
        {
            Pulse(border);
        }
    }

    private void Pulse(Control target)
    {
        _pulseTimer?.Stop();
        _pulsing?.Classes.Remove("searchPulse");

        _pulsing = target;
        target.Classes.Add("searchPulse");

        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseTimer?.Stop();
            _pulsing?.Classes.Remove("searchPulse");
            _pulsing = null;
        };
        _pulseTimer.Start();
    }
}

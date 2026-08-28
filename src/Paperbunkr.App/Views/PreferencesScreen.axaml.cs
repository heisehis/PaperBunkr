using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class PreferencesScreen : UserControl
{
    private PreferencesScreenViewModel? _vm;
    private DispatcherTimer? _pulseTimer;
    private Control? _pulsing;

    public PreferencesScreen()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollToAnchorRequested -= OnScrollToAnchorRequested;
        }

        _vm = DataContext as PreferencesScreenViewModel;

        if (_vm is not null)
        {
            _vm.ScrollToAnchorRequested += OnScrollToAnchorRequested;
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

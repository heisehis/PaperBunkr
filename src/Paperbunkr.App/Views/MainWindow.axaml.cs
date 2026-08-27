using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class MainWindow : Window
{
    private WindowNotificationManager? _notificationManager;

    // Close(object content) needs the exact content instance passed to Show(object content, ...) -
    // this maps each live ToastProgressViewModel back to the ToastProgressView control instance
    // actually shown for it, so ProgressToastCloseRequested can close the right one.
    private readonly Dictionary<ToastProgressViewModel, ToastProgressView> _progressToasts = new();

    /// <summary>
    /// Minimize-to-tray (docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-
    /// design.md §4) - always constructed (cheap, stays invisible) rather than lazily, so there's no
    /// "first minimize after enabling the setting" special case to get wrong.
    /// </summary>
    private readonly TrayIconService _trayIconService = new();

    /// <summary>
    /// Set only by <see cref="ExitFromTray"/> - a second guard alongside
    /// <see cref="WindowCloseReason.WindowClosing"/> below (belt and suspenders: if a future
    /// Avalonia version ever reports that reason differently for a programmatic
    /// <c>IClassicDesktopStyleApplicationLifetime.Shutdown()</c>, this still lets the real exit
    /// through instead of trapping the app in an unclosable tray loop).
    /// </summary>
    private bool _allowRealClose;

    /// <summary>
    /// Real bug, found via manual testing: collapsing the nav rail immediately on PointerExited
    /// fought with its own 150ms width-expand animation - hovering "blank" space (no button
    /// underneath to anchor the hit-test, e.g. the empty Grid.Row="1" gap between the two button
    /// groups) made the rail visibly flicker between collapsed/expanded, because the rail's own
    /// width change while the animation is running reflows content under a stationary cursor and
    /// can cause Avalonia's hit-testing to spuriously re-fire PointerExited/PointerEntered mid-
    /// animation. Debouncing the collapse (only actually collapse if the pointer stays away for
    /// 200ms - longer than the 150ms expand animation) absorbs that jitter regardless of its exact
    /// per-frame cause, rather than trusting every raw pointer event.
    /// </summary>
    private readonly DispatcherTimer _railCollapseTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _railCollapseTimer.Tick += OnRailCollapseTimerTick;

        _trayIconService.RestoreRequested += RestoreFromTray;
        _trayIconService.ExitRequested += ExitFromTray;
        PropertyChanged += OnWindowPropertyChanged;
        Closing += OnWindowClosing;
    }

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && IsMinimizeToTrayEnabled())
        {
            MinimizeToTray();
        }
    }

    /// <summary>
    /// Only the plain "user clicked this window's own close button" reason gets redirected to the
    /// tray. <see cref="WindowCloseReason.ApplicationShutdown"/>/<see cref="WindowCloseReason.OSShutdown"/>
    /// (session logoff, <c>desktop.Shutdown()</c>) always pass through - redirecting an OS-initiated
    /// shutdown to the tray would hang the OS waiting for a window that's never going to close.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowRealClose || e.CloseReason != WindowCloseReason.WindowClosing || !IsMinimizeToTrayEnabled())
        {
            return;
        }

        e.Cancel = true;
        MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        _trayIconService.IsVisible = true;

        // The first-time notice must be shown *before* hiding - a toast raised against an
        // already-hidden window would never actually render. Subsequent minimizes hide immediately,
        // same as CE's own balloon-only-once behavior.
        if (ShowFirstTimeTrayNoticeIfNeeded())
        {
            var hideAfterNotice = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hideAfterNotice.Tick += (_, _) =>
            {
                hideAfterNotice.Stop();
                Hide();
            };
            hideAfterNotice.Start();
        }
        else
        {
            Hide();
        }
    }

    private void RestoreFromTray()
    {
        _trayIconService.IsVisible = false;
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void ExitFromTray()
    {
        _allowRealClose = true;
        _trayIconService.IsVisible = false;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }
    }

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - hover-expand is transient UI state, not worth a full binding round-trip; a plain code-behind handler matches this file's existing event-handler pattern (OnWindowClosing etc).</summary>
    private void RailPointerEntered(object? sender, PointerEventArgs e)
    {
        _railCollapseTimer.Stop();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsNavRailHoverExpanded = true;
        }
    }

    /// <summary>Doesn't collapse immediately - see <see cref="_railCollapseTimer"/>'s doc comment.</summary>
    private void RailPointerExited(object? sender, PointerEventArgs e)
    {
        _railCollapseTimer.Stop();
        _railCollapseTimer.Start();
    }

    private void OnRailCollapseTimerTick(object? sender, EventArgs e)
    {
        _railCollapseTimer.Stop();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsNavRailHoverExpanded = false;
        }
    }

    private static bool IsMinimizeToTrayEnabled()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings().MinimizeToTray;
    }

    /// <summary>
    /// Avalonia's <see cref="TrayIcon"/> has no balloon-tip API (checked against
    /// Avalonia.Controls.xml - only Icon/ToolTipText/Menu/IsVisible/Clicked), so this app-level
    /// toast stands in for CE's balloon, shown exactly once ever (the persisted flag itself is the
    /// "don't show again" - there's no separate checkbox to manage, since there's nothing to
    /// re-enable once it's been seen). Returns whether it was actually shown, so
    /// <see cref="MinimizeToTray"/> knows whether to delay the hide.
    /// </summary>
    private bool ShowFirstTimeTrayNoticeIfNeeded()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        if (settings.MinimizeToTrayNoticeShown)
        {
            return false;
        }

        settings.MinimizeToTrayNoticeShown = true;
        context.SaveChanges();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowMinimizeToTrayNotice();
        }

        return true;
    }

    /// <summary>
    /// Toast host (P6 follow-up, docs/alpha-todo.md) - <see cref="WindowNotificationManager"/> needs
    /// a real attached <c>Window</c>, which doesn't exist yet when <see cref="MainViewModel"/> is
    /// constructed (App.axaml.cs builds the ViewModel before the Window). Wired here once
    /// <c>DataContext</c> is actually set, same pattern <see cref="ReaderScreen"/> already uses for
    /// its own post-construction hookup.
    /// </summary>
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _notificationManager ??= new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
        viewModel.ToastRequested += (title, message) =>
            _notificationManager.Show(new Notification(title, message, NotificationType.Success));

        // expiration: TimeSpan.Zero - Avalonia treats a zero/negative expiration as "don't
        // auto-close"; the toast stays up (live-bound, updates as Done/Total change) until the
        // ViewModel explicitly closes it via ProgressToastCloseRequested.
        viewModel.ProgressToastRequested += toastVm =>
        {
            var view = new ToastProgressView { DataContext = toastVm };
            _progressToasts[toastVm] = view;
            _notificationManager.Show(view, NotificationType.Information, expiration: System.TimeSpan.Zero);
        };
        viewModel.ProgressToastCloseRequested += toastVm =>
        {
            if (_progressToasts.Remove(toastVm, out var view))
            {
                _notificationManager.Close(view);
            }
        };
    }
}

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class MainWindow : Window
{
    private WindowNotificationManager? _notificationManager;

    // Close(object content) needs the exact content instance passed to Show(object content, ...) -
    // this maps each live ToastProgressViewModel back to the ToastProgressView control instance
    // actually shown for it, so ProgressToastCloseRequested can close the right one.
    private readonly Dictionary<ToastProgressViewModel, ToastProgressView> _progressToasts = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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

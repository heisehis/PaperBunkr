using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class MainWindow : Window
{
    private WindowNotificationManager? _notificationManager;

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
    }
}

using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>Row wrapper for one <see cref="ActivityAlert"/> in the Activity Center - adds the dismiss / follow-link commands.</summary>
public sealed class ActivityAlertViewModel
{
    public ActivityAlertViewModel(ActivityAlert alert, Action<Guid> dismiss, Action<ActivityLink> followLink)
    {
        Alert = alert;
        // Deferred: this command runs from the row's own ✕ Button.Click still routing through the
        // Alerts row's own ItemsControl. IActivityService.DismissAlert dispatches synchronously
        // when already on the UI thread (ActivityService.DefaultDispatch), which would rebuild
        // Alerts and detach that same row mid-route, crashing Avalonia's detach walk with an
        // ArgumentOutOfRangeException (see Paperbunkr.App.Controls.SuggestBox.Commit for the fully
        // diagnosed case).
        DismissCommand = new RelayCommand(() => Dispatcher.UIThread.Post(() => dismiss(alert.Id)));
        FollowLinkCommand = new RelayCommand(
            () => { if (alert.ActionLink is { } link) followLink(link); },
            () => alert.ActionLink is not null);
    }

    public ActivityAlert Alert { get; }

    public IRelayCommand DismissCommand { get; }

    public IRelayCommand FollowLinkCommand { get; }
}

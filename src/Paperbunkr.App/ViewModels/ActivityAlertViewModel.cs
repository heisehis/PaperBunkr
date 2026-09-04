using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>Row wrapper for one <see cref="ActivityAlert"/> in the Activity Center - adds the dismiss / follow-link commands.</summary>
public sealed class ActivityAlertViewModel
{
    public ActivityAlertViewModel(ActivityAlert alert, Action<Guid> dismiss, Action<ActivityLink> followLink)
    {
        Alert = alert;
        DismissCommand = new RelayCommand(() => dismiss(alert.Id));
        FollowLinkCommand = new RelayCommand(
            () => { if (alert.ActionLink is { } link) followLink(link); },
            () => alert.ActionLink is not null);
    }

    public ActivityAlert Alert { get; }

    public IRelayCommand DismissCommand { get; }

    public IRelayCommand FollowLinkCommand { get; }
}

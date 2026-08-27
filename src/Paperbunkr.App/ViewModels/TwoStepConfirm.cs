using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Shared two-step inline-confirm delete affordance (docs/superpowers/specs/2026-08-22-delete-
/// functionality-design.md) - extracted out of <see cref="MissingFileRowViewModel"/>, the app's
/// original (and until now, only) single-click-destructive real-data delete: first click arms a
/// 3-second confirm window (<see cref="IsArmed"/> flips, <see cref="Label"/> shows
/// <c>armedLabel</c>); a second click within that window fires <c>onConfirmed</c>; letting the
/// window lapse (or any other cancel) reverts silently. No modal - matches the app's existing
/// lightweight interaction style, same rationale <c>MissingFileRowViewModel</c>'s own doc comment
/// already gave for this exact pattern before it was shared. Any row/tile that needs a destructive
/// delete action holds one of these as a property (conventionally named <c>DeleteConfirm</c>) and
/// binds <see cref="Label"/>/<see cref="TriggerCommand"/> (for a text button, e.g. "Remove" →
/// "Confirm remove?") or <see cref="IsArmed"/> (for an icon-only button that just needs to visually
/// change state, e.g. a sidebar row too narrow for label text).
/// </summary>
public partial class TwoStepConfirm : ObservableObject
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly string _idleLabel;
    private readonly string _armedLabel;
    private readonly Action _onConfirmed;
    private DispatcherTimer? _revertTimer;

    public TwoStepConfirm(Action onConfirmed, string idleLabel = "Remove", string armedLabel = "Confirm remove?")
    {
        _onConfirmed = onConfirmed;
        _idleLabel = idleLabel;
        _armedLabel = armedLabel;
        _label = idleLabel;
    }

    [ObservableProperty]
    private string _label;

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(IsArmed));

    /// <summary>True during the confirm window - a second <see cref="TriggerCommand"/> invocation while this is true commits the delete instead of arming it.</summary>
    public bool IsArmed => Label == _armedLabel;

    [RelayCommand]
    private void Trigger()
    {
        if (!IsArmed)
        {
            Label = _armedLabel;
            _revertTimer?.Stop();
            _revertTimer = new DispatcherTimer { Interval = ConfirmWindow };
            _revertTimer.Tick += (_, _) => Cancel();
            _revertTimer.Start();
            return;
        }

        Cancel();
        _onConfirmed();
    }

    /// <summary>Reverts to the idle state without confirming - call when the row itself is going away (e.g. a different action on the same row was used instead) so a stray timer tick can't fire later against a disposed/replaced row.</summary>
    public void Cancel()
    {
        _revertTimer?.Stop();
        _revertTimer = null;
        Label = _idleLabel;
    }
}

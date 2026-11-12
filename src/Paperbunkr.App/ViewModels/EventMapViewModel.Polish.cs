using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>M6b polish (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md): the progress-line toggle and Save as image.</summary>
public sealed partial class EventMapViewModel
{
    /// <summary>Draw the "read so far" line; persisted as <c>AppSettings.EventMapProgressLine</c> (default on).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLineLabel))]
    private bool _showProgressLine = true;

    public string ProgressLineLabel => ShowProgressLine ? "Hide progress line" : "Show progress line";

    /// <summary>Save as image works on the Map and the Matrix (§4).</summary>
    public bool CanSaveImage => HasMap && (IsMapMode || IsMatrixMode);

    /// <summary>Asks the view to render and save the current Map or Matrix as a PNG.</summary>
    public event Action? SaveImageRequested;

    [RelayCommand]
    private void ToggleProgressLine() => ShowProgressLine = !ShowProgressLine;

    [RelayCommand]
    private void SaveAsImage() => SaveImageRequested?.Invoke();

    /// <summary>The export's result goes to the Activity Center (the app's one notification surface).</summary>
    internal void RaiseActivityAlert(Paperbunkr.App.Models.ActivityAlert alert) => _activity.RaiseAlert(alert);

    partial void OnShowProgressLineChanged(bool value)
    {
        if (_applyingRoomSettings)
        {
            return;
        }

        if (!_roomSettingsLoaded)
        {
            _progressLineChangedBeforeLoad = true;
        }
        else
        {
            _ = SaveRoomSettingsAsync();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

/// <summary>An event with no dates, listed under the Atlas's "Undated" menu.</summary>
public sealed record AtlasUndatedEvent(int EventId, string Name);

/// <summary>The continuity Atlas mode (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md §2).</summary>
public sealed partial class EventMapViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndatedEvents), nameof(UndatedLabel), nameof(UndatedEvents))]
    private ContinuityAtlas _atlas = ContinuityAtlas.Empty;

    public bool IsAtlasMode => MapMode == EventMapMode.Atlas;

    /// <summary>The Atlas segment shows on continuity maps only.</summary>
    public bool IsAtlasAvailable => IsContinuityScope;

    public bool HasUndatedEvents => Atlas.Undated.Count > 0;

    public string UndatedLabel => $"Undated · {Atlas.Undated.Count}";

    public IReadOnlyList<AtlasUndatedEvent> UndatedEvents => Atlas.Undated.Select(u => new AtlasUndatedEvent(u.EventId, u.Name)).ToArray();

    /// <summary>Raised to ask the Atlas view to fit the whole span.</summary>
    public event System.Action? FitAtlasRequested;

    [RelayCommand]
    private void ShowAtlasMode() => SetMapMode(EventMapMode.Atlas);

    [RelayCommand]
    private void FitAtlas() => FitAtlasRequested?.Invoke();

    /// <summary>A bar was clicked or activated: open that event on the Continuity screen's Map, as a band click does.</summary>
    public void OpenAtlasEvent(int eventId) => Dispatcher.UIThread.Post(() => _openEventMap(eventId));

    [RelayCommand]
    private void OpenUndatedEvent(int eventId) => OpenAtlasEvent(eventId);

    private void RefreshAtlas()
    {
        if (_continuityData is not { } data)
        {
            Atlas = ContinuityAtlas.Empty;
            return;
        }

        // The loaded rows carry live read state (Mark read patches them), including members a filter keeps off the map.
        var read = data.Events.SelectMany(e => e.Members).Where(m => m.ReadState == EventMapReadState.Read).Select(m => m.IssueId).ToHashSet();
        Atlas = ContinuityAtlasBuilder.Build(data, _hiddenEvents, read.Contains);
    }
}

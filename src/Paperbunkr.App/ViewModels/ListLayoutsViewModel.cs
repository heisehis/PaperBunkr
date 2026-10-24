using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// What a browsing screen gives <see cref="ListLayoutsViewModel"/>: which list is showing, and how to read and write
/// that list's layout. The JSON is the screen's own record (<c>ListLayoutState</c> / <c>BooksWorkspaceState</c>).
/// </summary>
public interface IListLayoutHost
{
    WorkspaceScreen LayoutScreen { get; }

    /// <summary>The list on screen now: <c>all</c>, <c>content:Comic</c>, <c>collection:12</c>.</summary>
    string LayoutSelectionKey { get; }

    string CaptureLayoutJson();

    /// <summary>True when every list on this screen uses one shared saved default instead of per-selection overrides.</summary>
    bool UsesSingleDefault => false;

    /// <summary>The built-in state restored by Reset when this host has one shared default.</summary>
    string BuiltInDefaultLayoutJson => CaptureLayoutJson();

    /// <summary>Applies a layout to the list on screen and re-renders it. Must not call <see cref="ListLayoutsViewModel.TrackChange"/>'s write path (the caller marks it applied).</summary>
    void ApplyLayoutJson(string json);
}

/// <summary>One named layout in the Workspace menu and the Edit Layouts overlay.</summary>
public sealed record ListLayoutRow(int Id, string Name);

/// <summary>
/// The list-layout half of a screen's Workspace menu (docs/superpowers/specs/2026-10-04-list-layouts-design.md): the named
/// layouts, and the bookkeeping that makes every list remember its own layout the way CE's <c>ComicListItem.Display</c>
/// does. One instance per screen; the Edit Layouts overlay binds straight to it.
///
/// Per-list memory works by comparison, not by hooking every property: the screen calls <see cref="TrackChange"/> from
/// its one settings-save path, and the list's row is written only when the captured layout differs from the last one
/// applied or saved for that list.
/// </summary>
public partial class ListLayoutsViewModel : ObservableObject
{
    private readonly IListLayoutHost _host;
    private readonly ListLayoutService _service;
    private readonly Action<Action> _post;
    private string? _lastJson;
    private int _suppress;

    public ListLayoutsViewModel(
        IListLayoutHost host,
        ListLayoutService? service = null,
        Action<string?, Action<string>>? promptForName = null,
        Action<Action>? post = null)
    {
        _host = host;
        _service = service ?? new ListLayoutService();
        PromptForName = promptForName ?? ((_, _) => { });
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        Layouts = new ObservableCollection<ListLayoutRow>();
    }

    public ObservableCollection<ListLayoutRow> Layouts { get; }

    public bool HasLayouts => Layouts.Count > 0;

    public bool HasReorderableLayouts => Layouts.Count > 1;

    public bool UsesSingleDefault => _host.UsesSingleDefault;

    public bool ShowSetOnAllLists => true;

    public string ApplyLayoutLabel => UsesSingleDefault ? "Set as default" : "Use here";

    public string SetOnAllLabel => UsesSingleDefault ? "All series" : "All lists";

    public string SetOnAllTip => UsesSingleDefault ? "Use on every series, including ones with their own layout" : "Set on all lists";

    /// <summary>
    /// Raised after a write that can change which layout the screen should show without going through the screen: set as default,
    /// a series' own layout set or cleared, set on all series, reset. A detail screen re-resolves its series' layout on it.
    /// </summary>
    public event Action? AssignmentsChanged;

    /// <summary>The assignment key of one series' own detail layout (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md, Q8).</summary>
    public static string SeriesKey(int seriesId) => $"series:{seriesId}";

    public bool HasAssignment(string selectionKey) => _service.GetAssignment(_host.LayoutScreen, selectionKey) is not null;

    /// <summary>The named layout a series' own layout is a copy of, or null when it follows the default or no longer matches one.</summary>
    public int? SeriesLayoutId(int seriesId)
    {
        if (_service.GetAssignment(_host.LayoutScreen, SeriesKey(seriesId)) is not { } json)
        {
            return null;
        }

        return _service.ListNamed(_host.LayoutScreen).FirstOrDefault(l => string.Equals(l.StateJson, json, StringComparison.Ordinal))?.Id;
    }

    /// <summary>"Use only for this series": the series gets its own copy of the named layout.</summary>
    public void UseForSeries(int seriesId, int layoutId)
    {
        if (_service.ListNamed(_host.LayoutScreen).FirstOrDefault(l => l.Id == layoutId) is not { } layout)
        {
            return;
        }

        _service.SetAssignment(_host.LayoutScreen, SeriesKey(seriesId), layout.StateJson);
        AssignmentsChanged?.Invoke();
    }

    /// <summary>"Follow the default": drops the series' own layout.</summary>
    public void FollowDefault(int seriesId)
    {
        _service.ClearAssignment(_host.LayoutScreen, SeriesKey(seriesId));
        AssignmentsChanged?.Invoke();
    }

    /// <summary>Makes a named layout the default of a one-default screen (the Workspace popup's detail rows).</summary>
    [RelayCommand]
    private void SetAsDefault(int id)
    {
        if (_service.ListNamed(_host.LayoutScreen).FirstOrDefault(l => l.Id == id) is not { } layout)
        {
            _post(RefreshLayouts);
            return;
        }

        _service.SetAssignment(_host.LayoutScreen, ListLayoutAssignment.DefaultKey, layout.StateJson);
        AssignmentsChanged?.Invoke();
        CloseMenuDeferred();
    }

    /// <summary>Asks for a layout name: (current name or null, what to do with the answer). <c>MainViewModel</c> swaps in the layout-worded prompt.</summary>
    public Action<string?, Action<string>> PromptForName { get; set; }

    /// <summary>Opens the List Options overlay for this screen. Set by <c>MainViewModel</c>.</summary>
    public Action? OpenListOptions { get; set; }

    /// <summary>Opens the Edit Layouts overlay for this screen. Set by <c>MainViewModel</c>.</summary>
    public Action? OpenEditLayouts { get; set; }

    /// <summary>Closes the Workspace popup the menu rows live in. Set by the owning screen.</summary>
    public Action? CloseMenu { get; set; }

    /// <summary>
    /// Asks before "Set on all lists" (CE asks too, <c>HiddenMessageBoxes.SetAllListLayouts</c>) and before a delete:
    /// (message, title, confirm label, is destructive). Null means no question. Set by <c>MainViewModel</c>.
    /// </summary>
    public Func<string, string, string, bool, Task<bool>>? Confirm { get; set; }

    /// <summary>
    /// Called once, at the end of the screen's construction. Captures the screen default the first time it ever runs
    /// (the state the user had before lists could differ), and returns the layout the current list remembers, if any,
    /// for the screen to apply before its first render.
    /// </summary>
    public string? Initialize()
    {
        if (_service.GetAssignment(_host.LayoutScreen, ListLayoutAssignment.DefaultKey) is null)
        {
            _service.SetAssignment(_host.LayoutScreen, ListLayoutAssignment.DefaultKey, _host.CaptureLayoutJson());
        }

        RefreshLayouts();
        return _service.GetAssignment(_host.LayoutScreen, _host.LayoutSelectionKey);
    }

    /// <summary>The layout the list now on screen should show: its own, else the screen default. Null only before <see cref="Initialize"/>.</summary>
    public string? ResolveForSelection() =>
        _service.GetAssignment(_host.LayoutScreen, _host.LayoutSelectionKey)
        ?? _service.GetAssignment(_host.LayoutScreen, ListLayoutAssignment.DefaultKey);

    /// <summary>Records that the screen now shows exactly what is stored for the current list, so the next <see cref="TrackChange"/> has a baseline.</summary>
    public void MarkApplied() => _lastJson = _host.CaptureLayoutJson();

    /// <summary>Runs <paramref name="apply"/> with change tracking off, then takes the result as the baseline.</summary>
    public void ApplyWithoutTracking(Action apply)
    {
        _suppress++;
        try
        {
            apply();
        }
        finally
        {
            _suppress--;
        }

        MarkApplied();
    }

    /// <summary>
    /// The screen's settings-save hook. Writes the current list's own layout when it changed since the baseline; the
    /// first change on a list is what creates its row.
    /// </summary>
    public void TrackChange()
    {
        if (_suppress > 0 || _lastJson is null)
        {
            return;
        }

        string json = _host.CaptureLayoutJson();
        if (string.Equals(json, _lastJson, StringComparison.Ordinal))
        {
            return;
        }

        _lastJson = json;
        _service.SetAssignment(_host.LayoutScreen, _host.LayoutSelectionKey, json);
    }

    public void RefreshLayouts()
    {
        Layouts.Clear();
        foreach (var layout in _service.ListNamed(_host.LayoutScreen))
        {
            Layouts.Add(new ListLayoutRow(layout.Id, layout.Name));
        }

        OnPropertyChanged(nameof(HasLayouts));
        OnPropertyChanged(nameof(HasReorderableLayouts));
    }

    [RelayCommand]
    private void ShowListOptions()
    {
        CloseMenuDeferred();
        OpenListOptions?.Invoke();
    }

    [RelayCommand]
    private void ShowEditLayouts()
    {
        CloseMenuDeferred();
        OpenEditLayouts?.Invoke();
    }

    /// <summary>CE's Save List Layout: names the current list's layout; an existing name is replaced.</summary>
    [RelayCommand]
    private void SaveLayoutAs()
    {
        CloseMenuDeferred();
        PromptForName(null, name =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _service.SaveNamed(_host.LayoutScreen, name, _host.CaptureLayoutJson());
            RefreshLayouts();
        });
    }

    /// <summary>CE's "Set '{name}' Layout": copies the named layout onto the list on screen.</summary>
    [RelayCommand]
    private void ApplyLayout(int id)
    {
        if (_host.UsesSingleDefault)
        {
            SetAsDefault(id);
            return;
        }

        var layout = _service.ListNamed(_host.LayoutScreen).FirstOrDefault(l => l.Id == id);
        if (layout is null)
        {
            _post(RefreshLayouts);
            return;
        }

        ApplyToCurrentList(layout.StateJson);
        CloseMenuDeferred();
    }

    /// <summary>Drops the current list's own layout and shows the screen default again.</summary>
    [RelayCommand]
    private void ResetToDefault()
    {
        if (_host.UsesSingleDefault)
        {
            _service.SetAssignment(_host.LayoutScreen, ListLayoutAssignment.DefaultKey, _host.BuiltInDefaultLayoutJson);
            AssignmentsChanged?.Invoke();
            CloseMenuDeferred();
            return;
        }

        _service.ClearAssignment(_host.LayoutScreen, _host.LayoutSelectionKey);
        if (_service.GetAssignment(_host.LayoutScreen, ListLayoutAssignment.DefaultKey) is { } json)
        {
            ApplyWithoutTracking(() => _host.ApplyLayoutJson(json));
        }

        CloseMenuDeferred();
    }

    [RelayCommand]
    private void RenameLayout(int id)
    {
        var row = Layouts.FirstOrDefault(l => l.Id == id);
        if (row is null)
        {
            return;
        }

        PromptForName(row.Name, name =>
        {
            _service.Rename(id, name);
            RefreshLayouts();
        });
    }

    [RelayCommand]
    private async Task DeleteLayout(int id)
    {
        var row = Layouts.FirstOrDefault(l => l.Id == id);
        if (row is null)
        {
            return;
        }

        if (Confirm is not null
            && !await Confirm($"Delete the layout \"{row.Name}\"? Lists that use it keep their own copy.", "Delete layout", "Delete", true))
        {
            return;
        }

        _service.Delete(id);

        // Deferred: this runs from a button inside the row being removed (CLAUDE.md, "don't remove a control from
        // inside a routed event it's still raising").
        _post(RefreshLayouts);
    }

    [RelayCommand]
    private void MoveLayoutUp(int id) => MoveLayout(id, -1);

    [RelayCommand]
    private void MoveLayoutDown(int id) => MoveLayout(id, +1);

    private void MoveLayout(int id, int delta)
    {
        var ids = Layouts.Select(l => l.Id).ToList();
        int index = ids.IndexOf(id);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return;
        }

        (ids[index], ids[target]) = (ids[target], ids[index]);
        _service.Reorder(_host.LayoutScreen, ids);
        _post(RefreshLayouts);
    }

    /// <summary>CE's "set on all lists": every list on this screen, and any created later, shows this layout.</summary>
    [RelayCommand]
    private async Task SetLayoutOnAllLists(int id)
    {
        var layout = _service.ListNamed(_host.LayoutScreen).FirstOrDefault(l => l.Id == id);
        if (layout is null)
        {
            return;
        }

        if (_host.UsesSingleDefault)
        {
            if (Confirm is not null
                && !await Confirm($"Use the layout \"{layout.Name}\" on every series? Series with their own layout go back to following the default.", "Set on all series", "Set on all series", false))
            {
                return;
            }

            _service.SetOnAllLists(_host.LayoutScreen, layout.StateJson);
            AssignmentsChanged?.Invoke();
            CloseMenuDeferred();
            return;
        }

        if (Confirm is not null
            && !await Confirm($"Use the layout \"{layout.Name}\" for every list? Each list's own layout is replaced.", "Set on all lists", "Set on all lists", false))
        {
            return;
        }

        _service.SetOnAllLists(_host.LayoutScreen, layout.StateJson);
        ApplyWithoutTracking(() => _host.ApplyLayoutJson(layout.StateJson));
    }

    /// <summary>Applies a layout to the list on screen and stores it as that list's own.</summary>
    public void ApplyToCurrentList(string json)
    {
        _suppress++;
        try
        {
            _host.ApplyLayoutJson(json);
        }
        finally
        {
            _suppress--;
        }

        // Stored as captured, not as given: the capture is the normalized form the next comparison is made against.
        _lastJson = _host.CaptureLayoutJson();
        _service.SetAssignment(_host.LayoutScreen, _host.LayoutSelectionKey, _lastJson);
    }

    /// <summary>Stores what the screen shows now as the current list's own layout (a workspace apply: it sets the list and its look together).</summary>
    public void StoreCurrent()
    {
        _lastJson = _host.CaptureLayoutJson();
        _service.SetAssignment(_host.LayoutScreen, _host.LayoutSelectionKey, _lastJson);
    }

    /// <summary>Drops a deleted list's row.</summary>
    public void ForgetList(string selectionKey) => _service.ClearAssignment(_host.LayoutScreen, selectionKey);

    private void CloseMenuDeferred()
    {
        if (CloseMenu is { } close)
        {
            _post(close);
        }
    }
}

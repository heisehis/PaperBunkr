using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.ViewModels.EntityPages;

namespace Paperbunkr.App.ViewModels;

/// <summary>The entity page's tab strip (redesign §3): the kind's tabs in order, empty ones hidden, the lead tab selected on every new entity.</summary>
public partial class MetadataEntityDetailScreenViewModel
{
    private readonly Dictionary<string, EntityPageTab> _tabById = new();

    /// <summary>The kind's tabs, in order (hidden ones included: the view binds IsVisible).</summary>
    [ObservableProperty] private IReadOnlyList<EntityPageTab> _tabs = System.Array.Empty<EntityPageTab>();

    [ObservableProperty] private string _selectedTabId = EntityTabIds.Overview;

    public bool ShowOverviewTab => SelectedTabId == EntityTabIds.Overview;
    public bool ShowAppearancesTab => SelectedTabId is EntityTabIds.Appearances or EntityTabIds.Credits;
    public bool ShowSeriesTab => SelectedTabId == EntityTabIds.Series;
    public bool ShowEventsTab => SelectedTabId == EntityTabIds.Events;
    public bool ShowCareerTab => SelectedTabId == EntityTabIds.Career;
    public bool ShowContinuitiesTab => SelectedTabId == EntityTabIds.Continuities;
    public bool ShowCharactersTab => SelectedTabId == EntityTabIds.Characters;

    partial void OnSelectedTabIdChanged(string value)
    {
        foreach (var tab in _tabById.Values) tab.IsSelected = tab.Id == value;
        foreach (string property in new[] { nameof(ShowOverviewTab), nameof(ShowAppearancesTab), nameof(ShowSeriesTab), nameof(ShowEventsTab), nameof(ShowCareerTab),
            nameof(ShowContinuitiesTab), nameof(ShowCharactersTab) })
            OnPropertyChanged(property);
    }

    // One property per tab for the view's fixed strip (the sliding underline needs the Button.tab controls as direct children of one panel, so
    // the strip is declared in XAML in the order Overview, Career, Appearances, Credits, Series, Events, Continuities, Characters - every kind's
    // order is a subsequence of it). A tab the kind doesn't have is a hidden placeholder.
    private static readonly EntityPageTab Absent = new("absent", string.Empty, _ => { }) { IsVisible = false };
    private EntityPageTab TabOrAbsent(string id) => _tabById.TryGetValue(id, out var tab) ? tab : Absent;
    public EntityPageTab OverviewTab => TabOrAbsent(EntityTabIds.Overview);
    public EntityPageTab CareerTab => TabOrAbsent(EntityTabIds.Career);
    public EntityPageTab AppearancesTab => TabOrAbsent(EntityTabIds.Appearances);
    public EntityPageTab CreditsTab => TabOrAbsent(EntityTabIds.Credits);
    public EntityPageTab SeriesTab => TabOrAbsent(EntityTabIds.Series);
    public EntityPageTab EventsTab => TabOrAbsent(EntityTabIds.Events);
    public EntityPageTab ContinuitiesTab => TabOrAbsent(EntityTabIds.Continuities);
    public EntityPageTab CharactersTab => TabOrAbsent(EntityTabIds.Characters);

    public void SelectTab(string id)
    {
        if (_tabById.TryGetValue(id, out var tab) && tab.IsVisible) SelectedTabId = id;
    }

    private static string TabLabel(string id) => id switch
    {
        EntityTabIds.Overview => "Overview",
        EntityTabIds.Appearances => "Appearances",
        EntityTabIds.Series => "Series",
        EntityTabIds.Events => "Events",
        EntityTabIds.Career => "Career",
        EntityTabIds.Credits => "Credits",
        EntityTabIds.Continuities => "Continuities",
        EntityTabIds.Characters => "Characters",
        _ => id,
    };

    private void BuildTabs()
    {
        _tabById.Clear();
        var tabs = Profile.TabIds.Select(id => new EntityPageTab(id, TabLabel(id), t => SelectTab(t.Id))).ToList();
        foreach (var tab in tabs) _tabById[tab.Id] = tab;
        Tabs = tabs;
        foreach (string property in new[] { nameof(OverviewTab), nameof(CareerTab), nameof(AppearancesTab), nameof(CreditsTab), nameof(SeriesTab), nameof(EventsTab),
            nameof(ContinuitiesTab), nameof(CharactersTab) })
            OnPropertyChanged(property);
        OnSelectedTabIdChanged(SelectedTabId);
    }

    /// <summary>Counts and visibility from the loaded data; falls back to the lead tab when the shown one emptied (a filter change).</summary>
    private void UpdateTabs()
    {
        if (_tabById.Count == 0) BuildTabs();
        foreach (var tab in _tabById.Values)
        {
            (int? count, bool visible) = tab.Id switch
            {
                EntityTabIds.Appearances or EntityTabIds.Credits => ((int?)(_page?.AppearanceCount ?? 0), (_page?.AppearanceCount ?? 0) > 0),
                EntityTabIds.Series => (SeriesCards.Count, SeriesCards.Count > 0 || IsPublisher),
                EntityTabIds.Events => (Events.Count, Events.Count > 0),
                EntityTabIds.Continuities => (Continuities.Count, Continuities.Count > 0),
                EntityTabIds.Characters => (Related.Count, Related.Count > 0),
                _ => ((int?)null, true),
            };
            tab.Count = count;
            tab.IsVisible = visible;
        }
        if (!_tabById.TryGetValue(SelectedTabId, out var shown) || !shown.IsVisible) SelectedTabId = Profile.TabIds[0];
        else OnSelectedTabIdChanged(SelectedTabId);
    }
}

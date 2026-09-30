using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels.Home;

/// <summary>One row of the Home section list in Preferences.</summary>
public sealed partial class HomeSectionRow : ObservableObject
{
    private readonly Action _changed;

    public HomeSectionRow(string key, bool isVisible, Action changed)
    {
        Key = key;
        _isVisible = isVisible;
        _changed = changed;
    }

    public string Key { get; }

    public string Name => HomeSectionKey.DisplayName(Key);

    [ObservableProperty]
    private bool _isVisible;

    partial void OnIsVisibleChanged(bool value) => _changed();
}

/// <summary>One "Not interested" series, listed so it can be unhidden.</summary>
public sealed record HiddenRecommendationRow(int SeriesId, string SeriesName, string DismissedText);

/// <summary>
/// Preferences › Appearance › Home (docs/superpowers/specs/2026-09-28-home-improvements-design.md I1/I5, cosmetics C10): section
/// order and visibility, the seasonal flourish switch, and the hidden-recommendations list. Its own class so the (already very
/// large) <see cref="PreferencesScreenViewModel"/> only gains one property. Every change saves immediately, like Library layouts;
/// Home re-reads it on its next load.
/// </summary>
public sealed partial class HomePreferencesViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private bool _loading;

    public HomePreferencesViewModel(Func<PaperbunkrDbContext> contextFactory) => _contextFactory = contextFactory;

    public ObservableCollection<HomeSectionRow> Sections { get; } = new();

    public ObservableCollection<HiddenRecommendationRow> HiddenRecommendations { get; } = new();

    public bool HasHiddenRecommendations => HiddenRecommendations.Count > 0;

    [ObservableProperty]
    private bool _seasonalFlourish;

    public void Load()
    {
        _loading = true;
        try
        {
            using var context = _contextFactory();
            var settings = context.GetOrCreateAppSettings();
            var layout = HomeLayout.Resolve(settings.HomeSectionOrder, settings.HomeHiddenSections);
            Sections.Clear();
            foreach (string key in layout.Order)
            {
                Sections.Add(new HomeSectionRow(key, !layout.Hidden.Contains(key), SaveLayout));
            }

            SeasonalFlourish = settings.HomeSeasonalFlourish;

            HiddenRecommendations.Clear();
            foreach (var row in DismissedRecommendations.List(context))
            {
                HiddenRecommendations.Add(new HiddenRecommendationRow(row.SeriesId, row.SeriesName,
                    $"Hidden {row.DismissedUtc.ToLocalTime():d MMM yyyy}"));
            }

            OnPropertyChanged(nameof(HasHiddenRecommendations));
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSeasonalFlourishChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        using var context = _contextFactory();
        context.GetOrCreateAppSettings().HomeSeasonalFlourish = value;
        context.SaveChanges();
    }

    [RelayCommand]
    private void MoveUp(HomeSectionRow? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(HomeSectionRow? row) => Move(row, 1);

    private void Move(HomeSectionRow? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        int from = Sections.IndexOf(row);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= Sections.Count)
        {
            return;
        }

        // Deferred: the ↑/↓ buttons live inside the row being moved (CLAUDE.md, routed-event detach rule).
        Dispatcher.UIThread.Post(() =>
        {
            Sections.Move(from, to);
            SaveLayout();
        });
    }

    /// <summary>Drag-reorder drop: puts <paramref name="dragged"/> where <paramref name="target"/> is (before it when moving up,
    /// after it when moving down - the usual list feel).</summary>
    public void MoveTo(HomeSectionRow dragged, HomeSectionRow target)
    {
        int from = Sections.IndexOf(dragged);
        int to = Sections.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        // Deferred: the drop is still routing through the target row's own container.
        Dispatcher.UIThread.Post(() =>
        {
            Sections.Move(from, to);
            SaveLayout();
        });
    }

    [RelayCommand]
    private void ResetLayout()
    {
        _loading = true;
        try
        {
            Sections.Clear();
            foreach (string key in HomeSectionKey.Default)
            {
                Sections.Add(new HomeSectionRow(key, true, SaveLayout));
            }
        }
        finally
        {
            _loading = false;
        }

        SaveLayout();
    }

    private void SaveLayout()
    {
        if (_loading)
        {
            return;
        }

        var (orderCsv, hiddenCsv) = HomeLayout.Serialize(Sections.Select(s => s.Key), Sections.Where(s => !s.IsVisible).Select(s => s.Key));
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.HomeSectionOrder = orderCsv;
        settings.HomeHiddenSections = hiddenCsv;
        context.SaveChanges();
    }

    /// <summary>Unhide: the removal is deferred a tick - the Unhide button lives inside the very row being removed (CLAUDE.md,
    /// routed-event detach rule).</summary>
    [RelayCommand]
    private void Unhide(HiddenRecommendationRow? row)
    {
        if (row is null)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            DismissedRecommendations.Restore(context, row.SeriesId);
        }

        Dispatcher.UIThread.Post(() =>
        {
            HiddenRecommendations.Remove(row);
            OnPropertyChanged(nameof(HasHiddenRecommendations));
        });
    }
}

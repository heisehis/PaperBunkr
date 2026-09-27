using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// One group of exclude conditions in an organizer profile: a set of conditions and child groups combined with "all of" or "any of" - the
/// full nested AND/OR shape the Smart List engine (and the plugin's exclude rules, `locommon.py:582-630`) understand. Groups nest to any depth;
/// the profile editor renders them recursively. <see cref="RootGroup"/>-level is the profile's own rule (no remove button).
/// </summary>
public sealed partial class ExcludeGroupViewModel : ObservableObject
{
    private readonly Action<ExcludeGroupViewModel>? _onRemove;

    public ExcludeGroupViewModel(bool matchAny, Action<ExcludeGroupViewModel>? onRemove = null)
    {
        _matchAny = matchAny;
        _onRemove = onRemove;
    }

    public static IReadOnlyList<string> ModeNames { get; } = new[] { "Any", "All" };

    /// <summary>True: a book matching ANY entry of the group matches the group (the plugin's default); false: it must match ALL of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText))]
    private bool _matchAny;

    /// <summary>"Any" or "All", as text for the suggest box (the app has no ComboBox). An unknown value leaves the mode unchanged.</summary>
    public string ModeText
    {
        get => MatchAny ? "Any" : "All";
        set
        {
            if (string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase))
            {
                MatchAny = true;
            }
            else if (string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
            {
                MatchAny = false;
            }
        }
    }

    public ObservableCollection<ExcludeConditionRowViewModel> Conditions { get; } = new();

    public ObservableCollection<ExcludeGroupViewModel> Groups { get; } = new();

    /// <summary>The profile's own top-level group cannot be removed.</summary>
    public bool IsRoot => _onRemove is null;

    /// <summary>No conditions anywhere below: this group excludes nothing and is dropped when the profile is saved.</summary>
    public bool IsEmpty => Conditions.Count == 0 && Groups.All(g => g.IsEmpty);

    [RelayCommand]
    private void AddCondition() =>
        Conditions.Add(NewCondition(SmartListField.SeriesName, SmartListOperator.Contains, string.Empty, false));

    [RelayCommand]
    private void AddGroup() => Groups.Add(new ExcludeGroupViewModel(matchAny: false, RemoveChild));

    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    public ExcludeConditionRowViewModel NewCondition(SmartListField field, SmartListOperator op, string value, bool not) =>
        new(field, op, value, not, RemoveCondition);

    // Removing a row from inside its own click would detach the very control still routing the event (see CLAUDE.md's runtime gotcha), so both
    // removals wait one dispatcher tick.
    private void RemoveCondition(ExcludeConditionRowViewModel row) => Dispatcher.UIThread.Post(() => Conditions.Remove(row));

    private void RemoveChild(ExcludeGroupViewModel group) => Dispatcher.UIThread.Post(() => Groups.Remove(group));

    /// <summary>Builds the group the rules engine evaluates: empty child groups are left out.</summary>
    public PluginConditionGroup ToGroup() => new(
        MatchAny ? SmartListGroupMode.Or : SmartListGroupMode.And,
        Conditions.Select(c => c.ToCondition()).ToList(),
        Groups.Where(g => !g.IsEmpty).Select(g => g.ToGroup()).ToList());

    /// <summary>The editor's view of a saved rule, at any depth.</summary>
    public static ExcludeGroupViewModel From(PluginConditionGroup rule, Action<ExcludeGroupViewModel>? onRemove = null)
    {
        var group = new ExcludeGroupViewModel(rule.Mode == SmartListGroupMode.Or, onRemove);
        foreach (PluginCondition condition in rule.Conditions)
        {
            group.Conditions.Add(group.NewCondition(condition.Field, condition.Op, condition.Value, condition.Not));
        }

        foreach (PluginConditionGroup child in rule.ChildGroups ?? Array.Empty<PluginConditionGroup>())
        {
            group.Groups.Add(From(child, group.RemoveChild));
        }

        return group;
    }
}

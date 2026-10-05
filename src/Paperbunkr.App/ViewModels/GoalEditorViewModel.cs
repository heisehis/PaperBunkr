using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The create dialog for a reading goal (docs/superpowers/specs/2026-09-23-insights-reading-goals-design.md, extended by
/// 2026-10-04-insights-goal-scopes-design.md): a <b>Count</b> goal (N issues/pages in a period) or a <b>Finish</b> goal (finish everything in a reading
/// list / collection / story event / continuity - target is the scope's size, deadline optional), with any number of scope rows that must all match.
/// Create-only (no in-place edit, per the original design); Renew and the "Set a goal" actions pre-fill it. Option lists (<see cref="MetricOptions"/> etc.)
/// mirror <see cref="StatsScreenViewModel.GrowthMeasureOptions"/>'s "list of IsActive-carrying option objects" shape rather than an enum-to-RadioButton converter.
/// </summary>
public partial class GoalEditorViewModel : ViewModelBase
{
    /// <summary>Scope kinds a Finish goal may use - the ones with a countable set of items behind them. Count goals may use any kind.</summary>
    public static readonly IReadOnlyList<GoalScopeKind> FinishScopeKinds = new[]
    {
        GoalScopeKind.ReadingList, GoalScopeKind.Collection, GoalScopeKind.StoryEvent, GoalScopeKind.Continuity, GoalScopeKind.Creator,
    };

    public static readonly IReadOnlyList<GoalScopeKind> CountScopeKinds = new[]
    {
        GoalScopeKind.Series, GoalScopeKind.Publisher, GoalScopeKind.Genre, GoalScopeKind.ReadingList, GoalScopeKind.Collection,
        GoalScopeKind.StoryEvent, GoalScopeKind.Continuity, GoalScopeKind.Creator, GoalScopeKind.MediaType,
    };

    public static readonly IReadOnlyList<string> MediaTypes = new[] { "Comic", "Manga", "Manhwa", "Manhua", "Novel" };

    private readonly Action _onSaved;
    private readonly Action _onCancel;
    private bool _titleManuallyEdited;
    private bool _settingTitleProgrammatically;

    // Names a user can pick for each kind, and the id behind each name (first one wins when two share a name).
    private readonly Dictionary<GoalScopeKind, IReadOnlyList<string>> _names = new();
    private readonly Dictionary<GoalScopeKind, Dictionary<string, string>> _idByName = new();
    private readonly Dictionary<GoalScopeKind, Dictionary<string, string>> _nameById = new();

    public GoalEditorViewModel(Action onSaved, Action onCancel)
    {
        _onSaved = onSaved;
        _onCancel = onCancel;

        using var context = PaperbunkrDb.CreateContext();
        LoadIdKind(GoalScopeKind.Series, context.Series.Select(s => new KeyValuePair<int, string>(s.Id, s.Name)).ToList());
        LoadIdKind(GoalScopeKind.ReadingList, context.ReadingLists.Select(l => new KeyValuePair<int, string>(l.Id, l.Name)).ToList());
        LoadIdKind(GoalScopeKind.Collection, context.Collections.Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList());
        LoadIdKind(GoalScopeKind.StoryEvent, context.StoryEvents.Select(e => new KeyValuePair<int, string>(e.Id, e.Name)).ToList());
        LoadIdKind(GoalScopeKind.Continuity, context.Continuities.Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList());
        LoadIdKind(GoalScopeKind.Creator, context.Creators.Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList());
        _names[GoalScopeKind.Publisher] = context.Issues.Select(i => i.Publisher).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct().OrderBy(p => p).Select(p => p!).ToList();
        _names[GoalScopeKind.Genre] = context.IssueTags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value)
            .Distinct().OrderBy(g => g).ToList();
        _names[GoalScopeKind.MediaType] = MediaTypes;

        PeriodOptions = new ObservableCollection<GoalPeriodOption>();
        ScopeRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WholeLibrary));
            OnPropertyChanged(nameof(CanCreate));
        };
        Reset();
    }

    /// <summary>No scope rows: the goal covers the whole library (shown as such in the editor).</summary>
    public bool WholeLibrary => ScopeRows.Count == 0;

    private void LoadIdKind(GoalScopeKind kind, List<KeyValuePair<int, string>> rows)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, string>();
        foreach (var (id, name) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).OrderBy(r => r.Key))
        {
            byName.TryAdd(name.Trim(), id.ToString());
            byId[id.ToString()] = name.Trim();
        }

        _idByName[kind] = byName;
        _nameById[kind] = byId;
        _names[kind] = byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Resets every field to its default state - called each time the overlay opens.</summary>
    public void Reset()
    {
        Kind = GoalKind.Count;
        Metric = GoalMetric.Items;
        TargetText = string.Empty;
        PeriodKind = GoalPeriodKind.ThisYear;
        CustomStart = null;
        CustomEnd = null;
        DistinctOnly = false;
        ScopeRows.Clear();
        _titleManuallyEdited = false;
        RebuildOptions();
        UpdateSuggestedTitle();
        NotifyShape();
    }

    /// <summary>The "Set a goal" quick-create path (reading list / collection / ... page): a Finish goal with one scope row already filled in.
    /// Returns false (form left at its defaults) when the entity is not one a Finish goal can target.</summary>
    public bool StartFinishGoal(GoalScopeKind kind, int id)
    {
        Reset();
        if (!FinishScopeKinds.Contains(kind) || !_nameById.TryGetValue(kind, out var names) || !names.TryGetValue(id.ToString(), out string? name))
        {
            return false;
        }

        SetKind(GoalKind.Finish);
        ScopeRows.Clear(); // SetKind leaves one blank row to fill in; this path supplies its own
        ScopeRows.Add(NewRow(kind, name));
        UpdateSuggestedTitle();
        NotifyShape();
        return true;
    }

    /// <summary>The "Renew" path for an ended goal (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md): resets the
    /// form, then copies kind, metric, target, count-each-once, period kind and every scope from <paramref name="goalId"/>. The period itself is not copied - a preset
    /// resolves from today as for any new goal, and a Custom range keeps its length but starts today. Nothing is saved until Create runs, and the
    /// title is left to the auto-suggestion. Returns false (form left at its defaults) when the goal no longer exists.</summary>
    public bool LoadFrom(int goalId)
    {
        Reset();

        using var context = PaperbunkrDb.CreateContext();
        var goal = context.ReadingGoals.Include(g => g.Scopes).AsNoTracking().FirstOrDefault(g => g.Id == goalId);
        if (goal is null)
        {
            return false;
        }

        SetKind(goal.Kind);
        ScopeRows.Clear(); // SetKind leaves one blank row for a Finish goal; the goal's own scopes replace it
        SetMetric(goal.Metric);
        TargetText = goal.Kind == GoalKind.Finish ? string.Empty : goal.Target.ToString();
        DistinctOnly = goal.DistinctOnly;
        SetPeriod(goal.PeriodKind);
        if (goal.PeriodKind == GoalPeriodKind.Custom)
        {
            int length = Math.Max(1, goal.PeriodEnd.DayNumber - goal.PeriodStart.DayNumber);
            CustomStart = DateTime.Today;
            CustomEnd = DateTime.Today.AddDays(length);
        }

        foreach (var (kind, value, label) in Paperbunkr.Data.Metadata.GoalResolver.FiltersOf(goal))
        {
            string? text = kind is GoalScopeKind.Publisher or GoalScopeKind.Genre or GoalScopeKind.MediaType
                ? value
                : value is not null && _nameById.TryGetValue(kind, out var names) && names.TryGetValue(value, out string? current) ? current : label;
            if (!string.IsNullOrWhiteSpace(text))
            {
                ScopeRows.Add(NewRow(kind, text));
            }
        }

        UpdateSuggestedTitle();
        NotifyShape();
        return true;
    }

    public ObservableCollection<GoalScopeRowViewModel> ScopeRows { get; } = new();

    public IReadOnlyList<GoalKindOption> KindOptions { get; } = new[]
    {
        new GoalKindOption(GoalKind.Count, "Count", "Read N issues or pages in a period"),
        new GoalKindOption(GoalKind.Finish, "Finish", "Finish everything in a list, collection, event or continuity"),
    };

    public IReadOnlyList<GoalMetricOption> MetricOptions { get; } = new[]
    {
        new GoalMetricOption(GoalMetric.Items, "Issues"),
        new GoalMetricOption(GoalMetric.Pages, "Pages"),
    };

    /// <summary>The periods on offer: a Finish goal also gets "No deadline".</summary>
    public ObservableCollection<GoalPeriodOption> PeriodOptions { get; }

    /// <summary>The scope kinds a row's dropdown offers for the current goal kind, as labels.</summary>
    public IReadOnlyList<string> ScopeKindLabels
        => (Kind == GoalKind.Finish ? FinishScopeKinds : CountScopeKinds).Select(ScopeKindLabel).ToList();

    public static string ScopeKindLabel(GoalScopeKind kind) => kind switch
    {
        GoalScopeKind.Series => "Series",
        GoalScopeKind.Publisher => "Publisher",
        GoalScopeKind.Genre => "Genre",
        GoalScopeKind.ReadingList => "Reading list",
        GoalScopeKind.Collection => "Collection",
        GoalScopeKind.StoryEvent => "Story event",
        GoalScopeKind.Continuity => "Continuity",
        GoalScopeKind.Creator => "Creator",
        GoalScopeKind.MediaType => "Media type",
        _ => "Whole library",
    };

    internal static GoalScopeKind? ScopeKindFromLabel(string? label)
        => CountScopeKinds.Cast<GoalScopeKind?>().FirstOrDefault(k => string.Equals(ScopeKindLabel(k!.Value), label?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>What a row's picker suggests for <paramref name="kind"/>.</summary>
    internal IReadOnlyList<string> NamesFor(GoalScopeKind kind) => _names.GetValueOrDefault(kind, Array.Empty<string>());

    /// <summary>The (value, label) a row stores for <paramref name="text"/>, or null when it does not name something that exists.</summary>
    internal (string Value, string Label)? Resolve(GoalScopeKind kind, string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        switch (kind)
        {
            case GoalScopeKind.Publisher:
            case GoalScopeKind.Genre:
                return (trimmed, trimmed);
            case GoalScopeKind.MediaType:
                string? media = MediaTypes.FirstOrDefault(m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase));
                return media is null ? null : (media, media);
            default:
                return _idByName.TryGetValue(kind, out var ids) && ids.TryGetValue(trimmed, out string? id) ? (id, _nameById[kind][id]) : null;
        }
    }

    private GoalScopeRowViewModel NewRow(GoalScopeKind kind, string text)
    {
        var row = new GoalScopeRowViewModel(this, kind, text);
        row.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanCreate));
            UpdateSuggestedTitle();
        };
        return row;
    }

    [RelayCommand]
    private void AddScope()
    {
        var first = (Kind == GoalKind.Finish ? FinishScopeKinds : CountScopeKinds)[0];
        ScopeRows.Add(NewRow(first, string.Empty));
        OnPropertyChanged(nameof(CanCreate));
    }

    [RelayCommand]
    private void RemoveScope(GoalScopeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // The Remove button lives inside the row it removes: deferring one dispatcher tick keeps the row's visual tree from being detached while
        // that very Click is still routing (the CLAUDE.md "don't remove a control from inside the routed event it's raising" rule).
        Dispatcher.UIThread.Post(() =>
        {
            ScopeRows.Remove(row);
            OnPropertyChanged(nameof(CanCreate));
            UpdateSuggestedTitle();
        });
    }

    [RelayCommand]
    private void SetKind(GoalKind value)
    {
        Kind = value;
        if (value == GoalKind.Finish)
        {
            Metric = GoalMetric.Items;
            DistinctOnly = false;
            foreach (var stale in ScopeRows.Where(r => !FinishScopeKinds.Contains(r.Kind)).ToList())
            {
                ScopeRows.Remove(stale); // a series/publisher/genre/media-type row has no countable set behind it
            }

            if (ScopeRows.Count == 0)
            {
                ScopeRows.Add(NewRow(FinishScopeKinds[0], string.Empty));
            }

            PeriodKind = GoalPeriodKind.NoDeadline;
        }
        else if (PeriodKind == GoalPeriodKind.NoDeadline)
        {
            PeriodKind = GoalPeriodKind.ThisYear;
        }

        RebuildOptions();
        UpdateSuggestedTitle();
        NotifyShape();
    }

    [RelayCommand]
    private void SetMetric(GoalMetric value)
    {
        Metric = value;
        foreach (var o in MetricOptions) o.IsActive = o.Value == value;
        UpdateSuggestedTitle();
    }

    [RelayCommand]
    private void SetPeriod(GoalPeriodKind value)
    {
        PeriodKind = value;
        foreach (var o in PeriodOptions) o.IsActive = o.Value == value;
        OnPropertyChanged(nameof(IsCustomPeriod));
        OnPropertyChanged(nameof(CanCreate));
        UpdateSuggestedTitle();
    }

    private void RebuildOptions()
    {
        foreach (var o in KindOptions) o.IsActive = o.Value == Kind;
        foreach (var o in MetricOptions) o.IsActive = o.Value == Metric;

        PeriodOptions.Clear();
        PeriodOptions.Add(new GoalPeriodOption(GoalPeriodKind.ThisYear, "This year"));
        PeriodOptions.Add(new GoalPeriodOption(GoalPeriodKind.ThisMonth, "This month"));
        PeriodOptions.Add(new GoalPeriodOption(GoalPeriodKind.Custom, "Custom range"));
        if (Kind == GoalKind.Finish)
        {
            PeriodOptions.Add(new GoalPeriodOption(GoalPeriodKind.NoDeadline, "No deadline"));
        }

        foreach (var o in PeriodOptions) o.IsActive = o.Value == PeriodKind;
    }

    private void NotifyShape()
    {
        OnPropertyChanged(nameof(IsFinishGoal));
        OnPropertyChanged(nameof(IsCountGoal));
        OnPropertyChanged(nameof(ScopeKindLabels));
        OnPropertyChanged(nameof(IsCustomPeriod));
        OnPropertyChanged(nameof(ScopeSectionHint));
        OnPropertyChanged(nameof(CanCreate));
        foreach (var row in ScopeRows)
        {
            row.RefreshKinds(); // each row's dropdown follows the goal kind
        }
    }

    [ObservableProperty]
    private GoalKind _kind;

    [ObservableProperty]
    private GoalMetric _metric;

    [ObservableProperty]
    private string _targetText = string.Empty;

    partial void OnTargetTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        UpdateSuggestedTitle();
    }

    [ObservableProperty]
    private GoalPeriodKind _periodKind;

    /// <summary>Count goals only: count each issue once, so a re-read does not count again.</summary>
    [ObservableProperty]
    private bool _distinctOnly;

    // DateTime?, not DateTimeOffset? - CalendarDatePicker.SelectedDate is DateTime? (it wraps a Calendar
    // control internally, which has always used DateTime? in both WPF and Avalonia), unlike DatePicker's
    // own DateTimeOffset?. Confirmed by the real runtime binding exception this produced when it was still
    // typed DateTimeOffset? here, not assumed from the docs prose alone (which claims DateTimeOffset? and
    // is wrong, at least for the Avalonia version this project is on).
    [ObservableProperty]
    private DateTime? _customStart;

    partial void OnCustomStartChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(CanCreate));
        UpdateSuggestedTitle();
    }

    [ObservableProperty]
    private DateTime? _customEnd;

    partial void OnCustomEndChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(CanCreate));
        UpdateSuggestedTitle();
    }

    [ObservableProperty]
    private string _title = string.Empty;

    partial void OnTitleChanged(string value)
    {
        if (!_settingTitleProgrammatically)
        {
            _titleManuallyEdited = true;
        }
    }

    public bool IsFinishGoal => Kind == GoalKind.Finish;

    public bool IsCountGoal => Kind == GoalKind.Count;

    public bool IsCustomPeriod => PeriodKind == GoalPeriodKind.Custom;

    public string ScopeSectionHint => Kind == GoalKind.Finish
        ? "What to finish. A goal finishes when every issue in all of these has been read."
        : "Optional. Every row has to match, e.g. Publisher + Reading list.";

    public bool CanCreate
    {
        get
        {
            if (ScopeRows.Any(r => !r.IsValid) || (Kind == GoalKind.Finish && ScopeRows.Count == 0))
            {
                return false;
            }

            if (Kind == GoalKind.Count && !(long.TryParse(TargetText, out long target) && target > 0))
            {
                return false;
            }

            return !IsCustomPeriod || (CustomStart is not null && CustomEnd is not null && CustomEnd > CustomStart);
        }
    }

    private void UpdateSuggestedTitle()
    {
        if (_titleManuallyEdited)
        {
            return;
        }

        string? scope = ScopeRows.Select(r => r.ValueText.Trim()).FirstOrDefault(t => t.Length > 0);
        string period = PeriodKind switch
        {
            GoalPeriodKind.ThisYear => " this year",
            GoalPeriodKind.ThisMonth => " this month",
            GoalPeriodKind.Custom when CustomEnd is { } end => $" by {end:MMM d, yyyy}",
            GoalPeriodKind.Custom => " by a set date",
            _ => string.Empty,
        };

        string text;
        if (Kind == GoalKind.Finish)
        {
            text = $"Finish {scope ?? "…"}{period}";
        }
        else
        {
            string unit = Metric == GoalMetric.Items ? "issues" : "pages";
            string target = long.TryParse(TargetText, out long t) ? t.ToString("N0") : "?";
            text = $"Read {target} {unit}{(scope is null ? string.Empty : $" from {scope}")}{period}";
        }

        _settingTitleProgrammatically = true;
        Title = text;
        _settingTitleProgrammatically = false;
    }

    [RelayCommand]
    private void Create()
    {
        if (!CanCreate)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly periodStart, periodEnd;
        switch (PeriodKind)
        {
            case GoalPeriodKind.ThisYear:
                periodStart = new DateOnly(today.Year, 1, 1);
                periodEnd = new DateOnly(today.Year, 12, 31);
                break;
            case GoalPeriodKind.ThisMonth:
                periodStart = new DateOnly(today.Year, today.Month, 1);
                periodEnd = periodStart.AddMonths(1).AddDays(-1);
                break;
            case GoalPeriodKind.NoDeadline:
                periodStart = today;
                periodEnd = DateOnly.MaxValue;
                break;
            default:
                periodStart = DateOnly.FromDateTime(CustomStart!.Value.Date);
                periodEnd = DateOnly.FromDateTime(CustomEnd!.Value.Date);
                break;
        }

        var scopes = new List<ReadingGoalScope>();
        foreach (var row in ScopeRows)
        {
            if (row.Resolved is not { } resolved)
            {
                return; // fail closed rather than save a goal that will never match anything
            }

            scopes.Add(new ReadingGoalScope { Kind = row.Kind, Value = resolved.Value, Label = resolved.Label });
        }

        bool finish = Kind == GoalKind.Finish;
        using var context = PaperbunkrDb.CreateContext();
        context.ReadingGoals.Add(new ReadingGoal
        {
            Title = string.IsNullOrWhiteSpace(Title) ? "Reading goal" : Title.Trim(),
            Kind = Kind,
            Metric = finish ? GoalMetric.Items : Metric,
            Target = finish ? 0 : long.Parse(TargetText), // a finish goal's target is the live size of its scope
            DistinctOnly = !finish && DistinctOnly,
            PeriodKind = PeriodKind,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ScopeKind = GoalScopeKind.Library, // the legacy single-scope pair stays unused; the rows below are the scope
            Scopes = scopes,
            CreatedUtc = DateTime.UtcNow,
        });
        context.SaveChanges();

        _onSaved();
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

/// <summary>One scope row in the goal editor: a kind (dropdown) and the thing it points at (a picker).</summary>
public sealed partial class GoalScopeRowViewModel : ObservableObject
{
    private readonly GoalEditorViewModel _owner;
    private bool _syncing;

    internal GoalScopeRowViewModel(GoalEditorViewModel owner, GoalScopeKind kind, string valueText)
    {
        _owner = owner;
        _syncing = true;
        Kind = kind;
        KindLabel = GoalEditorViewModel.ScopeKindLabel(kind);
        ValueText = valueText;
        _syncing = false;
    }

    public GoalScopeKind Kind { get; private set; }

    [ObservableProperty]
    private string _kindLabel = string.Empty;

    partial void OnKindLabelChanged(string value)
    {
        if (_syncing || GoalEditorViewModel.ScopeKindFromLabel(value) is not { } kind || kind == Kind)
        {
            return;
        }

        Kind = kind;
        ValueText = string.Empty; // a name from the old kind means nothing under the new one
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Suggestions));
        OnPropertyChanged(nameof(Watermark));
        OnPropertyChanged(nameof(IsValid));
    }

    [ObservableProperty]
    private string _valueText = string.Empty;

    partial void OnValueTextChanged(string value) => OnPropertyChanged(nameof(IsValid));

    /// <summary>The kinds this row's dropdown offers (they follow the editor's goal kind).</summary>
    public IReadOnlyList<string> KindLabels => _owner.ScopeKindLabels;

    internal void RefreshKinds() => OnPropertyChanged(nameof(KindLabels));

    public IReadOnlyList<string> Suggestions => _owner.NamesFor(Kind);

    public string Watermark => Kind switch
    {
        GoalScopeKind.Series => "Series name",
        GoalScopeKind.Publisher => "Publisher",
        GoalScopeKind.Genre => "Genre",
        GoalScopeKind.ReadingList => "Reading list name",
        GoalScopeKind.Collection => "Collection name",
        GoalScopeKind.StoryEvent => "Story event name",
        GoalScopeKind.Continuity => "Continuity name",
        GoalScopeKind.Creator => "Creator name",
        GoalScopeKind.MediaType => "Comic, Manga, Novel…",
        _ => string.Empty,
    };

    internal (string Value, string Label)? Resolved => _owner.Resolve(Kind, ValueText);

    /// <summary>True once the picker names something that exists (a blank row is not valid: it would silently mean "whole library").</summary>
    public bool IsValid => Resolved is not null;
}

public sealed partial class GoalKindOption : ObservableObject
{
    public GoalKindOption(GoalKind value, string label, string description)
    {
        Value = value;
        Label = label;
        Description = description;
    }

    public GoalKind Value { get; }

    public string Label { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool _isActive;
}

public sealed partial class GoalMetricOption : ObservableObject
{
    public GoalMetricOption(GoalMetric value, string label)
    {
        Value = value;
        Label = label;
    }

    public GoalMetric Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

public sealed partial class GoalPeriodOption : ObservableObject
{
    public GoalPeriodOption(GoalPeriodKind value, string label)
    {
        Value = value;
        Label = label;
    }

    public GoalPeriodKind Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The create dialog for a reading goal (docs/superpowers/specs/2026-09-23-insights-reading-goals-
/// design.md) - same create-only shape as <see cref="NewEventOrContinuityViewModel"/>, minus edit support
/// (out of scope for v1 per the design doc). Option lists (<see cref="MetricOptions"/> etc.) mirror
/// <see cref="StatsScreenViewModel.GrowthMeasureOptions"/>'s own "list of IsActive-carrying option objects"
/// shape rather than introducing an enum-to-RadioButton converter this codebase doesn't otherwise have.
/// </summary>
public partial class GoalEditorViewModel : ViewModelBase
{
    private readonly Action _onSaved;
    private readonly Action _onCancel;
    private bool _titleManuallyEdited;
    private bool _settingTitleProgrammatically;

    public GoalEditorViewModel(Action onSaved, Action onCancel)
    {
        _onSaved = onSaved;
        _onCancel = onCancel;

        using var context = PaperbunkrDb.CreateContext();
        AllSeriesNames = context.Series.Select(s => s.Name).Distinct().OrderBy(n => n).ToList();
        AllPublishers = context.Issues.Select(i => i.Publisher).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct().OrderBy(p => p).Select(p => p!).ToList();
        AllGenres = context.IssueTags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value)
            .Distinct().OrderBy(g => g).ToList();
    }

    /// <summary>Resets every field to its default state - called each time the overlay opens.</summary>
    public void Reset()
    {
        Metric = GoalMetric.Items;
        TargetText = string.Empty;
        PeriodKind = GoalPeriodKind.ThisYear;
        CustomStart = null;
        CustomEnd = null;
        ScopeKind = GoalScopeKind.Library;
        ScopeSeriesName = string.Empty;
        ScopePublisher = string.Empty;
        ScopeGenre = string.Empty;
        _titleManuallyEdited = false;
        UpdateSuggestedTitle();

        foreach (var o in MetricOptions) o.IsActive = o.Value == Metric;
        foreach (var o in PeriodOptions) o.IsActive = o.Value == PeriodKind;
        foreach (var o in ScopeOptions) o.IsActive = o.Value == ScopeKind;
    }

    public IReadOnlyList<string> AllSeriesNames { get; }

    public IReadOnlyList<string> AllPublishers { get; }

    public IReadOnlyList<string> AllGenres { get; }

    public IReadOnlyList<GoalMetricOption> MetricOptions { get; } = new[]
    {
        new GoalMetricOption(GoalMetric.Items, "Issues"),
        new GoalMetricOption(GoalMetric.Pages, "Pages"),
    };

    public IReadOnlyList<GoalPeriodOption> PeriodOptions { get; } = new[]
    {
        new GoalPeriodOption(GoalPeriodKind.ThisYear, "This year"),
        new GoalPeriodOption(GoalPeriodKind.ThisMonth, "This month"),
        new GoalPeriodOption(GoalPeriodKind.Custom, "Custom range"),
    };

    public IReadOnlyList<GoalScopeOption> ScopeOptions { get; } = new[]
    {
        new GoalScopeOption(GoalScopeKind.Library, "Whole library"),
        new GoalScopeOption(GoalScopeKind.Series, "One series"),
        new GoalScopeOption(GoalScopeKind.Publisher, "One publisher"),
        new GoalScopeOption(GoalScopeKind.Genre, "One genre"),
    };

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
        UpdateSuggestedTitle();
    }

    [RelayCommand]
    private void SetScope(GoalScopeKind value)
    {
        ScopeKind = value;
        foreach (var o in ScopeOptions) o.IsActive = o.Value == value;
        OnPropertyChanged(nameof(IsSeriesScope));
        OnPropertyChanged(nameof(IsPublisherScope));
        OnPropertyChanged(nameof(IsGenreScope));
    }

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
    private GoalScopeKind _scopeKind;

    [ObservableProperty]
    private string _scopeSeriesName = string.Empty;

    partial void OnScopeSeriesNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    [ObservableProperty]
    private string _scopePublisher = string.Empty;

    partial void OnScopePublisherChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    [ObservableProperty]
    private string _scopeGenre = string.Empty;

    partial void OnScopeGenreChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    [ObservableProperty]
    private string _title = string.Empty;

    partial void OnTitleChanged(string value)
    {
        if (!_settingTitleProgrammatically)
        {
            _titleManuallyEdited = true;
        }
    }

    public bool IsCustomPeriod => PeriodKind == GoalPeriodKind.Custom;

    public bool IsSeriesScope => ScopeKind == GoalScopeKind.Series;

    public bool IsPublisherScope => ScopeKind == GoalScopeKind.Publisher;

    public bool IsGenreScope => ScopeKind == GoalScopeKind.Genre;

    public bool CanCreate => long.TryParse(TargetText, out long target) && target > 0
        && (!IsCustomPeriod || (CustomStart is not null && CustomEnd is not null && CustomEnd > CustomStart))
        && (!IsSeriesScope || !string.IsNullOrWhiteSpace(ScopeSeriesName))
        && (!IsPublisherScope || !string.IsNullOrWhiteSpace(ScopePublisher))
        && (!IsGenreScope || !string.IsNullOrWhiteSpace(ScopeGenre));

    private void UpdateSuggestedTitle()
    {
        if (_titleManuallyEdited)
        {
            return;
        }

        string unit = Metric == GoalMetric.Items ? "issues" : "pages";
        string target = long.TryParse(TargetText, out long t) ? t.ToString("N0") : "?";
        string period = PeriodKind switch
        {
            GoalPeriodKind.ThisYear => "this year",
            GoalPeriodKind.ThisMonth => "this month",
            GoalPeriodKind.Custom when CustomEnd is { } end => $"by {end:MMM d, yyyy}",
            _ => "by a set date",
        };

        _settingTitleProgrammatically = true;
        Title = $"Read {target} {unit} {period}";
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
            default:
                periodStart = DateOnly.FromDateTime(CustomStart!.Value.Date);
                periodEnd = DateOnly.FromDateTime(CustomEnd!.Value.Date);
                break;
        }

        using var context = PaperbunkrDb.CreateContext();
        string? scopeValue = ScopeKind switch
        {
            GoalScopeKind.Series => context.Series.FirstOrDefault(s => s.Name == ScopeSeriesName.Trim())?.Id.ToString(),
            GoalScopeKind.Publisher => ScopePublisher.Trim(),
            GoalScopeKind.Genre => ScopeGenre.Trim(),
            _ => null,
        };

        if (ScopeKind == GoalScopeKind.Series && scopeValue is null)
        {
            return; // no matching series - fail closed rather than save a goal that will never match anything
        }

        context.ReadingGoals.Add(new ReadingGoal
        {
            Title = string.IsNullOrWhiteSpace(Title) ? "Reading goal" : Title.Trim(),
            Metric = Metric,
            Target = long.Parse(TargetText),
            PeriodKind = PeriodKind,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ScopeKind = ScopeKind,
            ScopeValue = scopeValue,
            CreatedUtc = DateTime.UtcNow,
        });
        context.SaveChanges();

        _onSaved();
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
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

public sealed partial class GoalScopeOption : ObservableObject
{
    public GoalScopeOption(GoalScopeKind value, string label)
    {
        Value = value;
        Label = label;
    }

    public GoalScopeKind Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

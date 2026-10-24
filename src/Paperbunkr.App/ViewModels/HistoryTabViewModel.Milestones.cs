using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The milestones strip above History's list (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §5): earned badges newest
/// first, then dimmed "next up" badges with progress. Derived by <see cref="MilestoneResolver"/> each time History loads; the Hide switch lives
/// in <see cref="InsightsPreferences"/>. No alerts, no persistence of its own.
/// </summary>
public partial class HistoryTabViewModel
{
    private int _milestoneGeneration;

    private bool _usesDefaultRunner;

    private Task<MilestoneData> RunMilestoneWork(Func<MilestoneData> work)
        => RunMilestones is { } run ? run(work) : _usesDefaultRunner ? Task.Run(work) : Task.FromResult(work());

    /// <summary>Where the Hide switch is saved. Null (tests) keeps it in memory only.</summary>
    public InsightsPreferences? Preferences { get; set; }

    /// <summary>Runs the resolver off the UI thread; tests pass a synchronous runner.</summary>
    public Func<Func<MilestoneData>, Task<MilestoneData>>? RunMilestones { get; set; }

    public ObservableCollection<MilestoneBadge> Milestones { get; } = new();

    [ObservableProperty]
    private bool _milestonesHidden;

    [ObservableProperty]
    private bool _milestonesLoaded;

    [ObservableProperty]
    private bool _onlyBackfilledHistory;

    /// <summary>The strip shows when there is anything to say and the user hasn't hidden it.</summary>
    public bool ShowMilestones => MilestonesLoaded && !MilestonesHidden && Milestones.Count > 0;

    /// <summary>Hidden-state of the whole header strip, kept visible when hidden so the user can bring it back.</summary>
    public bool ShowMilestonesHeader => MilestonesLoaded && (MilestonesHidden ? Milestones.Count > 0 : Milestones.Count > 0 || OnlyBackfilledHistory);

    public bool ShowBackfillNote => MilestonesLoaded && !MilestonesHidden && Milestones.Count == 0 && OnlyBackfilledHistory;

    public string MilestonesToggleText => MilestonesHidden ? "Show" : "Hide";

    public string MilestonesSummary
    {
        get
        {
            int earned = Milestones.Count(m => m.IsEarned);
            return earned == 0 ? string.Empty : $"{earned} earned";
        }
    }

    partial void OnMilestonesHiddenChanged(bool value)
    {
        if (Preferences is not null)
        {
            Preferences.HideMilestones = value;
        }

        RaiseMilestoneState();
    }

    partial void OnMilestonesLoadedChanged(bool value) => RaiseMilestoneState();

    partial void OnOnlyBackfilledHistoryChanged(bool value) => RaiseMilestoneState();

    private void RaiseMilestoneState()
    {
        OnPropertyChanged(nameof(ShowMilestones));
        OnPropertyChanged(nameof(ShowMilestonesHeader));
        OnPropertyChanged(nameof(ShowBackfillNote));
        OnPropertyChanged(nameof(MilestonesToggleText));
        OnPropertyChanged(nameof(MilestonesSummary));
    }

    [RelayCommand]
    private void ToggleMilestones() => MilestonesHidden = !MilestonesHidden;

    /// <summary>Applies the saved Hide switch, then loads the milestones. Called from <see cref="RefreshAsync"/>.</summary>
    private async Task LoadMilestonesAsync()
    {
        if (Preferences is not null && MilestonesHidden != Preferences.HideMilestones)
        {
            MilestonesHidden = Preferences.HideMilestones;
        }

        int generation = ++_milestoneGeneration;
        MilestoneData data;
        try
        {
            data = await RunMilestoneWork(() =>
            {
                using var context = _contextFactory();
                return MilestoneResolver.Build(context, _nowUtc(), _timeZone);
            });
        }
        catch (Exception)
        {
            return; // the strip stays as it was; History itself is unaffected
        }

        if (generation != _milestoneGeneration)
        {
            return;
        }

        Milestones.Clear();
        foreach (var m in data.Earned)
        {
            Milestones.Add(MilestoneBadge.FromEarned(m));
        }

        foreach (var p in data.NextUp)
        {
            Milestones.Add(MilestoneBadge.FromProgress(p));
        }

        OnlyBackfilledHistory = data.OnlyBackfilledHistory;
        MilestonesLoaded = true;
        RaiseMilestoneState();
    }

}

/// <summary>One badge on the milestones strip - earned (dated) or locked (progress toward the next one).</summary>
public sealed record MilestoneBadge(
    string Id, string Title, string Detail, string DateText, bool IsEarned, double ProgressFraction, string ProgressText, Symbol Icon)
{
    public bool IsLocked => !IsEarned;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public static MilestoneBadge FromEarned(Milestone m) => new(
        m.Id, m.Title, m.Detail, m.Date.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture), true, 1, string.Empty, IconFor(m.Kind));

    public static MilestoneBadge FromProgress(MilestoneProgress p) => new(
        p.Id, p.Title, string.Empty, string.Empty, false,
        p.Target <= 0 ? 0 : Math.Clamp((double)p.Current / p.Target, 0, 1), $"{p.Current:N0} / {p.Target:N0}",
        p.Id.StartsWith("streak", StringComparison.Ordinal) ? Symbol.Pulse : Symbol.CheckmarkCircle);

    private static Symbol IconFor(MilestoneKind kind) => kind switch
    {
        MilestoneKind.Streak => Symbol.Pulse,
        MilestoneKind.Series => Symbol.Library,
        MilestoneKind.Manga => Symbol.BookOpen,
        MilestoneKind.Book => Symbol.Book,
        MilestoneKind.Goal => Symbol.Flag,
        _ => Symbol.CheckmarkCircle,
    };
}

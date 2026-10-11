using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The reading-goal hooks on a list's page (docs/superpowers/specs/2026-10-04-insights-goal-scopes-design.md): "Set a goal…" in the Manage menu opens
/// the goal editor as a Finish goal for this list, and a "Goal: 12 of 30" chip appears while one is in progress.
/// </summary>
public partial class ReadingListPageViewModel
{
    /// <summary>Set by the shell: opens the goal editor pre-filled with a Finish goal for this list id.</summary>
    public Action<int>? SetGoal { get; set; }

    /// <summary>Set by the shell: goes to the Insights screen (where goals live).</summary>
    public Action? OpenInsights { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoalChip))]
    private string? _goalChipText;

    public bool HasGoalChip => !string.IsNullOrEmpty(GoalChipText);

    [RelayCommand]
    private void SetGoalForList()
    {
        if (_activeReadingListId is int listId)
        {
            SetGoal?.Invoke(listId);
        }
    }

    [RelayCommand]
    private void OpenGoalInsights() => OpenInsights?.Invoke();

    /// <summary>The chip shows the nearest-deadline <em>active</em> Finish goal whose scope is exactly this list (a goal that also narrows by another scope is
    /// about something smaller than the list, so its numbers would be misleading here). Ended goals live on Insights, not on the list's page.</summary>
    internal void RefreshGoalChip(int readingListId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var goal = GoalResolver.Build(context, DateTime.UtcNow)
            .Where(p => p.Goal.Kind == GoalKind.Finish && p.Outcome == GoalOutcome.Active && !p.ScopeMissing)
            .Where(p =>
            {
                var filters = GoalResolver.FiltersOf(p.Goal);
                return filters.Count == 1 && filters[0].Kind == GoalScopeKind.ReadingList && filters[0].Value == readingListId.ToString();
            })
            .OrderBy(p => p.Goal.PeriodEnd)
            .FirstOrDefault();

        GoalChipText = goal is null ? null : $"Goal: {goal.CurrentValue:N0} of {goal.EffectiveTarget:N0}";
    }
}

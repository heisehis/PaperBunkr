using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the computed properties on <see cref="ScheduledTaskRow"/> added by
/// docs/superpowers/specs/2026-09-08-automation-tasks-redesign-design.md.
/// </summary>
public class ScheduledTaskRowTests
{
    private static ScheduledTaskRow CreateRow() => new()
    {
        TaskId = "test-task",
        DisplayName = "Test task",
        Description = "A task.",
        ActivityKind = ActivityJobKind.Other,
    };

    [Fact]
    public void IsActive_FalseByDefault()
    {
        var row = CreateRow();

        Assert.False(row.IsActive);
        Assert.Equal("Run now", row.RunButtonLabel);
    }

    [Fact]
    public void IsActive_TrueWhenRunning()
    {
        var row = CreateRow();

        row.IsRunning = true;

        Assert.True(row.IsActive);
        Assert.Equal("Running", row.RunButtonLabel);
    }

    [Fact]
    public void IsActive_TrueWhenQueued()
    {
        var row = CreateRow();

        row.IsQueued = true;

        Assert.True(row.IsActive);
        Assert.Equal("Queued", row.RunButtonLabel);
    }

    [Fact]
    public void RunButtonLabel_RunningTakesPriorityOverQueued()
    {
        var row = CreateRow();

        row.IsQueued = true;
        row.IsRunning = true;

        Assert.Equal("Running", row.RunButtonLabel);
    }

    [Fact]
    public void IsActive_RaisesPropertyChanged_WhenIsRunningChanges()
    {
        var row = CreateRow();
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.IsRunning = true;

        Assert.Contains(nameof(ScheduledTaskRow.IsActive), raised);
        Assert.Contains(nameof(ScheduledTaskRow.RunButtonLabel), raised);
    }

    [Fact]
    public void IsActive_RaisesPropertyChanged_WhenIsQueuedChanges()
    {
        var row = CreateRow();
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.IsQueued = true;

        Assert.Contains(nameof(ScheduledTaskRow.IsActive), raised);
        Assert.Contains(nameof(ScheduledTaskRow.RunButtonLabel), raised);
    }

    // --- SuggestBox string projection (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md) ---

    [Fact]
    public void ModeText_RoundTripsAndIgnoresUnknownText()
    {
        var row = CreateRow();
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.ModeText = nameof(ScheduleMode.DailyAt);

        Assert.Equal(ScheduleMode.DailyAt, row.Mode);
        Assert.Equal(nameof(ScheduleMode.DailyAt), row.ModeText);
        Assert.Contains(nameof(ScheduledTaskRow.ModeText), raised);
        Assert.Equal(Enum.GetNames<ScheduleMode>(), row.ModeNames);

        row.ModeText = "Hourly";
        Assert.Equal(ScheduleMode.DailyAt, row.Mode);
    }
}

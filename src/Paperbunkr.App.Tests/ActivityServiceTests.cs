using System.Collections.Generic;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ActivityService"/> (docs/superpowers/specs/2026-09-03-activity-center-
/// design.md) - job lifecycle, aggregate state, alert dedupe, StopAll, completion-toast
/// suppression. Plain <c>[Fact]</c>: it needs no Avalonia platform (dispatch is synchronous here,
/// persistence is captured, not written).
/// </summary>
public class ActivityServiceTests
{
    private static ActivityService Create(out List<ActivityRun> recorded)
    {
        var runs = new List<ActivityRun>();
        recorded = runs;
        return new ActivityService(dispatch: a => a(), recordRun: runs.Add);
    }

    [Fact]
    public void StartJob_ThenSucceed_MovesToRecent_AndPersistsOneRun()
    {
        var svc = Create(out var recorded);

        var job = svc.StartJob(ActivityJobKind.LibraryScan, "Scan");
        Assert.Single(svc.ActiveJobs);
        Assert.Equal(ActivityJobStatus.Running, svc.ActiveJobs[0].Status);

        job.Report(3, 10, "3 / 10");
        Assert.Equal(0.3, svc.ActiveJobs[0].Fraction, 3);

        job.Succeed("Added 7 issues", itemsProcessed: 7);

        Assert.Empty(svc.ActiveJobs);
        var recent = Assert.Single(svc.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, recent.Status);
        Assert.Equal("Added 7 issues", recent.ResultSummary);

        var run = Assert.Single(recorded);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status);
        Assert.Equal(7, run.ItemsProcessed);
        Assert.Equal(ActivityJobKind.LibraryScan, run.Kind);
    }

    [Fact]
    public void Dispose_WithoutSettling_RecordsCancelled()
    {
        var svc = Create(out var recorded);

        using (svc.StartJob(ActivityJobKind.Import, "Import"))
        {
            Assert.Single(svc.ActiveJobs);
        }

        Assert.Empty(svc.ActiveJobs);
        Assert.Equal(ActivityJobStatus.Cancelled, Assert.Single(svc.RecentJobs).Status);
        Assert.Equal(ActivityRunStatus.Cancelled, Assert.Single(recorded).Status);
    }

    [Fact]
    public void SucceedAfterFail_IsIgnored()
    {
        var svc = Create(out var recorded);
        var job = svc.StartJob(ActivityJobKind.SyncMetadata, "Sync");

        job.Fail("boom");
        job.Succeed("too late");

        Assert.Equal(ActivityJobStatus.Failed, Assert.Single(svc.RecentJobs).Status);
        Assert.Single(recorded);
    }

    [Fact]
    public void StopAll_CancelsRunningTokens()
    {
        var svc = Create(out _);
        var a = svc.StartJob(ActivityJobKind.LibraryScan, "A");
        var b = svc.StartJob(ActivityJobKind.GenerateCovers, "B");

        svc.StopAll();

        Assert.True(a.CancellationToken.IsCancellationRequested);
        Assert.True(b.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void CompletionToast_RaisedWhenPanelClosed_SuppressedWhenOpen()
    {
        var svc = Create(out _);
        var toasts = new List<string>();
        svc.CompletionToastRequested += (t, _) => toasts.Add(t);

        svc.StartJob(ActivityJobKind.LibraryScan, "Visible").Succeed("done");
        Assert.Single(toasts);

        svc.PanelIsOpen = true;
        svc.StartJob(ActivityJobKind.LibraryScan, "Hidden").Succeed("done");
        Assert.Single(toasts); // unchanged

        // Cancelled jobs never toast.
        svc.PanelIsOpen = false;
        using (svc.StartJob(ActivityJobKind.Import, "Abandoned")) { }
        Assert.Single(toasts);
    }

    [Fact]
    public void RaiseAlert_DedupesByKey_RefreshingTimestamp()
    {
        var svc = Create(out _);

        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Warning, Title = "Folder offline", DedupeKey = "watch:D" });
        var first = svc.Alerts[0].CreatedUtc;

        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Warning, Title = "Folder offline again", DedupeKey = "watch:D" });

        Assert.Single(svc.Alerts);
        Assert.True(svc.Alerts[0].CreatedUtc >= first);
    }

    [Fact]
    public void DismissAlert_And_DismissAll()
    {
        var svc = Create(out _);
        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Info, Title = "one", DedupeKey = "1" });
        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Info, Title = "two", DedupeKey = "2" });

        svc.DismissAlert(svc.Alerts[0].Id);
        Assert.Single(svc.Alerts);

        svc.DismissAllAlerts();
        Assert.Empty(svc.Alerts);
    }

    [Fact]
    public void ClearFinished_DropsRecent_KeepsUpkeepAndAlerts()
    {
        var svc = Create(out _);
        svc.RegisterUpkeep("Background upkeep");
        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Info, Title = "keep me", DedupeKey = "k" });
        svc.StartJob(ActivityJobKind.LibraryScan, "done").Succeed("ok");
        Assert.Single(svc.RecentJobs);

        svc.ClearFinished();

        Assert.Empty(svc.RecentJobs);
        Assert.Single(svc.Alerts);
        Assert.Contains(svc.ActiveJobs, j => j.IsUpkeep);
    }

    [Fact]
    public void Upkeep_TogglesActive_ButNeverSettles()
    {
        var svc = Create(out var recorded);
        var upkeep = svc.RegisterUpkeep("Background upkeep");
        var row = Assert.Single(svc.ActiveJobs);
        Assert.True(row.IsUpkeep);
        Assert.False(row.UpkeepActive);

        upkeep.SetActive("watching 3 folders");
        Assert.True(row.UpkeepActive);
        Assert.Equal("watching 3 folders", row.Detail);

        upkeep.SetIdle();
        Assert.False(row.UpkeepActive);

        Assert.Contains(svc.ActiveJobs, j => j.IsUpkeep);
        Assert.Empty(recorded);
    }

    [Fact]
    public void AggregateProgress_Concept_DeterminateJobsSumFraction()
    {
        var svc = Create(out _);
        var a = svc.StartJob(ActivityJobKind.LibraryScan, "A");
        var b = svc.StartJob(ActivityJobKind.GenerateCovers, "B");

        a.Report(2, 4);
        b.Report(6, 6);

        int done = 0, total = 0;
        foreach (var j in svc.ActiveJobs)
        {
            done += j.Done ?? 0;
            total += j.Total ?? 0;
        }

        Assert.Equal(8, done);
        Assert.Equal(10, total);
    }
}

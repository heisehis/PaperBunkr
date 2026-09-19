using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>The one seam between the acquisition daemon and the UI: daemon events -> Activity Center jobs and alerts.</summary>
public class AcquisitionActivityBridgeTests
{
    private static (AcquisitionActivityBridge Bridge, ActivityService Activity, List<ActivityRun> Runs, List<ToastRequest> Toasts, ChannelEventPublisher Events) Create(
        Func<ActivityLink?>? resultLink = null)
    {
        var runs = new List<ActivityRun>();
        var toasts = new List<ToastRequest>();
        var activity = new ActivityService(dispatch: a => a(), recordRun: runs.Add);
        activity.CompletionToastRequested += toasts.Add;
        var events = new ChannelEventPublisher();
        var bridge = new AcquisitionActivityBridge(activity, events.Reader, post: a => a(), resultLink: resultLink);
        return (bridge, activity, runs, toasts, events);
    }

    [Fact]
    public void ACycle_BecomesOneActivityJob_ThatSucceedsWithTheDaemonsSummary()
    {
        var (bridge, activity, runs, _, _) = Create(() => new ActivityLink(ActivityLinkKind.Preferences, "Acquisition"));

        bridge.Handle(new CycleStartedEvent(Manual: false));
        var job = Assert.Single(activity.ActiveJobs);
        Assert.Equal(ActivityJobKind.Acquisition, job.Kind);
        Assert.Equal(ActivityTrigger.Scheduled, job.Trigger);

        bridge.Handle(new CycleProgressEvent(1, 4, "Spawn #263"));
        Assert.Equal(0.25, activity.ActiveJobs[0].Fraction, 3);

        bridge.Handle(new CycleCompletedEvent("Searched 4 issues, found 6 candidates.", 4, 6));

        Assert.Empty(activity.ActiveJobs);
        var run = Assert.Single(runs);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status);
        Assert.Equal("Searched 4 issues, found 6 candidates.", run.ResultSummary);
        Assert.Equal(4, run.ItemsProcessed);
        Assert.Equal("Preferences", run.ResultLinkKind);
    }

    [Fact]
    public void AScheduledCycle_ToastsOnlyOnFailure_ButAManualOneAlwaysToasts()
    {
        var (bridge, _, _, toasts, _) = Create();

        bridge.Handle(new CycleStartedEvent(Manual: false));
        bridge.Handle(new CycleCompletedEvent("Nothing to search for.", 0, 0));
        Assert.Empty(toasts);                                   // an hourly background success is not worth interrupting for

        bridge.Handle(new CycleStartedEvent(Manual: false));
        bridge.Handle(new CycleFailedEvent("Prowlarr didn't answer."));
        Assert.Single(toasts);

        toasts.Clear();
        bridge.Handle(new CycleStartedEvent(Manual: true));
        bridge.Handle(new CycleCompletedEvent("Searched 1 issue, found 2 candidates.", 1, 2));
        Assert.Single(toasts);                                  // "Search now" answers the user
    }

    [Fact]
    public void AFailedCycle_IsRecordedAsFailed()
    {
        var (bridge, activity, runs, _, _) = Create();

        bridge.Handle(new CycleStartedEvent(Manual: true));
        bridge.Handle(new CycleFailedEvent("Couldn't reach Prowlarr"));

        Assert.Empty(activity.ActiveJobs);
        var run = Assert.Single(runs);
        Assert.Equal(ActivityRunStatus.Failed, run.Status);
        Assert.Equal("Couldn't reach Prowlarr", run.ResultSummary);
    }

    [Fact]
    public void ANewCycle_WhileOneIsStillOpen_DoesNotLeaveAGhostJobBehind()
    {
        var (bridge, activity, _, _, _) = Create();

        bridge.Handle(new CycleStartedEvent(Manual: false));
        bridge.Handle(new CycleStartedEvent(Manual: false));

        Assert.Single(activity.ActiveJobs);
    }

    [Fact]
    public void Alerts_DedupeByTheDaemonsKey_MapSeverity_AndClearWhenTheConditionDoes()
    {
        var (bridge, activity, _, _, _) = Create();

        bridge.Handle(new DaemonAlertEvent("acq-indexer", DaemonAlertSeverity.Warning, "Prowlarr couldn't be reached", "timeout"));
        bridge.Handle(new DaemonAlertEvent("acq-indexer", DaemonAlertSeverity.Warning, "Prowlarr couldn't be reached", "timeout again"));
        bridge.Handle(new DaemonAlertEvent("acq-info", DaemonAlertSeverity.Info, "Off", null));

        Assert.Equal(2, activity.Alerts.Count);
        var outage = activity.Alerts.Single(a => a.DedupeKey == "acq-indexer");
        Assert.Equal(ActivityAlertSeverity.Warning, outage.Severity);
        Assert.Equal(ActivityLinkKind.Preferences, outage.ActionLink!.Kind);
        Assert.Equal("Acquisition", outage.ActionLink.Payload);

        bridge.Handle(new DaemonAlertClearedEvent("acq-indexer"));

        Assert.Equal("acq-info", Assert.Single(activity.Alerts).DedupeKey);
        bridge.Handle(new DaemonAlertClearedEvent("never-raised"));   // clearing something that isn't there is harmless
    }

    [Fact]
    public void EventsWithoutAnOpenJob_AreIgnoredSafely()
    {
        var (bridge, activity, runs, _, _) = Create();

        bridge.Handle(new CycleProgressEvent(1, 2, null));
        bridge.Handle(new CycleCompletedEvent("done", 0, 0));
        bridge.Handle(new CycleFailedEvent("x"));

        Assert.Empty(activity.ActiveJobs);
        Assert.Empty(runs);
    }

    [Fact]
    public async Task TheBackgroundPump_DrainsTheChannel_IntoActivityCenter()
    {
        var (bridge, activity, runs, _, events) = Create();
        bridge.Start();

        events.Publish(new CycleStartedEvent(Manual: true));
        events.Publish(new CycleCompletedEvent("ok", 1, 1));
        events.Publish(new DaemonAlertEvent("k", DaemonAlertSeverity.Info, "Hello", null));

        for (int i = 0; i < 100 && (runs.Count == 0 || activity.Alerts.Count == 0); i++)
        {
            await Task.Delay(20);
        }

        Assert.Single(runs);
        Assert.Single(activity.Alerts);
        bridge.Dispose();
    }
}

using System;
using System.Collections.Generic;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ActivityCenterViewModel"/> projections + open-state plumbing
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Synchronous dispatch, captured
/// persistence - no Avalonia platform or DB needed. The History tab (DB-backed) is out of scope here.
/// </summary>
public class ActivityCenterViewModelTests
{
    private static (ActivityCenterViewModel Vm, ActivityService Svc, List<ActivityLink> Links) Create()
    {
        var svc = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        var links = new List<ActivityLink>();
        var vm = new ActivityCenterViewModel(svc, links.Add, dispatch: a => a());
        return (vm, svc, links);
    }

    [Fact]
    public void RunningJobs_ExcludesUpkeep_WhichIsSurfacedSeparately()
    {
        var (vm, svc, _) = Create();
        svc.RegisterUpkeep("Background upkeep");
        svc.StartJob(ActivityJobKind.LibraryScan, "Scan");

        Assert.Single(vm.RunningJobs);
        Assert.Equal("Scan", vm.RunningJobs[0].Title);
        Assert.NotNull(vm.UpkeepJob);
        Assert.True(vm.UpkeepJob!.IsUpkeep);
        Assert.True(vm.HasRunningJobs);
        Assert.Equal(1, vm.RunningCount);
    }

    [Fact]
    public void FinishedJob_LandsInRecentLists()
    {
        var (vm, svc, _) = Create();
        svc.StartJob(ActivityJobKind.SyncMetadata, "Sync").Succeed("done");

        Assert.Single(vm.RecentJobs);
        Assert.Single(vm.RecentJobsForPeek);
        Assert.True(vm.HasRecentJobs);
        Assert.False(vm.HasRunningJobs);
    }

    [Fact]
    public void Alerts_AreWrapped_AndDismissRoutesToService()
    {
        var (vm, svc, _) = Create();
        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Warning, Title = "heads up", DedupeKey = "a" });

        var row = Assert.Single(vm.Alerts);
        Assert.Equal("heads up", row.Alert.Title);

        row.DismissCommand.Execute(null);
        Assert.Empty(svc.Alerts);
        Assert.Empty(vm.Alerts);
    }

    [Fact]
    public void OpeningPeekOrDrawer_SetsPanelIsOpen_OnService()
    {
        var (vm, svc, _) = Create();
        Assert.False(svc.PanelIsOpen);

        vm.TogglePeekCommand.Execute(null);
        Assert.True(vm.IsPeekOpen);
        Assert.True(svc.PanelIsOpen);

        vm.OpenDrawerCommand.Execute(null);
        Assert.False(vm.IsPeekOpen);
        Assert.True(vm.IsDrawerOpen);
        Assert.True(svc.PanelIsOpen);

        vm.CloseCommand.Execute(null);
        Assert.False(svc.PanelIsOpen);
    }

    [Fact]
    public void CancelJobCommand_CancelsThatJobsToken()
    {
        var (vm, svc, _) = Create();
        var job = svc.StartJob(ActivityJobKind.LibraryScan, "Scan");

        vm.CancelJobCommand.Execute(vm.RunningJobs[0]);

        Assert.True(job.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void FollowLinkCommand_InvokesResolver_AndCloses()
    {
        var (vm, _, links) = Create();
        var link = new ActivityLink(ActivityLinkKind.Preferences, "reader");

        vm.IsDrawerOpen = true;
        vm.FollowLinkCommand.Execute(link);

        Assert.Equal(link, Assert.Single(links));
        Assert.False(vm.IsDrawerOpen);
    }

    [Fact]
    public void ShowHistoryTab_TogglesIsHistory()
    {
        var (vm, _, _) = Create();
        Assert.False(vm.IsHistory);

        vm.ShowHistoryTabCommand.Execute(null);
        Assert.True(vm.IsHistory);

        vm.ShowActiveTabCommand.Execute(null);
        Assert.False(vm.IsHistory);
    }
}

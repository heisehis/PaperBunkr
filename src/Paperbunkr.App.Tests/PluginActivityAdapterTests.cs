using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="PluginActivityAdapter"/> against a real <see cref="ActivityService"/> (docs/superpowers/
/// specs/2026-09-20-plugin-api-4-1-design.md §4): every plugin job is a visible, plugin-attributed
/// <see cref="ActivityJobKind.Plugin"/> row; toast policy is host-controlled; alert severities map
/// one-to-one; and dedupe keys are scoped to the plugin. Plain <c>[Fact]</c> like
/// <see cref="ActivityServiceTests"/> - dispatch is synchronous and persistence is captured.
/// </summary>
public class PluginActivityAdapterTests
{
    private static ActivityService CreateService(out List<ActivityRun> recorded)
    {
        var runs = new List<ActivityRun>();
        recorded = runs;
        return new ActivityService(dispatch: a => a(), recordRun: runs.Add);
    }

    [Fact]
    public void StartJob_creates_a_visible_plugin_job_titled_with_the_plugin_name()
    {
        var svc = CreateService(out _);
        var adapter = new PluginActivityAdapter(svc, "dup-finder", "Duplicate Finder");

        using var handle = adapter.StartJob("Scanning");

        var job = Assert.Single(svc.ActiveJobs);
        Assert.Equal(ActivityJobKind.Plugin, job.Kind);
        Assert.Equal(ActivityTrigger.Plugin, job.Trigger);
        Assert.Equal("Duplicate Finder: Scanning", job.Title);
        Assert.Equal(ActivityJobStatus.Running, job.Status);
    }

    [Fact]
    public void The_toast_policy_is_fixed_by_the_host_to_failures_only()
    {
        var svc = CreateService(out _);
        var adapter = new PluginActivityAdapter(svc, "p", "P");

        using var handle = adapter.StartJob("x");

        Assert.Equal(ActivityToastPolicy.FailuresOnly, Assert.Single(svc.ActiveJobs).ToastPolicy);
    }

    [Fact]
    public void Progress_and_detail_reach_the_job_and_success_is_recorded_in_history()
    {
        var svc = CreateService(out var recorded);
        var adapter = new PluginActivityAdapter(svc, "p", "P");

        var handle = adapter.StartJob("Work");
        handle.Report(3, 10, "3 of 10");
        Assert.Equal(0.3, svc.ActiveJobs[0].Fraction, 3);
        handle.Report("wrapping up");
        Assert.Equal("wrapping up", svc.ActiveJobs[0].Detail);
        handle.Succeed("All done");
        handle.Dispose();

        Assert.Empty(svc.ActiveJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, Assert.Single(svc.RecentJobs).Status);
        var run = Assert.Single(recorded);
        Assert.Equal(ActivityJobKind.Plugin, run.Kind);
        Assert.Equal(ActivityTrigger.Plugin, run.Trigger);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status);
        Assert.Equal("P: Work", run.Title);
    }

    [Fact]
    public void Fail_is_recorded_as_a_failed_run()
    {
        var svc = CreateService(out var recorded);
        var handle = new PluginActivityAdapter(svc, "p", "P").StartJob("Work");

        handle.Fail("it broke");
        handle.Dispose();

        Assert.Equal(ActivityRunStatus.Failed, Assert.Single(recorded).Status);
    }

    [Fact]
    public void Disposing_without_settling_records_a_cancelled_run()
    {
        var svc = CreateService(out var recorded);

        using (new PluginActivityAdapter(svc, "p", "P").StartJob("Work"))
        {
            Assert.Single(svc.ActiveJobs);
        }

        Assert.Empty(svc.ActiveJobs);
        Assert.Equal(ActivityRunStatus.Cancelled, Assert.Single(recorded).Status);
    }

    [Fact]
    public void A_cancellable_job_can_be_cancelled_from_the_activity_center()
    {
        var svc = CreateService(out _);
        var handle = new PluginActivityAdapter(svc, "p", "P").StartJob("Work", cancellable: true);
        Assert.False(handle.CancellationToken.IsCancellationRequested);

        svc.CancelJob(svc.ActiveJobs.Single().Id);

        Assert.True(handle.CancellationToken.IsCancellationRequested);
        handle.Dispose();
    }

    [Fact]
    public void A_blank_plugin_name_falls_back_to_the_key()
    {
        var svc = CreateService(out _);

        using var handle = new PluginActivityAdapter(svc, "my-plugin", "  ").StartJob("Work");

        Assert.Equal("my-plugin: Work", Assert.Single(svc.ActiveJobs).Title);
    }

    [Theory]
    [InlineData(PluginAlertSeverity.Info, ActivityAlertSeverity.Info)]
    [InlineData(PluginAlertSeverity.Warning, ActivityAlertSeverity.Warning)]
    [InlineData(PluginAlertSeverity.Error, ActivityAlertSeverity.Error)]
    public void Alert_severity_maps_one_to_one(PluginAlertSeverity given, ActivityAlertSeverity expected)
    {
        var svc = CreateService(out _);

        new PluginActivityAdapter(svc, "p", "Plug").RaiseAlert(given, "Title", "Detail");

        var alert = Assert.Single(svc.Alerts);
        Assert.Equal(expected, alert.Severity);
        Assert.Equal("Plug: Title", alert.Title);
        Assert.Equal("Detail", alert.Detail);
    }

    [Fact]
    public void Repeat_alerts_with_the_same_key_from_the_same_plugin_collapse_to_one()
    {
        var svc = CreateService(out _);
        var adapter = new PluginActivityAdapter(svc, "p", "P");

        adapter.RaiseAlert(PluginAlertSeverity.Warning, "Offline", dedupeKey: "net");
        adapter.RaiseAlert(PluginAlertSeverity.Warning, "Offline", dedupeKey: "net");

        Assert.Single(svc.Alerts);
    }

    [Fact]
    public void The_same_key_from_two_plugins_does_not_collide()
    {
        var svc = CreateService(out _);

        new PluginActivityAdapter(svc, "one", "One").RaiseAlert(PluginAlertSeverity.Info, "Hi", dedupeKey: "shared");
        new PluginActivityAdapter(svc, "two", "Two").RaiseAlert(PluginAlertSeverity.Info, "Hi", dedupeKey: "shared");

        Assert.Equal(2, svc.Alerts.Count);
    }

    [Fact]
    public void A_plugin_cannot_suppress_an_app_alert_by_reusing_its_key()
    {
        var svc = CreateService(out _);
        svc.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Info, Title = "Update available", DedupeKey = "update-available" });

        new PluginActivityAdapter(svc, "p", "P").RaiseAlert(PluginAlertSeverity.Info, "Mine", dedupeKey: "update-available");

        Assert.Equal(2, svc.Alerts.Count);
    }

    [Fact]
    public void Alerts_without_a_key_are_never_deduped()
    {
        var svc = CreateService(out _);
        var adapter = new PluginActivityAdapter(svc, "p", "P");

        adapter.RaiseAlert(PluginAlertSeverity.Info, "Same");
        adapter.RaiseAlert(PluginAlertSeverity.Info, "Same");

        Assert.Equal(2, svc.Alerts.Count);
    }

    [Fact]
    public void Each_cloned_environment_reports_as_its_own_plugin()
    {
        var svc = CreateService(out _);
        var baseEnvironment = new PaperbunkrPluginEnvironment
        {
            MainWindow = null!,
            App = null!,
            OpenBooks = null!,
            Browser = null!,
            ComicDisplay = null!,
            Metadata = null!,
            Rules = null!,
            Writer = null!,
            ThemePlugin = null!,
            ActivityService = svc,
            ResolvePluginName = key => key switch { "a" => "Alpha", "b" => "Beta", _ => key },
        };

        var cloneA = (PaperbunkrPluginEnvironment)baseEnvironment.Clone();
        cloneA.PluginKey = "a";
        var cloneB = (PaperbunkrPluginEnvironment)baseEnvironment.Clone();
        cloneB.PluginKey = "b";

        // Fetched only after both clones have their keys - and each Activity is built per access, so a
        // clone taken earlier could not have leaked its key into the other.
        using var a = cloneA.Activity.StartJob("Job");
        using var b = cloneB.Activity.StartJob("Job");

        Assert.Equal(new[] { "Beta: Job", "Alpha: Job" }, svc.ActiveJobs.Select(j => j.Title).ToArray());
    }

    [Fact]
    public void The_native_environment_hands_out_the_same_reporter_as_the_base_one()
    {
        var svc = CreateService(out _);
        var inner = new PaperbunkrPluginEnvironment
        {
            MainWindow = null!,
            App = null!,
            OpenBooks = null!,
            Browser = null!,
            ComicDisplay = null!,
            Metadata = null!,
            Rules = null!,
            Writer = null!,
            ThemePlugin = null!,
            ActivityService = svc,
        };
        inner.PluginKey = "native-plug";
        var native = new PaperbunkrNativePluginEnvironment(inner, svc, modalHost: null!);

        using var handle = native.Activity.StartJob("Native job");

        Assert.Equal("native-plug: Native job", Assert.Single(svc.ActiveJobs).Title);
    }
}

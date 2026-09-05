using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PluginScanAlertService"/> (docs/superpowers/specs/2026-09-05-plugin-
/// grouped-review-and-scan-alerts-design.md §4) - proactive Activity Center alerts when a
/// <c>CreateBookList</c> command's grouped result count grows. Same
/// <c>PluginPaths.RootDirectory</c>-redirect seam <c>PluginScreenViewModelTests</c> already uses,
/// and the same synchronous-dispatch <see cref="ActivityService"/> construction
/// <c>ActivityServiceTests</c> already uses - no Avalonia platform needed.
/// </summary>
public sealed class PluginScanAlertServiceTests : IDisposable
{
    private readonly string _originalRoot;
    private readonly string _root;
    private readonly TestPluginApplication _app = new();

    public PluginScanAlertServiceTests()
    {
        _originalRoot = PluginPaths.RootDirectory;
        _root = Path.Combine(Path.GetTempPath(), $"pb-scanalert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        PluginPaths.RootDirectory = _root;
    }

    public void Dispose()
    {
        PluginPaths.RootDirectory = _originalRoot;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>One command per library book, grouped - group count tracks <see cref="TestPluginApplication.Library"/>'s size directly, so a test can just resize the library instead of writing new fixture scripts per scenario.</summary>
    private void WriteGroupedPlugin()
    {
        string dir = Path.Combine(_root, "scan-alert-probe");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), """
            <Plugin key="scan-alert-probe" name="Scan Alert Probe">
              <Command hook="CreateBookList" key="scan-alert-probe.grouped" name="Grouped" script="grouped.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "grouped.csx"), "return Environment.App.GetLibraryBooks().Select(b => new PluginBookGroup(\"g\", new[] { b })).ToArray();");
    }

    private void WriteFlatPlugin()
    {
        string dir = Path.Combine(_root, "flat-probe");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), """
            <Plugin key="flat-probe" name="Flat Probe">
              <Command hook="CreateBookList" key="flat-probe.flat" name="Flat" script="flat.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "flat.csx"), "return Environment.App.GetLibraryBooks();");
    }

    private (PluginHostService Host, ActivityService Activity, PluginScanAlertService Service) Build()
    {
        var host = new PluginHostService();
        host.InitializeForTests(new TestPluginEnvironment(_app));
        var activity = new ActivityService(dispatch: a => a());
        return (host, activity, new PluginScanAlertService(host, activity));
    }

    [Fact]
    public async Task First_check_with_any_groups_alerts_once()
    {
        WriteGroupedPlugin();
        var (_, activity, service) = Build();
        _app.Library = new List<Issue> { new() { Id = 1 } };

        await service.CheckForNewGroupsAsync();

        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("1 possible duplicate found", alert.Title);
        Assert.Equal(ActivityLinkKind.PluginGroupedReview, alert.ActionLink!.Kind);
        Assert.Equal("scan-alert-probe|scan-alert-probe.grouped", alert.ActionLink.Payload);
    }

    [Fact]
    public async Task Repeated_check_with_the_same_count_does_not_alert_again()
    {
        WriteGroupedPlugin();
        var (_, activity, service) = Build();
        _app.Library = new List<Issue> { new() { Id = 1 }, new() { Id = 2 } };

        await service.CheckForNewGroupsAsync();
        await service.CheckForNewGroupsAsync();
        await service.CheckForNewGroupsAsync();

        Assert.Single(activity.Alerts); // dedupe keeps repeats to one row, but this also proves no *new* growth was seen
    }

    [Fact]
    public async Task Growing_count_refreshes_the_alert_rather_than_stacking_a_second_one()
    {
        WriteGroupedPlugin();
        var (_, activity, service) = Build();
        _app.Library = new List<Issue> { new() { Id = 1 } };
        await service.CheckForNewGroupsAsync();

        _app.Library = new List<Issue> { new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 } };
        await service.CheckForNewGroupsAsync();

        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("3 possible duplicates found", alert.Title);
    }

    [Fact]
    public async Task Shrinking_then_regrowing_past_the_original_high_water_mark_alerts_again()
    {
        WriteGroupedPlugin();
        var (_, activity, service) = Build();
        _app.Library = new List<Issue> { new() { Id = 1 }, new() { Id = 2 } };
        await service.CheckForNewGroupsAsync();
        Assert.Equal("2 possible duplicates found", Assert.Single(activity.Alerts).Title);

        _app.Library = new List<Issue> { new() { Id = 1 } }; // shrinks - no alert (count did not grow)
        await service.CheckForNewGroupsAsync();
        Assert.Equal("2 possible duplicates found", Assert.Single(activity.Alerts).Title); // unchanged, no new alert

        _app.Library = new List<Issue> { new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 } }; // regrows past 2
        await service.CheckForNewGroupsAsync();
        Assert.Equal("3 possible duplicates found", Assert.Single(activity.Alerts).Title);
    }

    [Fact]
    public async Task A_flat_Issue_array_command_never_raises_an_alert()
    {
        WriteFlatPlugin();
        var (_, activity, service) = Build();
        _app.Library = new List<Issue> { new() { Id = 1 }, new() { Id = 2 } };

        await service.CheckForNewGroupsAsync();

        Assert.Empty(activity.Alerts);
    }
}

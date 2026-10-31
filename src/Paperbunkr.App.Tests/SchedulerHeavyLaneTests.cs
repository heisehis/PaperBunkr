using System.Linq;
using Paperbunkr.App.Services.Scheduling;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Which scheduled tasks hold the heavy-job lane (reported 2026-10-28: the hourly Metron sync, rate-limited to 14 requests a minute, held the lane for
/// ten minutes at a time, so a bulk scrape the user started waited behind it with nothing on screen - again after every restart, since a sync cut off
/// by closing the app is overdue at the next launch).
/// </summary>
public class SchedulerHeavyLaneTests
{
    [Fact]
    public void MetronAccountSync_RunsBesideTheLane()
    {
        var sync = ScheduledTaskCatalog.All.Single(t => t.Id == ScheduledTaskCatalog.MetronSync);

        Assert.False(sync.UsesHeavyJobLane);
    }

    [Fact]
    public void EveryOtherTask_StillTakesTheLane()
    {
        // Only the sync is exempt: scans, scrapes, organize and cover work are the heavy jobs the lane exists for.
        var exempt = ScheduledTaskCatalog.All.Where(t => !t.UsesHeavyJobLane).Select(t => t.Id).ToList();

        Assert.Equal(new[] { ScheduledTaskCatalog.MetronSync }, exempt);
    }
}

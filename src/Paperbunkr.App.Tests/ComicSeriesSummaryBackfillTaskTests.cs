using Paperbunkr.App.Services.Scheduling;

namespace Paperbunkr.App.Tests;

public sealed class ComicSeriesSummaryBackfillTaskTests
{
    [Fact]
    public void Backfill_IsAnEnabledNetworkTask_WithAUniqueIdentityAndPriority()
    {
        var task = Assert.Single(ScheduledTaskCatalog.All,
            t => t.Id == ScheduledTaskCatalog.ComicSeriesSummaryBackfill);
        Assert.True(task.DefaultEnabled);
        Assert.Equal(SchedulerResourceClass.Network, task.Resource);
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Priority).Distinct().Count());
    }
}

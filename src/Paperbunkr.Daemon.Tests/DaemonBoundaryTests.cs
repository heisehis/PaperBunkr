using System.Reflection;
using Paperbunkr.Daemon.Events;

namespace Paperbunkr.Daemon.Tests;

/// <summary>The daemon must stay UI-free so it can be extracted into a headless host later.</summary>
public class DaemonBoundaryTests
{
    [Fact]
    public void DaemonAssembly_ReferencesNoAvalonia()
    {
        var daemon = typeof(ChannelEventPublisher).Assembly;

        var referenced = daemon.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Equals("Paperbunkr.App", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChannelEventPublisher_DeliversEventsInOrder()
    {
        var publisher = new ChannelEventPublisher();

        publisher.Publish(new CycleStartedEvent(Manual: false));
        publisher.Publish(new CycleProgressEvent(1, 2, "x"));
        publisher.Publish(new CycleCompletedEvent("done", 2, 0));
        publisher.Complete();

        var seen = new List<DaemonEvent>();
        await foreach (var e in publisher.Reader.ReadAllAsync())
        {
            seen.Add(e);
        }

        Assert.Collection(seen,
            e => Assert.IsType<CycleStartedEvent>(e),
            e => Assert.IsType<CycleProgressEvent>(e),
            e => Assert.IsType<CycleCompletedEvent>(e));
    }

    [Fact]
    public void Publish_AfterComplete_DoesNotThrow()
    {
        var publisher = new ChannelEventPublisher();
        publisher.Complete();

        publisher.Publish(new CycleFailedEvent("late"));
    }
}

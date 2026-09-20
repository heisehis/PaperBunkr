using System.Threading.Channels;

namespace Paperbunkr.Daemon.Events;

/// <summary>Where the daemon publishes <see cref="DaemonEvent"/>s. Publishing never blocks or throws.</summary>
public interface IEventPublisher
{
    void Publish(DaemonEvent daemonEvent);
}

/// <summary>
/// <see cref="IEventPublisher"/> backed by an unbounded <see cref="Channel{T}"/>. The host (the app today,
/// a headless service later) drains <see cref="Reader"/>; the daemon only ever sees the interface.
/// </summary>
public sealed class ChannelEventPublisher : IEventPublisher
{
    private readonly Channel<DaemonEvent> _channel = Channel.CreateUnbounded<DaemonEvent>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<DaemonEvent> Reader => _channel.Reader;

    public void Publish(DaemonEvent daemonEvent) => _channel.Writer.TryWrite(daemonEvent);

    public void Complete() => _channel.Writer.TryComplete();
}

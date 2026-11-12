using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls.EventMap;

namespace Paperbunkr.App.Tests.EventMap;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapCoverSourceTests
{
    [Fact]
    public void CacheHitIsSharedAndLateDecodeIsDropped()
    {
        using var shared = new RenderTargetBitmap(new PixelSize(2, 2));
        var callbacks = new ConcurrentQueue<Action>();
        using var source = new EventMapCoverSource(() => { }, key => key == "shared" ? shared : null,
            (_, _) => new RenderTargetBitmap(new PixelSize(2, 2)), callbacks.Enqueue);
        source.SetWanted(new[] { "shared", "late" });
        Assert.Same(shared, source.Get("shared", 48));
        Assert.Null(source.Get("late", 48));
        Assert.True(SpinWait.SpinUntil(() => !callbacks.IsEmpty, TimeSpan.FromSeconds(10)));
        source.SetWanted(Array.Empty<string>());
        Assert.True(callbacks.TryDequeue(out var callback));
        callback!();
        Assert.Equal(0, source.Count);
    }

    [Fact]
    public void DecodedCoversAreBoundedByTheLru()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var source = new EventMapCoverSource(() => { }, _ => null,
            (_, _) => new RenderTargetBitmap(new PixelSize(2, 2)), callbacks.Enqueue);
        var keys = Enumerable.Range(0, 201).Select(i => i.ToString()).ToArray();
        source.SetWanted(keys);
        foreach (string key in keys)
        {
            Assert.Null(source.Get(key, 48));
            Assert.True(SpinWait.SpinUntil(() => !callbacks.IsEmpty, TimeSpan.FromSeconds(10)));
            Assert.True(callbacks.TryDequeue(out var callback));
            callback!();
        }
        Assert.Equal(200, source.Count);
    }
}

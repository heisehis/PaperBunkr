using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SharedElementTransitionService"/>'s control flow and cleanup (docs/
/// superpowers/specs/2026-09-04-navigation-transition-system-design.md) - NOT the actual eased
/// motion, which is manual/on-screen only per this project's standing no-unattended-GUI-automation
/// caveat ([[feedback_no_computer_use]]).
///
/// Two headless-testing gotchas this file works around, both confirmed the hard way (not assumed):
/// 1. Headless mode has no running dispatcher loop, so <c>Bounds</c>/<c>TransformToVisual</c> don't
///    update after a tree mutation on their own - <see cref="Layout"/> forces a synchronous
///    <c>LayoutManager.ExecuteLayoutPass()</c> after every mutation instead. Deliberately not
///    <c>Dispatcher.UIThread.RunJobs()</c> - a documented source of cross-thread flakiness in this
///    project's headless suite (see ReaderScreenViewModelTests' own note on it); a layout pass needs
///    no such thread affinity.
/// 2. A bare <c>async Task</c> xUnit test has no captured <see cref="SynchronizationContext"/>, so
///    any <c>await</c> resumes on an arbitrary thread-pool thread - which then trips Avalonia's
///    compositor "calling thread doesn't own this object" check the instant it touches a visual
///    again (confirmed via a live repro: every test crashed inside
///    <c>Compositor.RegisterForSerialization</c>/<c>VerifyAccess</c> right after its first real
///    <c>await</c>, even ones that mutate nothing async). <see cref="RunSync"/> installs a minimal
///    single-threaded <see cref="SynchronizationContext"/> so every continuation resumes on the same
///    OS thread the test started on - the one Avalonia's headless platform actually owns.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SharedElementTransitionServiceTests
{
    private static readonly Easing Easing = new LinearEasing();
    private static readonly TimeSpan ShortDuration = TimeSpan.FromMilliseconds(20);

    /// <summary>Runs <paramref name="testBody"/> to completion on the calling thread, pumping its own <c>await</c> continuations instead of letting the thread pool steal them - see the class doc's gotcha #2.</summary>
    private static void RunSync(Func<Task> testBody)
    {
        var previous = SynchronizationContext.Current;
        var pump = new QueuingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            var task = testBody();
            pump.RunUntilComplete(task);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class QueuingSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void RunUntilComplete(Task task)
        {
            task.ContinueWith(_ => _queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }

    private static (Window Window, Canvas Overlay, Panel ScreenHost) CreateHarness()
    {
        var overlay = new Canvas();
        var screenHost = new Panel();
        var root = new Panel { Children = { screenHost, overlay } };
        var window = new Window { Content = root, Width = 800, Height = 600 };
        window.Show();
        Layout(window);
        return (window, overlay, screenHost);
    }

    private static void Layout(Window window) => window.GetLayoutManager()?.ExecuteLayoutPass();

    private static Border AddCover(Window window, Panel host, double x, double y, double width, double height, IImage? image)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(x, y, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            CornerRadius = new CornerRadius(6),
            Child = image is null ? null : new Image { Source = image },
        };
        host.Children.Add(border);
        Layout(window);
        return border;
    }

    private static void Remove(Window window, Panel host, Control element)
    {
        host.Children.Remove(element);
        Layout(window);
    }

    private static IImage CreateStubImage() => new RenderTargetBitmap(new PixelSize(4, 4));

    [Fact]
    public void CaptureAndFly_DestinationRegistersInTime_ReturnsTrueAndClearsOverlay() => RunSync(async () =>
    {
        var (window, overlay, screenHost) = CreateHarness();
        var service = new SharedElementTransitionService();
        service.RegisterOverlayHost(overlay);

        var source = AddCover(window, screenHost, x: 20, y: 20, width: 100, height: 100, image: CreateStubImage());
        service.Register("cover:1", source, () => ((Image)source.Child!).Source);
        service.CaptureOutgoing("cover:1");

        // Simulate the screen swap: the outgoing cover leaves, the incoming one (a different size/position) arrives.
        Remove(window, screenHost, source);
        var destination = AddCover(window, screenHost, x: 200, y: 40, width: 300, height: 160, image: CreateStubImage());
        service.Register("cover:1", destination, () => ((Image)destination.Child!).Source);

        var result = await service.FlyToIncomingAsync("cover:1", ShortDuration, Easing, CancellationToken.None);

        Assert.True(result);
        Assert.Empty(overlay.Children);
        window.Close();
    });

    [Fact]
    public void FlyToIncomingAsync_DestinationNeverRegisters_ReturnsFalseWithoutThrowing() => RunSync(async () =>
    {
        var (window, overlay, screenHost) = CreateHarness();
        var service = new SharedElementTransitionService();
        service.RegisterOverlayHost(overlay);

        var source = AddCover(window, screenHost, 0, 0, 100, 100, CreateStubImage());
        service.Register("cover:missing", source, () => ((Image)source.Child!).Source);
        service.CaptureOutgoing("cover:missing");
        Remove(window, screenHost, source);
        service.Unregister("cover:missing", source);
        // Nothing ever registers as the destination.

        var result = await service.FlyToIncomingAsync("cover:missing", ShortDuration, Easing, CancellationToken.None);

        Assert.False(result);
        Assert.Empty(overlay.Children);
        window.Close();
    });

    [Fact]
    public void FlyToIncomingAsync_NothingCaptured_ReturnsFalseImmediately() => RunSync(async () =>
    {
        var (window, overlay, _) = CreateHarness();
        var service = new SharedElementTransitionService();
        service.RegisterOverlayHost(overlay);

        var result = await service.FlyToIncomingAsync("cover:never-captured", ShortDuration, Easing, CancellationToken.None);

        Assert.False(result);
        window.Close();
    });

    [Fact]
    public void Cancel_MidFlight_RemovesCloneAndCompletesWithoutThrowing() => RunSync(async () =>
    {
        var (window, overlay, screenHost) = CreateHarness();
        var service = new SharedElementTransitionService();
        service.RegisterOverlayHost(overlay);

        var source = AddCover(window, screenHost, 0, 0, 100, 100, CreateStubImage());
        service.Register("cover:cancel", source, () => ((Image)source.Child!).Source);
        service.CaptureOutgoing("cover:cancel");
        Remove(window, screenHost, source);
        var destination = AddCover(window, screenHost, 300, 300, 200, 200, CreateStubImage());
        service.Register("cover:cancel", destination, () => ((Image)destination.Child!).Source);

        var flightTask = service.FlyToIncomingAsync("cover:cancel", TimeSpan.FromSeconds(5), Easing, CancellationToken.None);
        service.Cancel();
        var result = await flightTask;

        Assert.False(result);
        Assert.Empty(overlay.Children);
        window.Close();
    });

    [Fact]
    public void Unregister_AfterANewerRegistrationForTheSameKey_DoesNotEvictTheNewerOne() => RunSync(async () =>
    {
        var (window, overlay, screenHost) = CreateHarness();
        var service = new SharedElementTransitionService();
        service.RegisterOverlayHost(overlay);

        var oldElement = AddCover(window, screenHost, 0, 0, 50, 50, CreateStubImage());
        service.Register("cover:reused", oldElement, () => ((Image)oldElement.Child!).Source);

        var newElement = AddCover(window, screenHost, 100, 100, 50, 50, CreateStubImage());
        service.Register("cover:reused", newElement, () => ((Image)newElement.Child!).Source);

        // The old element's own detach handler fires after the new one already registered - must not evict it.
        service.Unregister("cover:reused", oldElement);
        service.CaptureOutgoing("cover:reused");

        // If the newer registration had been wrongly evicted, CaptureOutgoing above would have
        // captured nothing and this fly would return false even though a real destination shows up.
        Remove(window, screenHost, newElement);
        var destination = AddCover(window, screenHost, 400, 400, 80, 80, CreateStubImage());
        service.Register("cover:reused", destination, () => ((Image)destination.Child!).Source);

        var result = await service.FlyToIncomingAsync("cover:reused", ShortDuration, Easing, CancellationToken.None);

        Assert.True(result);
        window.Close();
    });
}

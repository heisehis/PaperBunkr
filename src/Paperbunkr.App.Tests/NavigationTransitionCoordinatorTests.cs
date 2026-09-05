using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="NavigationTransitionCoordinator"/>'s sequencing (docs/superpowers/specs/
/// 2026-09-04-navigation-transition-system-design.md) against a fake <see cref="ISharedElementTransitionService"/>
/// - no real visual tree needed here, unlike <see cref="SharedElementTransitionServiceTests"/>, since
/// the coordinator only calls the interface, never touches a Visual itself.
/// </summary>
public class NavigationTransitionCoordinatorTests
{
    private sealed class FakeSharedElementService : ISharedElementTransitionService
    {
        public int CaptureCount;
        public int FlyCount;
        public bool FlyResult = true;
        public string? LastCaptureKey;
        public string? LastFlyKey;

        public void RegisterOverlayHost(Canvas host) { }
        public void Register(string key, Visual element, Func<IImage?> imageAccessor) { }
        public void Unregister(string key, Visual element) { }

        public void CaptureOutgoing(string key)
        {
            CaptureCount++;
            LastCaptureKey = key;
        }

        public Task<bool> FlyToIncomingAsync(string key, TimeSpan duration, Easing easing, CancellationToken cancellationToken)
        {
            FlyCount++;
            LastFlyKey = key;
            return Task.FromResult(FlyResult);
        }

        public void Cancel() { }
    }

    private static NavigationTransitionCoordinator CreateCoordinator(FakeSharedElementService service, bool reducedMotion = false) =>
        new(service, () => reducedMotion, () => TimeSpan.FromMilliseconds(10), new LinearEasing());

    [Fact]
    public async Task RunAsync_ReducedMotion_OnlyRunsSwapContent_NoServiceCalls()
    {
        var service = new FakeSharedElementService();
        var coordinator = CreateCoordinator(service, reducedMotion: true);
        var swapRan = false;

        await coordinator.RunAsync("cover:1", () => swapRan = true);

        Assert.True(swapRan);
        Assert.Equal(0, service.CaptureCount);
        Assert.Equal(0, service.FlyCount);
    }

    [Fact]
    public async Task RunAsync_NormalMotion_CapturesSwapsThenFlies_InOrder()
    {
        var service = new FakeSharedElementService();
        var coordinator = CreateCoordinator(service);
        var order = new System.Collections.Generic.List<string>();
        service.FlyResult = true;

        await coordinator.RunAsync("cover:1", () => order.Add("swap"));

        Assert.Equal(1, service.CaptureCount);
        Assert.Equal("cover:1", service.LastCaptureKey);
        Assert.Equal(1, service.FlyCount);
        Assert.Equal("cover:1", service.LastFlyKey);
        Assert.Equal(new[] { "swap" }, order);
    }

    [Fact]
    public async Task RunAsync_NullSharedKey_SkipsCaptureAndFly_StillRunsSwap()
    {
        var service = new FakeSharedElementService();
        var coordinator = CreateCoordinator(service);
        var swapRan = false;

        await coordinator.RunAsync(null, () => swapRan = true);

        Assert.True(swapRan);
        Assert.Equal(0, service.CaptureCount);
        Assert.Equal(0, service.FlyCount);
    }

    [Fact]
    public async Task RunAsync_FlyReturnsFalse_StillCompletesWithoutThrowing()
    {
        var service = new FakeSharedElementService { FlyResult = false };
        var coordinator = CreateCoordinator(service);
        var swapRan = false;

        await coordinator.RunAsync("cover:missing", () => swapRan = true);

        Assert.True(swapRan);
        Assert.Equal(1, service.CaptureCount);
        Assert.Equal(1, service.FlyCount);
    }
}

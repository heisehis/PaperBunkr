using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="EntranceAnimation.ComputeDelayMs"/> - the pure delay math extracted from
/// <c>Prepare</c> (docs/superpowers/specs/2026-09-12-entrance-animation-v2-design.md §4) so the
/// stagger-tail cap is unit-testable without a real <c>DispatcherTimer</c>/container.
/// </summary>
public class EntranceAnimationTests
{
    [Fact]
    public void ComputeDelayMs_ClampsAboveMaxStaggerIndex()
    {
        Assert.Equal(EntranceAnimation.ComputeDelayMs(20, reducedMotion: false), EntranceAnimation.ComputeDelayMs(25, reducedMotion: false));
    }

    [Fact]
    public void ComputeDelayMs_BelowCap_ScalesLinearly()
    {
        Assert.True(EntranceAnimation.ComputeDelayMs(5, reducedMotion: false) < EntranceAnimation.ComputeDelayMs(10, reducedMotion: false));
        Assert.NotEqual(EntranceAnimation.ComputeDelayMs(5, reducedMotion: false), EntranceAnimation.ComputeDelayMs(20, reducedMotion: false));
    }

    [Fact]
    public void ComputeDelayMs_ReducedMotion_AlwaysZero()
    {
        Assert.Equal(0, EntranceAnimation.ComputeDelayMs(0, reducedMotion: true));
        Assert.Equal(0, EntranceAnimation.ComputeDelayMs(5, reducedMotion: true));
        Assert.Equal(0, EntranceAnimation.ComputeDelayMs(25, reducedMotion: true));
    }
}

using System.Collections.Generic;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests;

public class PageSkipStepperTests
{
    private static System.Func<int, bool> Skip(params int[] pages)
    {
        var set = new HashSet<int>(pages);
        return set.Contains;
    }

    [Fact]
    public void Resolve_LandingNotSkippable_ReturnsLanding()
    {
        Assert.Equal(3, PageSkipStepper.Resolve(3, 1, 10, Skip(4, 5)));
    }

    [Fact]
    public void Resolve_Forward_WalksPastSkippableRun()
    {
        Assert.Equal(6, PageSkipStepper.Resolve(3, 1, 10, Skip(3, 4, 5)));
    }

    [Fact]
    public void Resolve_Backward_WalksPastSkippableRun()
    {
        Assert.Equal(2, PageSkipStepper.Resolve(5, -1, 10, Skip(3, 4, 5)));
    }

    [Fact]
    public void Resolve_AllSkippableAhead_ReturnsNull()
    {
        Assert.Null(PageSkipStepper.Resolve(7, 1, 10, Skip(7, 8, 9)));
    }

    [Fact]
    public void Resolve_AllSkippableBehind_ReturnsNull()
    {
        Assert.Null(PageSkipStepper.Resolve(2, -1, 10, Skip(0, 1, 2)));
    }

    [Fact]
    public void Resolve_LandingPastEnd_ReturnsNull()
    {
        Assert.Null(PageSkipStepper.Resolve(10, 1, 10, Skip()));
    }

    [Fact]
    public void Resolve_InvalidDirection_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => PageSkipStepper.Resolve(1, 0, 10, Skip()));
    }
}

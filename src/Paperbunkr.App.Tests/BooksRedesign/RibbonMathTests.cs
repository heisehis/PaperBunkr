using Paperbunkr.App.Services.Books;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The chapter ribbon's widths (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
public class RibbonMathTests
{
    [Fact]
    public void Weights_FollowLength() =>
        Assert.Equal([100d, 200d, 300d], RibbonMath.Weights([100, 200, 300]));

    [Fact]
    public void TinyChapter_IsFlooredToAShareOfTheAverage()
    {
        var weights = RibbonMath.Weights([1000, 1000, 1]);

        // average 667, floor 35% of it = 233.4
        Assert.True(weights[2] > 200 && weights[2] < 240, $"floor was {weights[2]}");
        Assert.Equal(1000, weights[0]);
    }

    [Fact]
    public void UnknownLengths_AreEqual() =>
        Assert.Equal([1d, 1d, 1d], RibbonMath.Weights([0, 0, 0]));

    [Fact]
    public void Empty_IsEmpty() => Assert.Empty(RibbonMath.Weights([]));

    [Fact]
    public void Widths_ShareTheSpaceLeftAfterTheGaps()
    {
        var widths = RibbonMath.Widths([1, 1, 2], 108, 4);

        Assert.Equal([25d, 25d, 50d], widths);
    }
}

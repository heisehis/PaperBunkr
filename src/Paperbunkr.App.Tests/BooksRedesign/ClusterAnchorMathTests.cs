using Avalonia;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>Where the floating reader clusters sit (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.2).</summary>
public class ClusterAnchorMathTests
{
    private static readonly Size Host = new(1310, 768);
    private static readonly Size Cluster = new(300, 44);

    [Fact]
    public void TopLeft_IsInsetByTheMargin() => Assert.Equal(new Point(16, 16), ClusterAnchorMath.Place(ClusterAnchor.TopLeft, Host, Cluster));

    [Fact]
    public void TopRight_HugsTheRightEdge() => Assert.Equal(new Point(994, 16), ClusterAnchorMath.Place(ClusterAnchor.TopRight, Host, Cluster));

    [Fact]
    public void BottomCenter_IsCentredAboveTheBottomEdge() =>
        Assert.Equal(new Point(505, 708), ClusterAnchorMath.Place(ClusterAnchor.BottomCenter, Host, Cluster));

    [Fact]
    public void AClusterWiderThanTheHost_StaysOnIt() =>
        Assert.Equal(new Point(0, 16), ClusterAnchorMath.Place(ClusterAnchor.TopRight, new Size(200, 400), Cluster));
}

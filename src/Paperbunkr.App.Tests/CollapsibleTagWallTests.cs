using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

[Collection(nameof(AvaloniaTestCollection))]
public class CollapsibleTagWallTests
{
    [Fact]
    public void CollapsedPanel_ArrangesOnlyTheFirstTwoCompleteRows()
    {
        TestAppBuilder.EnsureInitialized();
        var panel = MakePanel(6);

        panel.Measure(new Size(100, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 100, panel.DesiredSize.Height));

        Assert.Equal(40, panel.DesiredSize.Height);
        Assert.Equal(2, panel.OverflowItemCount);
        Assert.All(panel.Children.Take(4), child => Assert.True(child.IsVisible));
        Assert.All(panel.Children.Skip(4), child => Assert.False(child.IsVisible));
    }

    [Fact]
    public void ExpandedPanel_ShowsOverflowAndWidthChangesRecomputeRows()
    {
        TestAppBuilder.EnsureInitialized();
        var panel = MakePanel(6);
        panel.Measure(new Size(100, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 100, panel.DesiredSize.Height));

        panel.IsExpanded = true;
        panel.Measure(new Size(100, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 100, panel.DesiredSize.Height));
        Assert.Equal(60, panel.DesiredSize.Height);
        Assert.All(panel.Children, child => Assert.True(child.IsVisible));
        Assert.Equal(2, panel.OverflowItemCount);

        panel.IsExpanded = false;
        panel.Measure(new Size(50, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 50, panel.DesiredSize.Height));
        Assert.Equal(40, panel.DesiredSize.Height);
        Assert.Equal(4, panel.OverflowItemCount);
        Assert.All(panel.Children.Take(2), child => Assert.True(child.IsVisible));
        Assert.All(panel.Children.Skip(2), child => Assert.False(child.IsVisible));
    }

    private static CollapsibleTagWrapPanel MakePanel(int count)
    {
        var panel = new CollapsibleTagWrapPanel();
        for (int i = 0; i < count; i++)
        {
            panel.Children.Add(new Border { Width = 40, Height = 20 });
        }

        return panel;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="AnimatedStackPanel"/>'s synchronous FLIP-jump only (docs/superpowers/specs/
/// 2026-09-07-chrome-content-motion-polish-design.md item 5) - the jump to the pre-move offset
/// happens directly inside <c>ArrangeOverride</c>, so it's headless-testable without pumping a
/// dispatcher. The subsequent release back to identity is deferred via
/// <c>Dispatcher.UIThread.Post</c> and therefore manual/on-screen only, same carve-out
/// <see cref="SharedElementTransitionServiceTests"/>'s own doc comment already establishes for this
/// project's headless suite ("NOT the actual eased motion... manual/on-screen only") - deliberately
/// not <c>Dispatcher.UIThread.RunJobs()</c>, a documented flakiness source here.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class AnimatedStackPanelTests
{
    private static void Layout(Window window) => window.GetLayoutManager()?.ExecuteLayoutPass();

    private static Border MakeItem(double height) => new() { Height = height, Width = 100 };

    [Fact]
    public void RemovingAChild_JumpsSurvivorsToTheirPreMoveOffset()
    {
        var top = MakeItem(40);
        var middle = MakeItem(40);
        var bottom = MakeItem(40);
        var panel = new AnimatedStackPanel { Children = { top, middle, bottom } };
        var window = new Window { Content = panel, Width = 200, Height = 400 };
        window.Show();
        Layout(window);

        // Baseline: no reflow has happened yet, so nothing should have a transform.
        Assert.True(bottom.RenderTransform is null || bottom.RenderTransform == TransformOperations.Identity);

        panel.Children.Remove(middle);
        Layout(window);

        // bottom moved up by "middle"'s height (40px) - AnimatedStackPanel should have jumped its
        // RenderTransform to the pre-move offset (delta = oldY - newY = +40) before the deferred
        // release back to identity.
        Assert.NotNull(bottom.RenderTransform);
        Assert.NotEqual(TransformOperations.Identity, bottom.RenderTransform);
    }

    [Fact]
    public void NoPositionChange_NeverTouchesRenderTransform()
    {
        var only = MakeItem(40);
        var panel = new AnimatedStackPanel { Children = { only } };
        var window = new Window { Content = panel, Width = 200, Height = 400 };
        window.Show();
        Layout(window);

        Layout(window); // second pass, nothing moved

        Assert.True(only.RenderTransform is null || only.RenderTransform == TransformOperations.Identity);
    }
}

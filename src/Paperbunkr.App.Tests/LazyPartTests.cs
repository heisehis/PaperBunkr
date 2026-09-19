using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="LazyPart"/> builds its content only while active (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md §4). Under the Avalonia collection because
/// <see cref="Border"/>/<see cref="Decorator"/> construction needs the platform bootstrap.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LazyPartTests
{
    private static int s_built;

    private static FuncDataTemplate<object> CountingTemplate() =>
        new((_, _) =>
        {
            s_built++;
            return new Border { Tag = "built" };
        });

    [Fact]
    public void Inactive_IsNotVisible_SoItAddsNoStackPanelSpacing_AndActiveIsVisible()
    {
        var part = new LazyPart { ContentTemplate = CountingTemplate() };
        Assert.False(part.IsVisible);

        part.IsActive = true;
        Assert.True(part.IsVisible);

        part.IsActive = false;
        Assert.False(part.IsVisible);
    }

    [Fact]
    public void Inactive_BuildsNothing()
    {
        int before = s_built;
        var part = new LazyPart { ContentTemplate = CountingTemplate() };

        Assert.Null(part.Child);
        Assert.Equal(before, s_built);
    }

    [Fact]
    public void BecomingActive_BuildsTheContentOnce_AndBecomingInactiveDropsIt()
    {
        int before = s_built;
        var part = new LazyPart { ContentTemplate = CountingTemplate() };

        part.IsActive = true;
        var first = part.Child;
        Assert.NotNull(first);
        Assert.Equal(before + 1, s_built);

        part.IsActive = true; // no change: no rebuild
        Assert.Same(first, part.Child);
        Assert.Equal(before + 1, s_built);

        part.IsActive = false;
        Assert.Null(part.Child);

        part.IsActive = true;
        Assert.NotNull(part.Child);
        Assert.NotSame(first, part.Child);
        Assert.Equal(before + 2, s_built);
    }

    [Fact]
    public void ActivatedBeforeTheTemplateIsSet_BuildsWhenTheTemplateArrives()
    {
        var part = new LazyPart { IsActive = true };
        Assert.Null(part.Child);

        part.ContentTemplate = CountingTemplate();

        Assert.NotNull(part.Child);
    }

    [Fact]
    public void ReplacingTheTemplate_WhileActive_RebuildsFromTheNewOne()
    {
        var part = new LazyPart { ContentTemplate = CountingTemplate(), IsActive = true };
        var old = part.Child;

        part.ContentTemplate = new FuncDataTemplate<object>((_, _) => new TextBlock { Text = "new" });

        Assert.NotSame(old, part.Child);
        Assert.IsType<TextBlock>(part.Child);
    }

    [Fact]
    public void Content_InheritsTheDecoratorsDataContext()
    {
        var part = new LazyPart { ContentTemplate = CountingTemplate(), DataContext = "the item" };

        part.IsActive = true;

        // Content is a Decorator child, so it sees the item through the inherited DataContext (no ContentPresenter involved).
        var child = Assert.IsType<Border>(part.Child);
        var host = new StackPanel();
        host.Children.Add(part);
        Assert.Equal("the item", part.DataContext);
        Assert.Same(child, part.Child);
    }
}

using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Verifies <c>BlockIdInjector</c> (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-
/// redesign-design.md) - the shared block-anchor mechanism highlights and reading-position locators
/// both build on.
/// </summary>
public class BlockIdInjectorTests
{
    [Fact]
    public void Inject_AssignsSequentialIds_ToEveryBlockLevelElement()
    {
        string html = "<h1>Title</h1><p>First.</p><p>Second.</p>";

        string result = BlockIdInjector.Inject(html);

        Assert.Equal("<h1 id=\"pb-p1\">Title</h1><p id=\"pb-p2\">First.</p><p id=\"pb-p3\">Second.</p>", result);
    }

    [Fact]
    public void Inject_IsDeterministic_SameInputProducesSameIds()
    {
        string html = "<p>One</p><blockquote>Two</blockquote><li>Three</li>";

        string first = BlockIdInjector.Inject(html);
        string second = BlockIdInjector.Inject(html);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Inject_LeavesExistingIdAttribute_Untouched()
    {
        string html = "<p id=\"already-set\">Text.</p><p>Next.</p>";

        string result = BlockIdInjector.Inject(html);

        Assert.Contains("<p id=\"already-set\">Text.</p>", result);
        Assert.Contains("<p id=\"pb-p1\">Next.</p>", result);
    }

    [Fact]
    public void Inject_PreservesExistingAttributes_OnTheSameTag()
    {
        string html = "<p class=\"pb-highlight\">Text.</p>";

        string result = BlockIdInjector.Inject(html);

        Assert.Equal("<p class=\"pb-highlight\" id=\"pb-p1\">Text.</p>", result);
    }

    [Fact]
    public void Inject_IgnoresNonBlockElements_LikeInlineSpansAndImages()
    {
        string html = "<p>Some <strong>bold</strong> text.</p><img src=\"data:image/png;base64,ABC\" />";

        string result = BlockIdInjector.Inject(html);

        Assert.DoesNotContain("<strong id=", result);
        Assert.DoesNotContain("<img id=", result);
        Assert.Contains("<p id=\"pb-p1\">", result);
    }
}

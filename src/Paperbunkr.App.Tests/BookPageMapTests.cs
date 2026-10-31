using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests;

public class BookPageMapTests
{
    private static string Words(int n, string word = "word") => string.Join(' ', Enumerable.Repeat(word, n)) + ".";

    private static BookBlock B(int id, string text) => new($"pb-p{id}", text, 0);

    [Fact]
    public void APageClosesAtTheFirstParagraphEndInsideTheRange()
    {
        // 100 + 100 + 100 words: 200 is under the minimum, 300 reaches it, so the fourth paragraph starts page two.
        var map = BookPageMap.Build([[B(1, Words(100)), B(2, Words(100)), B(3, Words(100)), B(4, Words(50))]], 250, 300);

        Assert.Equal(2, map.Count);
        Assert.Equal("pb-p4", map.Pages[1].BlockId);
        Assert.Equal(0, map.PageOf(0, "pb-p3"));
        Assert.Equal(1, map.PageOf(0, "pb-p4"));
    }

    [Fact]
    public void AParagraphThatWouldOverrunMovesToTheNextPageOnceThePageIsHalfFull()
    {
        var map = BookPageMap.Build([[B(1, Words(200)), B(2, Words(200))]], 250, 300);

        Assert.Equal(2, map.Count);
        Assert.Equal("pb-p2", map.Pages[1].BlockId);
    }

    [Fact]
    public void EveryUnitStartsANewPage()
    {
        var map = BookPageMap.Build([[B(1, Words(10))], [B(1, Words(10))], []], 250, 300);

        Assert.Equal(3, map.Count);
        Assert.Equal([0, 1, 2], map.Pages.Select(p => p.UnitIndex));
        Assert.Equal(1, map.PageOf(1, "pb-p1")); // same block id, different unit (a different file)
        Assert.Equal(2, map.PageOf(2, null)); // an empty unit still has its page
    }

    [Fact]
    public void ALongParagraphIsSplitAtSentenceEnds()
    {
        string paragraph = string.Join(' ', Enumerable.Range(0, 7).Select(_ => Words(100))); // seven 100-word sentences
        var map = BookPageMap.Build([[B(1, paragraph)]], 250, 300);

        Assert.Equal(3, map.Count); // 300 + 300 + 100
        Assert.Equal([0, 300, 600], map.Pages.Select(p => p.WordOffset));
        Assert.All(map.Pages, p => Assert.Equal("pb-p1", p.BlockId));
        Assert.Equal(0, map.PageOf(0, "pb-p1")); // a split paragraph counts from where it starts
    }

    [Fact]
    public void ASentenceLongerThanThePageStaysWhole()
    {
        var map = BookPageMap.Build([[B(1, string.Join(' ', Enumerable.Repeat("word", 700)))]], 250, 300);

        Assert.Equal(1, map.Count);
    }
}

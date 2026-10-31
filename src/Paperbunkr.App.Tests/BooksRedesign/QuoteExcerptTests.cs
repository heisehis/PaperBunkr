using Paperbunkr.App.Services.Books;

namespace Paperbunkr.App.Tests.BooksRedesign;

public class QuoteExcerptTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void NothingToQuote_IsNull(string? text) => Assert.Null(QuoteExcerpt.Cut(text));

    [Fact]
    public void ShortText_IsReturnedWhole() =>
        Assert.Equal("They only remembered how.", QuoteExcerpt.Cut("They only remembered how."));

    [Fact]
    public void Whitespace_IsCollapsedAndTrimmed() =>
        Assert.Equal("By the ninth winter the trees", QuoteExcerpt.Cut("  By the\n ninth   winter\tthe trees \r\n"));

    [Fact]
    public void LongText_IsCutAtAWordBoundaryWithAnEllipsis()
    {
        string text = "By the ninth winter the trees no longer rang when the wind came through; they only remembered how.";

        string? quote = QuoteExcerpt.Cut(text, 40);

        Assert.Equal("By the ninth winter the trees no longer…", quote);
    }

    [Fact]
    public void TrailingPunctuationBeforeTheCut_IsDropped()
    {
        string? quote = QuoteExcerpt.Cut("one two, three four five six", 9);

        Assert.Equal("one two…", quote);
    }

    [Fact]
    public void OneWordLongerThanTheLimit_IsCutMidWord() =>
        Assert.Equal("abcdefghij…", QuoteExcerpt.Cut("abcdefghijklmnop", 10));

    [Fact]
    public void ExactlyAtTheLimit_IsNotCut() =>
        Assert.Equal("abcde fghij", QuoteExcerpt.Cut("abcde fghij", 11));
}

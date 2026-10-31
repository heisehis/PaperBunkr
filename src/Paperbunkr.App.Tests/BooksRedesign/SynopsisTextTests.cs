using Paperbunkr.App.Services.Books;

namespace Paperbunkr.App.Tests.BooksRedesign;

public class SynopsisTextTests
{
    [Fact]
    public void Markdown_FromOpenLibrary_ReadsAsPlainText()
    {
        const string source = "***A Game of Thrones*** is the first novel in ***A Song of Ice and Fire***, by **George R. R. Martin**.\n\n" +
            "### Plot Summary\n\nSet in Westeros.\n\n" +
            "- **Eddard \"Ned\" Stark**: Lord of Winterfell.\n- Became a *New York Times* Bestseller\n\n" +
            "Followed by: [***A Clash of Kings***][1]\n\n[1]: https://openlibrary.org/works/OL257939W\n";

        string clean = SynopsisText.Clean(source);

        Assert.Equal(
            "A Game of Thrones is the first novel in A Song of Ice and Fire, by George R. R. Martin.\n\n" +
            "Plot Summary\n\nSet in Westeros.\n\n" +
            "• Eddard \"Ned\" Stark: Lord of Winterfell.\n• Became a New York Times Bestseller\n\n" +
            "Followed by: A Clash of Kings",
            clean);
    }

    [Fact]
    public void Html_FromGoogleBooks_ReadsAsPlainText()
    {
        Assert.Equal("A <b>bold</b> tale.\n\nSecond & last.",
            SynopsisText.Clean("<p>A &lt;b&gt;bold&lt;/b&gt; tale.</p><p>Second &amp; last.</p>"));
        Assert.Equal("One\nTwo", SynopsisText.Clean("One<br/>Two"));
        Assert.Equal("Read the book.", SynopsisText.Clean("Read the <i>book</i>."));
    }

    [Theory]
    [InlineData("2*3*4 is twenty-four", "2*3*4 is twenty-four")]
    [InlineData("snake_case_name stays", "snake_case_name stays")]
    [InlineData("  plain text  ", "plain text")]
    [InlineData("See [the site](https://example.com) now", "See the site now")]
    [InlineData(null, "")]
    public void LeavesOrdinaryTextAlone(string? source, string expected) => Assert.Equal(expected, SynopsisText.Clean(source));
}

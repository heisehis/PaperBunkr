using Paperbunkr.Data.Books;

namespace Paperbunkr.Data.Tests;

public class BookSeriesSuggesterTests
{
    [Theory]
    [InlineData("Dune Messiah (Dune Chronicles #2)", "Dune Chronicles", 2f)]
    [InlineData("Some Title (The Foo Cycle, Book 3)", "The Foo Cycle", 3f)]
    [InlineData("Book 3 of The Dune Chronicles", "Dune Chronicles", 3f)]
    [InlineData("The Dune Chronicles, Book 2: Messiah", "The Dune Chronicles", 2f)]
    [InlineData("Foundation Vol. 3", "Foundation", 3f)]
    [InlineData("Discworld #12", "Discworld", 12f)]
    [InlineData("Discworld #2.5: Short", "Discworld", 2.5f)]
    public void Suggests_WhenTheTitleCarriesAnExplicitMarker(string title, string series, float index)
    {
        var s = BookSeriesSuggester.Suggest(title);

        Assert.NotNull(s);
        Assert.Equal(series, s!.Value.SeriesName);
        Assert.Equal(index, s.Value.Index);
    }

    [Theory]
    [InlineData("The Hobbit")]
    [InlineData("Apollo 13")]
    [InlineData("Fahrenheit 451")]
    [InlineData("The Book Thief")]
    [InlineData("Catch-22")]
    [InlineData("Vol. 1")]
    [InlineData("Book 2")]
    [InlineData("#5")]
    [InlineData("")]
    [InlineData(null)]
    public void DoesNotGuess_ForPlainTitles(string? title)
    {
        Assert.Null(BookSeriesSuggester.Suggest(title));
    }

    [Fact]
    public void PrefersTheExistingSpelling_CaseInsensitively()
    {
        var s = BookSeriesSuggester.Suggest("Dune Messiah (dune chronicles #2)", new[] { "Dune Chronicles", "Other" });

        Assert.Equal("Dune Chronicles", s!.Value.SeriesName);
    }
}

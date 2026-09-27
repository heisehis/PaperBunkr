using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>Port of CE's cvdb.__cleanup_search_terms (verified against the plugin source, 2026-09-25).</summary>
public class SearchTermCleanerTests
{
    [Theory]
    [InlineData("Batman: Dark Victory", "batman: dark victory")]          // colon is kept - it is one of CE's allowed symbols
    [InlineData("Batman & Robin", "batman and robin")]
    [InlineData("Saga (2012) tbp", "saga 2012")]                          // parentheses and the noise word go
    [InlineData("Hulk   Vs.   Wolverine", "hulk vs. wolverine")]
    [InlineData("Ultimate Spider-Man C2C", "ultimate spider-man")]
    [InlineData("Marvel's Avengers!", "marvel's avengers")]
    public void Clean_MatchesCesFirstPass(string input, string expected) =>
        Assert.Equal(expected, SearchTermCleaner.Clean(input, alternate: false));

    [Theory]
    [InlineData("fantastic 4", "fantastic four")]        // digits expand to words first
    [InlineData("the eight", "the 8")]                    // nothing to expand, so words contract to digits
    [InlineData("batman", "batman")]                      // nothing either way: identical, so the caller skips the retry
    public void Clean_AlternateSwapsDigitsAndWords(string input, string expected) =>
        Assert.Equal(expected, SearchTermCleaner.Clean(input, alternate: true));

    [Fact]
    public void Clean_OnlyPunctuation_IsEmpty() =>
        Assert.Equal(string.Empty, SearchTermCleaner.Clean("!!! ??", alternate: false));
}

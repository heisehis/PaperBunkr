using Paperbunkr.App.Services.LibrarySearch;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SuggestionCandidateList.Rank"/> must return exactly what the old
/// <c>RecomputeSuggestions</c> value ranking did: Contains(OrdinalIgnoreCase), prefix matches first,
/// then alphabetical (OrdinalIgnoreCase), capped.
/// </summary>
public class SuggestionCandidateListTests
{
    private static List<string> Oracle(IEnumerable<string> pool, string query, int max) =>
        pool.Where(v => v.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

    private static readonly string[] s_pool =
    {
        "Batman", "Batgirl", "Bat Lash", "Detective Comics", "Superman", "Man of Steel", "The Bat-Man of Gotham",
        "Ultimate Spider-Man", "Spider-Man", "Manga Classics", "Hellboy", "Berserk", "Akira", "Sandman", "Saga",
        "Attack on Titan", "進撃の巨人", "ヒーロー", "Café Strasse", "Straße", "İstanbul", "Marvel Knights", "Image Comics",
        "batman: year one", "BATMAN BEYOND", "Batwoman",
    };

    [Theory]
    [InlineData("bat", 6)]
    [InlineData("man", 6)]
    [InlineData("man", 100)]
    [InlineData("MAN", 3)]
    [InlineData("s", 6)]
    [InlineData("spider", 6)]
    [InlineData("進", 6)]
    [InlineData("café", 6)]
    [InlineData("strasse", 6)]
    [InlineData("zzz", 6)]
    [InlineData("batman", 1)]
    [InlineData("a", 12)]
    public void Rank_MatchesTheOldRankingExactly(string query, int max)
    {
        // The pool must be distinct under OrdinalIgnoreCase, like the real suggestion index guarantees.
        var pool = s_pool.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var list = new SuggestionCandidateList(pool);

        Assert.Equal(Oracle(pool, query, max), list.Rank(query, max));
    }

    [Fact]
    public void Rank_PrefixMatchesComeBeforeSubstringMatches()
    {
        var list = new SuggestionCandidateList(new[] { "Superman", "Man of Steel", "Manga" });

        Assert.Equal(new[] { "Man of Steel", "Manga", "Superman" }, list.Rank("man", 6));
    }

    [Fact]
    public void Rank_StopsAtTheCap()
    {
        var pool = Enumerable.Range(0, 5000).Select(i => $"Series {i:0000}").ToList();
        var list = new SuggestionCandidateList(pool);

        var result = list.Rank("series", 6);

        Assert.Equal(6, result.Count);
        Assert.Equal(Oracle(pool, "series", 6), result);
    }

    [Fact]
    public void Rank_EmptyPoolOrEmptyQuery_ReturnsNothing()
    {
        Assert.Empty(SuggestionCandidateList.Empty.Rank("x", 6));
        Assert.Empty(new SuggestionCandidateList(new[] { "a" }).Rank(string.Empty, 6));
    }

    [Fact]
    public void Rank_QueryLongerThanAnyValue_ReturnsNothing()
    {
        var list = new SuggestionCandidateList(new[] { "ab", "abc" });

        Assert.Empty(list.Rank("abcdefghij", 6));
    }

    [Fact]
    public void Rank_LargePool_RandomQueriesMatchOracle()
    {
        var random = new Random(42);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz ";
        string Word(int length) => new(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

        var pool = Enumerable.Range(0, 4000).Select(_ => Word(random.Next(3, 14)).Trim()).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var list = new SuggestionCandidateList(pool);

        for (int i = 0; i < 300; i++)
        {
            string query = Word(random.Next(1, 4)).Trim();
            if (query.Length == 0)
            {
                continue;
            }

            Assert.Equal(Oracle(pool, query, 6), list.Rank(query, 6));
        }
    }
}

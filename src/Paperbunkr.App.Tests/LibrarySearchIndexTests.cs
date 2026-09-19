using Paperbunkr.App.Services.LibrarySearch;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Parity of <see cref="LibrarySearchIndex"/> with the substring semantics the Library search always
/// had (docs/superpowers/specs/2026-09-19-library-search-perf-design.md §2): the oracle below is the old
/// <c>MatchesSearch</c> logic, per-field <c>Contains(OrdinalIgnoreCase)</c> over
/// <see cref="SearchFieldBundleCatalog"/>.
/// </summary>
public class LibrarySearchIndexTests
{
    private static bool OldContains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool OldIssuesMatch(Series s, SearchMode mode, string query) =>
        s.Issues.Any(i => SearchFieldBundleCatalog.IssueFieldSelectors[mode](i).Any(v => OldContains(v, query)));

    private static bool OldSeriesLevel(Series s, SearchMode mode, string query) => mode switch
    {
        SearchMode.Series => OldContains(s.Name, query) || s.Titles.Any(t => OldContains(t.Value, query)),
        SearchMode.All => OldContains(s.Name, query) || s.Titles.Any(t => OldContains(t.Value, query))
            || OldContains(s.Publisher, query) || OldContains(s.Genre, query),
        _ => false,
    };

    private static bool OldMatchesSearch(Series s, SearchMode mode, string query) =>
        OldSeriesLevel(s, mode, query) || OldIssuesMatch(s, mode, query);

    private static readonly string[] s_queries =
    {
        "batman", "BATMAN", "Bat", "man", "miller", "MILLER", "moore", "978-1", "isbn", "c:\\comics", "cbz",
        "noir", "marvel", "image", "gotham", "in the", "meets", "ß", "STRASSE", "istanbul", "kelvin", "k",
        "進撃", "巨人", "ヒーロー", "café", "CAFÉ", "cafe", "İ", "ı", "i", "x", "no such text anywhere", "1", "#",
    };

    private static List<Series> BuildCorpus()
    {
        int nextIssueId = 1;
        Issue NewIssue(int seriesId, Action<Issue>? configure = null)
        {
            var issue = new Issue { Id = nextIssueId++, SeriesId = seriesId, Number = nextIssueId.ToString() };
            configure?.Invoke(issue);
            return issue;
        }

        var corpus = new List<Series>();

        var batman = new Series { Id = 1, Name = "Batman", Publisher = "DC", Genre = "Noir" };
        batman.Issues.Add(NewIssue(1, i => { i.Writer = "Frank Miller"; i.Summary = "Gotham in the rain"; i.FilePath = @"C:\Comics\Batman\Batman 001.cbz"; }));
        batman.Issues.Add(NewIssue(1, i => { i.Writer = "Alan Moore"; i.Penciller = "Brian Bolland"; i.ISBN = "978-1-4012-1666-3"; }));
        corpus.Add(batman);

        var titan = new Series { Id = 2, Name = "Attack on Titan", Publisher = "Kodansha" };
        titan.Titles.Add(new SeriesTitle { SeriesId = 2, Value = "進撃の巨人", Type = SeriesTitleType.Native });
        titan.Issues.Add(NewIssue(2, i => { i.Writer = "Hajime Isayama"; i.AlternateSeries = "ヒーロー"; }));
        corpus.Add(titan);

        var unicode = new Series { Id = 3, Name = "Café Strasse", Publisher = "Marvel" };
        unicode.Issues.Add(NewIssue(3, i => { i.Summary = "STRASSE and straße, İstanbul, ıstanbul, KELVIN sign (U+212A)."; i.Notes = "Kelvin"; }));
        unicode.Issues.Add(NewIssue(3, i => { i.Writer = "Zoë Café"; i.BookOwner = "Image Comics"; }));
        corpus.Add(unicode);

        // A series with nothing but a name and no issues at all.
        corpus.Add(new Series { Id = 4, Name = "Empty Shelf" });

        // Series-level publisher matches but no issue does; and the reverse.
        var publisherOnly = new Series { Id = 5, Name = "Quiet", Publisher = "Marvel Knights", Genre = "Noir" };
        publisherOnly.Issues.Add(NewIssue(5));
        corpus.Add(publisherOnly);

        var writerOnly = new Series { Id = 6, Name = "Loud" };
        writerOnly.Issues.Add(NewIssue(6, i => { i.Writer = "Miller"; i.Publisher = "Image"; i.Volume = "1"; i.Notes = "#1"; }));
        corpus.Add(writerOnly);

        return corpus;
    }

    [Fact]
    public void SeriesMatches_EqualsTheOldMatchesSearch_ForEveryModeAndQuery()
    {
        var corpus = BuildCorpus();
        var index = LibrarySearchIndex.Build(corpus);

        foreach (var mode in Enum.GetValues<SearchMode>())
        {
            foreach (string query in s_queries)
            {
                string normalized = LibrarySearchIndex.Normalize(query);
                foreach (var series in corpus)
                {
                    Assert.True(
                        OldMatchesSearch(series, mode, query) == index.SeriesMatches(series.Id, mode, normalized),
                        $"mode={mode} query='{query}' series='{series.Name}'");
                }
            }
        }
    }

    [Fact]
    public void IssueMatches_EqualsTheOldPerIssueBundle_ForEveryModeAndQuery()
    {
        var corpus = BuildCorpus();
        var index = LibrarySearchIndex.Build(corpus);

        foreach (var mode in Enum.GetValues<SearchMode>())
        {
            foreach (string query in s_queries)
            {
                string normalized = LibrarySearchIndex.Normalize(query);
                foreach (var issue in corpus.SelectMany(s => s.Issues))
                {
                    bool expected = SearchFieldBundleCatalog.IssueFieldSelectors[mode](issue).Any(v => OldContains(v, query));
                    Assert.True(
                        expected == index.IssueMatches(issue.Id, mode, normalized),
                        $"mode={mode} query='{query}' issue={issue.Id}");
                }
            }
        }
    }

    [Fact]
    public void SeriesLevelMatches_EqualsTheOldSeriesLevelChecks()
    {
        var corpus = BuildCorpus();
        var index = LibrarySearchIndex.Build(corpus);

        foreach (var mode in Enum.GetValues<SearchMode>())
        {
            foreach (string query in s_queries)
            {
                string normalized = LibrarySearchIndex.Normalize(query);
                foreach (var series in corpus)
                {
                    Assert.True(
                        OldSeriesLevel(series, mode, query) == index.SeriesLevelMatches(series.Id, mode, normalized),
                        $"mode={mode} query='{query}' series='{series.Name}'");
                }
            }
        }
    }

    [Fact]
    public void Query_CannotMatchAcrossTwoFields()
    {
        var series = new Series { Id = 1, Name = "S" };
        series.Issues.Add(new Issue { Id = 1, SeriesId = 1, Writer = "Frank", Penciller = "Miller" });
        var index = LibrarySearchIndex.Build(new[] { series });

        // "FrankMiller" only exists if two fields were concatenated without a separator.
        Assert.False(index.IssueMatches(1, SearchMode.Artists, LibrarySearchIndex.Normalize("frankmiller")));
        Assert.True(index.IssueMatches(1, SearchMode.Artists, LibrarySearchIndex.Normalize("miller")));
    }

    [Fact]
    public void KelvinSign_MatchesLikeOrdinalIgnoreCase_NotLikeLowerCasing()
    {
        // OrdinalIgnoreCase does NOT treat U+212A (KELVIN SIGN) as equal to 'k'; a naive
        // ToLowerInvariant-both-sides index would (it lower-cases to 'k').
        var series = new Series { Id = 1, Name = "S" };
        series.Issues.Add(new Issue { Id = 1, SeriesId = 1, Writer = "\u212A" });
        var index = LibrarySearchIndex.Build(new[] { series });

        bool oldResult = OldContains("\u212A", "k");
        Assert.Equal(oldResult, index.IssueMatches(1, SearchMode.Writer, LibrarySearchIndex.Normalize("k")));
    }

    [Fact]
    public void Build_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => LibrarySearchIndex.Build(BuildCorpus(), cts.Token));
    }
}

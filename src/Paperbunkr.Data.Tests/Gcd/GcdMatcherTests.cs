using Paperbunkr.Data.Gcd;

namespace Paperbunkr.Data.Tests.Gcd;

/// <summary><see cref="GcdMatcher"/> (docs/superpowers/specs/2026-09-27-gcd-data-design.md §3).</summary>
public class GcdMatcherTests : GcdLibraryDb
{
    private static Func<int, CancellationToken, Task<int?>> Metron(Dictionary<int, int?> answers, List<int>? asked = null) =>
        (metronId, _) =>
        {
            asked?.Add(metronId);
            return Task.FromResult(answers.GetValueOrDefault(metronId));
        };

    [Fact]
    public async Task Name_UniqueNameYearPublisher_Matches_WithIssuesByNumber()
    {
        Dump.Series(100, "The Incredible Hulk", 1968, "Marvel").Series(101, "The Incredible Hulk", 1999, "Marvel")
            .Issue(1000, 100, "1").Issue(1001, 100, "2").Issue(1002, 100, "3 [105]");
        var hulk = AddSeries("Incredible Hulk (1968)", "Marvel Comics", ("001", 1968, null), ("2", 1968, null), ("3", 1968, null), ("99", 1976, null));
        using var store = Dump.Open();

        var result = await GcdMatcher.RunAsync(Factory, store);

        var reloaded = Reload(hulk.Id);
        Assert.Equal(100, reloaded.GcdSeriesId);
        Assert.Equal(GcdMatchSourceKind.Name, reloaded.GcdMatchSource);
        var byNumber = reloaded.Issues.ToDictionary(i => i.Number!, i => i.GcdIssueId);
        Assert.Equal(1000, byNumber["001"]);
        Assert.Equal(1001, byNumber["2"]);
        Assert.Equal(1002, byNumber["3"]);
        Assert.Null(byNumber["99"]);
        Assert.Equal((0, 1, 3), (result.SeriesByMetron, result.SeriesByName, result.IssuesMatched));
    }

    [Fact]
    public async Task Name_StartYearFromEarliestIssue_WhenNameHasNone()
    {
        Dump.Series(100, "Hulk", 2008, "Marvel").Series(101, "Hulk", 2014, "Marvel");
        var hulk = AddSeries("Hulk", "Marvel", ("1", 2014, null), ("2", 2014, null));
        using var store = Dump.Open();

        await GcdMatcher.RunAsync(Factory, store);

        Assert.Equal(101, Reload(hulk.Id).GcdSeriesId);
    }

    [Fact]
    public async Task Name_Ambiguous_Or_WrongPublisher_Or_NoYear_IsLeftAlone()
    {
        Dump.Series(100, "Hulk", 2008, "Marvel").Series(101, "Hulk", 2008, "Marvel")
            .Series(200, "Batman", 1940, "DC");
        var ambiguous = AddSeries("Hulk (2008)", "Marvel");
        var wrongPublisher = AddSeries("Batman (1940)", "Panini");
        var noYear = AddSeries("Batman", "DC Comics", ("1", null, null));
        using var store = Dump.Open();

        var result = await GcdMatcher.RunAsync(Factory, store);

        Assert.Null(Reload(ambiguous.Id).GcdSeriesId);
        Assert.Null(Reload(wrongPublisher.Id).GcdSeriesId);
        Assert.Null(Reload(noYear.Id).GcdSeriesId);
        Assert.Equal(0, result.SeriesMatched);
    }

    [Fact]
    public async Task ScrapedIssueIds_MatchSeriesAsMetron()
    {
        Dump.Series(100, "Planet Hulk", 2006, "Marvel").Issue(1000, 100, "1").Issue(1001, 100, "2");
        var series = AddSeries("Hulk: Planet Hulk", null, ("1", 2006, 1000), ("2", 2006, null));
        using var store = Dump.Open();

        var result = await GcdMatcher.RunAsync(Factory, store);

        var reloaded = Reload(series.Id);
        Assert.Equal(100, reloaded.GcdSeriesId);
        Assert.Equal(GcdMatchSourceKind.Metron, reloaded.GcdMatchSource);
        Assert.Equal(1001, reloaded.Issues.Single(i => i.Number == "2").GcdIssueId);
        Assert.Equal(1, result.SeriesByMetron);
    }

    [Fact]
    public async Task MetronSeriesRecord_Wins_OverAnEarlierNameMatch_AndFixesIssues()
    {
        Dump.Series(100, "Hulk", 2008, "Marvel").Issue(1000, 100, "1")
            .Series(300, "Red Hulk", 2008, "Marvel").Issue(3000, 300, "1");
        var series = AddMatchedSeries("Hulk (2008)", 100);
        using (var context = NewContext())
        {
            context.Issues.Add(new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "1", GcdIssueId = 1000 });
            context.SaveChanges();
        }

        AttachMetronSeriesId(series.Id, 555);
        using var store = Dump.Open();

        await GcdMatcher.RunAsync(Factory, store, Metron(new() { [555] = 300 }));

        var reloaded = Reload(series.Id);
        Assert.Equal(300, reloaded.GcdSeriesId);
        Assert.Equal(GcdMatchSourceKind.Metron, reloaded.GcdMatchSource);
        Assert.Equal(3000, reloaded.Issues.Single().GcdIssueId);
    }

    [Fact]
    public async Task MetronMatch_IsNeverRedone_ByName_OrAskedAgain()
    {
        Dump.Series(100, "Hulk", 2008, "Marvel").Series(101, "Hulk (Other)", 2008, "Marvel");
        var series = AddMatchedSeries("Hulk (2008)", 101, GcdMatchSourceKind.Metron);
        AttachMetronSeriesId(series.Id, 555);
        var asked = new List<int>();
        using var store = Dump.Open();

        await GcdMatcher.RunAsync(Factory, store, Metron(new() { [555] = 100 }, asked));

        Assert.Equal(101, Reload(series.Id).GcdSeriesId);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task MetronLookups_AreCapped_AndFailuresSkipped()
    {
        Dump.Series(100, "Hulk", 2008, "Marvel");
        var a = AddSeries("A", null);
        var b = AddSeries("B", null);
        var c = AddSeries("C", null);
        AttachMetronSeriesId(a.Id, 1);
        AttachMetronSeriesId(b.Id, 2);
        AttachMetronSeriesId(c.Id, 3);
        var asked = new List<int>();
        using var store = Dump.Open();

        Task<int?> Lookup(int id, CancellationToken _)
        {
            asked.Add(id);
            return id == 1 ? throw new HttpRequestException("down") : Task.FromResult<int?>(100);
        }

        var result = await GcdMatcher.RunAsync(Factory, store, Lookup, maxMetronLookups: 2);

        Assert.Equal(new[] { 1, 2 }, asked);
        Assert.Null(Reload(a.Id).GcdSeriesId);
        Assert.Equal(100, Reload(b.Id).GcdSeriesId);
        Assert.Null(Reload(c.Id).GcdSeriesId);
        Assert.Equal(1, result.SeriesByMetron);
    }

    [Fact]
    public async Task RunTwice_ChangesNothingTheSecondTime()
    {
        Dump.Series(100, "Hulk", 2008, "Marvel").Issue(1000, 100, "1");
        AddSeries("Hulk (2008)", "Marvel", ("1", 2008, null));
        using var store = Dump.Open();

        await GcdMatcher.RunAsync(Factory, store);
        var second = await GcdMatcher.RunAsync(Factory, store);

        Assert.Equal(new GcdMatchResult(0, 0, 0, 0), second);
    }

    [Fact]
    public void ClearAll_ForgetsEveryId()
    {
        var series = AddMatchedSeries("Hulk (2008)", 100, GcdMatchSourceKind.Metron);
        using (var context = NewContext())
        {
            context.Issues.Add(new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "1", GcdIssueId = 1000 });
            context.SaveChanges();
        }

        GcdMatcher.ClearAll(Factory);

        var reloaded = Reload(series.Id);
        Assert.Null(reloaded.GcdSeriesId);
        Assert.Null(reloaded.GcdMatchSource);
        Assert.Null(reloaded.Issues.Single().GcdIssueId);
    }
}

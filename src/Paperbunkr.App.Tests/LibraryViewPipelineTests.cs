using Paperbunkr.App.Models;
using Paperbunkr.App.Services.LibrarySearch;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="LibraryViewPipeline"/> in isolation (no database, no view-model): the filter / sort /
/// group half of the old <c>RebuildView</c>, plus the per-issue search rule (docs/superpowers/specs/
/// 2026-09-19-library-search-perf-design.md §3). Under the Avalonia collection because cards and rows
/// build cover brushes.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryViewPipelineTests
{
    private static int s_nextId = 1;

    private static Issue NewIssue(int seriesId, string number, Action<Issue>? configure = null)
    {
        var issue = new Issue { Id = s_nextId++, SeriesId = seriesId, Number = number, AddedTime = DateTime.UtcNow };
        configure?.Invoke(issue);
        return issue;
    }

    private static Series NewSeries(int id, string name, ContentType type = ContentType.Comic, string? publisher = null)
    {
        var series = new Series { Id = id, Name = name, ContentType = type, Publisher = publisher };
        return series;
    }

    /// <summary>Two series: "Batman" (issues by Miller and Moore) and "Saga" (one issue by Vaughan).</summary>
    private static List<Series> Corpus()
    {
        var batman = NewSeries(1, "Batman", publisher: "DC");
        batman.Issues.Add(NewIssue(1, "1", i => { i.Writer = "Frank Miller"; i.LastPageRead = 5; }));
        batman.Issues.Add(NewIssue(1, "2", i => { i.Writer = "Alan Moore"; }));

        var saga = NewSeries(2, "Saga", ContentType.Manga, "Image");
        saga.Issues.Add(NewIssue(2, "1", i => { i.Writer = "Brian K. Vaughan"; i.FileIsMissing = true; }));

        return new List<Series> { batman, saga };
    }

    private static LibraryViewInputs Inputs(
        string query = "",
        SearchMode mode = SearchMode.All,
        ContentType? contentType = null,
        IReadOnlySet<int>? collection = null,
        bool tracked = false,
        bool unread = false,
        bool missing = false,
        IssueListGroupField group = IssueListGroupField.None,
        LibraryLens lens = LibraryLens.All,
        string? publisher = null)
    {
        var list = new IssueListScreenViewModel(_ => { }) { SortField = IssueListSortField.Series, SortDirection = SortDirection.Ascending, GroupField = group };
        return new LibraryViewInputs(contentType, collection, query, mode, tracked, unread, missing, list.CaptureSortGroupSpec(), Lens: lens, PublisherFilter: publisher);
    }

    private static LibraryViewResult Run(List<Series> corpus, LibraryViewInputs inputs)
    {
        var projection = LibraryProjection.Build(corpus, Array.Empty<VirtualTagDefinition>(), dataVersion: 1);
        return LibraryViewPipeline.Compute(inputs, projection, builtProjection: null, CancellationToken.None);
    }

    [Fact]
    public void NoFilters_ReturnsEveryCardAndRow()
    {
        var result = Run(Corpus(), Inputs());

        Assert.Equal(2, result.Cards.Count);
        Assert.Equal(3, result.Rows.Count);
        Assert.False(result.IsGrouped);
    }

    [Fact]
    public void WriterSearch_ListsOnlyTheMatchingIssues_ButKeepsTheSeriesCard()
    {
        var result = Run(Corpus(), Inputs("miller", SearchMode.Writer));

        Assert.Equal("Batman", Assert.Single(result.Cards).Name);
        var row = Assert.Single(result.Rows);
        Assert.Equal("Frank Miller", row.Writer);
    }

    [Fact]
    public void SeriesNameSearch_ListsEveryIssueOfThatSeries()
    {
        var result = Run(Corpus(), Inputs("batman", SearchMode.All));

        Assert.Equal("Batman", Assert.Single(result.Cards).Name);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void SeriesModeSearch_OnTheSeriesName_ListsEveryIssueOfThatSeries()
    {
        var result = Run(Corpus(), Inputs("batman", SearchMode.Series));

        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void PublisherSearch_InAllMode_ListsEveryIssueOfThatSeriesButNotInWriterMode()
    {
        var corpus = Corpus();

        Assert.Equal(2, Run(corpus, Inputs("dc", SearchMode.All)).Rows.Count);
        Assert.Empty(Run(corpus, Inputs("dc", SearchMode.Writer)).Rows);
    }

    [Fact]
    public void NoMatch_ReturnsNothing()
    {
        var result = Run(Corpus(), Inputs("zzzz"));

        Assert.Empty(result.Cards);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void EmptyEffectiveQuery_IsNoTextFilter_EvenWithAScopedMode()
    {
        var result = Run(Corpus(), Inputs(string.Empty, SearchMode.Writer));

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void ContentTypeFilter_KeepsOnlyThatType_AndWinsOverACollection()
    {
        var corpus = Corpus();

        var byType = Run(corpus, Inputs(contentType: ContentType.Manga, collection: new HashSet<int> { 1 }));

        Assert.Equal("Saga", Assert.Single(byType.Cards).Name);
    }

    [Fact]
    public void CollectionFilter_KeepsOnlyMemberSeries()
    {
        var result = Run(Corpus(), Inputs(collection: new HashSet<int> { 2 }));

        Assert.Equal("Saga", Assert.Single(result.Cards).Name);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void TrackedFilter_KeepsOnlySeriesWithTrackingLinks()
    {
        var corpus = Corpus();
        corpus[1].TrackingLinks.Add(new TrackingLink { SeriesId = 2, Service = TrackingService.AniList, ExternalId = "1" });

        var result = Run(corpus, Inputs(tracked: true));

        Assert.Equal("Saga", Assert.Single(result.Cards).Name);
    }

    [Fact]
    public void UnreadFilter_IsPerIssueForRows_AndPerSeriesForCards()
    {
        var result = Run(Corpus(), Inputs(unread: true));

        // Batman #1 is read (page 5), #2 and Saga #1 are unread. Both series contain an unread issue.
        Assert.Equal(2, result.Cards.Count);
        Assert.Equal(2, result.Rows.Count);
        Assert.DoesNotContain(result.Rows, r => r.Writer == "Frank Miller");
    }

    [Fact]
    public void MissingFilter_IsPerIssueForRows_AndPerSeriesForCards()
    {
        var result = Run(Corpus(), Inputs(missing: true));

        Assert.Equal("Saga", Assert.Single(result.Cards).Name);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void Grouped_FillsGroupsAndLeavesFlatListsEmpty()
    {
        // Grouped by Series (row Publisher comes from the Issue, which the corpus leaves blank).
        var result = Run(Corpus(), Inputs(group: IssueListGroupField.Series));

        Assert.True(result.IsGrouped);
        Assert.Empty(result.Cards);
        Assert.Empty(result.Rows);
        Assert.Equal(2, result.RowGroups.Count);
        Assert.Equal(3, result.RowGroups.Sum(g => g.Items.Count));
        Assert.Equal(new[] { 2, 1 }, result.RowGroups.Select(g => g.Items.Count));
    }

    [Fact]
    public void Compute_ReusesTheCachedRowAndCardInstances()
    {
        var corpus = Corpus();
        var projection = LibraryProjection.Build(corpus, Array.Empty<VirtualTagDefinition>(), dataVersion: 1);

        var first = LibraryViewPipeline.Compute(Inputs(), projection, null, CancellationToken.None);
        var second = LibraryViewPipeline.Compute(Inputs("miller", SearchMode.Writer), projection, null, CancellationToken.None);

        Assert.Contains(second.Rows.Single(), first.Rows);
        Assert.Contains(second.Cards.Single(), first.Cards);
    }

    [Fact]
    public void Compute_CancelledToken_Throws()
    {
        var projection = LibraryProjection.Build(Corpus(), Array.Empty<VirtualTagDefinition>(), dataVersion: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => LibraryViewPipeline.Compute(Inputs(), projection, null, cts.Token));
    }

    /// <summary>"Fresh" (2 unopened), "Midway" (1 read, 1 half-read, 1 unopened), "Done" (1 read), "Shelf" (no issues at all).</summary>
    private static List<Series> LensCorpus()
    {
        static Action<Issue> Pages(int? lastPageRead, string publisher) => i => { i.PageCount = 20; i.LastPageRead = lastPageRead; i.Publisher = publisher; };

        var fresh = NewSeries(10, "Fresh", publisher: "DC");
        fresh.Issues.Add(NewIssue(10, "1", Pages(null, "DC")));
        fresh.Issues.Add(NewIssue(10, "2", Pages(null, "DC")));

        var midway = NewSeries(11, "Midway", publisher: "Marvel");
        midway.Issues.Add(NewIssue(11, "1", Pages(19, "Marvel")));
        midway.Issues.Add(NewIssue(11, "2", Pages(8, "Marvel")));
        midway.Issues.Add(NewIssue(11, "3", Pages(null, "Marvel")));

        var done = NewSeries(12, "Done", publisher: "DC");
        done.Issues.Add(NewIssue(12, "1", Pages(19, "DC")));

        return new List<Series> { fresh, midway, done, NewSeries(13, "Shelf") };
    }

    [Fact]
    public void LensOf_Series_IsUnreadUntilOpened_ReadOnlyWhenEveryIssueIs()
    {
        var corpus = LensCorpus();

        Assert.Equal(LibraryLens.Unread, LibraryViewPipeline.LensOf(corpus[0]));
        Assert.Equal(LibraryLens.Reading, LibraryViewPipeline.LensOf(corpus[1]));
        Assert.Equal(LibraryLens.Read, LibraryViewPipeline.LensOf(corpus[2]));
        Assert.Equal(LibraryLens.Unread, LibraryViewPipeline.LensOf(corpus[3]));
    }

    [Fact]
    public void LensOf_Series_OpenedButUnfinishedEverywhere_IsReading_NotRead()
    {
        // The unread badge would call this series finished (nothing is unopened); the lens must not.
        var series = NewSeries(20, "Half");
        series.Issues.Add(NewIssue(20, "1", i => { i.PageCount = 20; i.LastPageRead = 5; }));
        series.Issues.Add(NewIssue(20, "2", i => { i.PageCount = 20; i.LastPageRead = 19; }));

        Assert.Equal(LibraryLens.Reading, LibraryViewPipeline.LensOf(series));
    }

    [Fact]
    public void Card_AllReadBadge_MatchesTheReadLens_AndTheStackFlagFollowsTheIssueCount()
    {
        var projection = LibraryProjection.Build(LensCorpus(), Array.Empty<VirtualTagDefinition>(), dataVersion: 1);
        var cards = projection.Entries.ToDictionary(e => e.Card.Name, e => e.Card);

        Assert.False(cards["Fresh"].IsAllRead);
        Assert.False(cards["Midway"].IsAllRead);
        Assert.True(cards["Done"].IsAllRead);
        Assert.False(cards["Shelf"].IsAllRead);

        Assert.True(cards["Fresh"].HasMultipleIssues);
        Assert.False(cards["Done"].HasMultipleIssues);
        // The check and the unread count never show together.
        Assert.All(cards.Values, c => Assert.False(c.IsAllRead && c.HasUnread));
    }

    [Fact]
    public void LensCounts_AreTalliedForBothGranularities_AndDoNotDependOnTheSelectedLens()
    {
        var all = Run(LensCorpus(), Inputs());
        var reading = Run(LensCorpus(), Inputs(lens: LibraryLens.Reading));

        Assert.Equal(new LensCounts(Reading: 1, Unread: 2, Read: 1), all.SeriesLensCounts);
        Assert.Equal(new LensCounts(Reading: 1, Unread: 3, Read: 2), all.IssueLensCounts);
        Assert.Equal(4, all.SeriesLensCounts.All);
        Assert.Equal(all.SeriesLensCounts, reading.SeriesLensCounts);
        Assert.Equal(all.IssueLensCounts, reading.IssueLensCounts);
    }

    [Fact]
    public void Lens_FiltersCardsPerSeries_AndRowsPerIssue()
    {
        var read = Run(LensCorpus(), Inputs(lens: LibraryLens.Read));

        Assert.Equal("Done", Assert.Single(read.Cards).Name);
        // Rows are per issue: Midway's finished issue is listed even though Midway itself is only "reading".
        Assert.Equal(new[] { "Done", "Midway" }, read.Rows.Select(r => r.SeriesName).OrderBy(n => n));

        var unread = Run(LensCorpus(), Inputs(lens: LibraryLens.Unread));
        Assert.Equal(new[] { "Fresh", "Shelf" }, unread.Cards.Select(c => c.Name).OrderBy(n => n));
        Assert.Equal(3, unread.Rows.Count);
    }

    [Fact]
    public void LensCounts_FollowTheSearchAndThePublisherFilter()
    {
        var searched = Run(LensCorpus(), Inputs("midway"));
        Assert.Equal(new LensCounts(Reading: 1, Unread: 0, Read: 0), searched.SeriesLensCounts);

        var dc = Run(LensCorpus(), Inputs(publisher: "dc"));
        Assert.Equal(new[] { "Done", "Fresh" }, dc.Cards.Select(c => c.Name).OrderBy(n => n));
        Assert.Equal(new LensCounts(Reading: 0, Unread: 1, Read: 1), dc.SeriesLensCounts);
        Assert.Equal(3, dc.IssueLensCounts.All);
    }

    [Fact]
    public void WithSeriesRebuilt_ReturnsNewVersion_AndLeavesTheOldOneUntouched()
    {
        var corpus = Corpus();
        var v1 = LibraryProjection.Build(corpus, Array.Empty<VirtualTagDefinition>(), dataVersion: 7);
        var oldCard = v1.Entries[0].Card;

        corpus[0].Status = SeriesStatus.Ongoing;
        var v2 = v1.WithSeriesRebuilt(corpus[0], Array.Empty<VirtualTagDefinition>());

        Assert.NotSame(v1, v2);
        Assert.Same(oldCard, v1.Entries[0].Card);
        Assert.NotSame(oldCard, v2.Entries[0].Card);
        Assert.Equal("Ongoing", v2.Entries[0].Card.SeriesStatusLabel);
        Assert.Same(v1.Entries[1], v2.Entries[1]);
        Assert.Same(v1.Index, v2.Index);
        Assert.Equal(7, v2.DataVersion);
    }

    [Fact]
    public void WithSeriesRebuilt_UnknownSeries_ReturnsTheSameVersion()
    {
        var v1 = LibraryProjection.Build(Corpus(), Array.Empty<VirtualTagDefinition>(), dataVersion: 1);

        Assert.Same(v1, v1.WithSeriesRebuilt(NewSeries(999, "Nope"), Array.Empty<VirtualTagDefinition>()));
    }
}

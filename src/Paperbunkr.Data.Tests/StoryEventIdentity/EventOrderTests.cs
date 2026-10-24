using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>
/// Event order from outside reading orders (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4): the CBRO timeline parser, the
/// order-fitting rule, <see cref="EventChronology.LoadSpans"/> with CBRO ranks, and <see cref="EventOrderSweep"/> against fake sources. No network.
/// </summary>
public class EventOrderTests : IdentityTestDb
{
    // ---------- CBRO page parsing (markup copied from the live pages, 2026-10-12, trimmed) ----------

    private const string MarvelPage = """
        <p style="text-align: justify;">Welcome to the Marvel Event Timeline.  Listed in order (1999) of the <a href="https://comicbookreadingorders.com/marvel/marvel-master-reading-order/">Master</a>.</p>
        <h2  class="h-custom-headline cs-ta-center mtn h5" ><span><strong>Event Order</strong></span></h2>
        <p>Reed Richards and Sue Storm’s Wedding (1965) &#8211; <span style="color: #0000ff;">Fantastic Four Annual #3</span></p>
        <p>Kree-Skrull War (1971) &#8211; <span style="color: #0000ff;">Avengers #89-97</span></p>
        <p><a href="https://comicbookreadingorders.com/marvel/events/avengers-defenders-war-reading-order/">Avengers/Defenders War</a> (1973)</p>
        <p><span style="color: #0000ff;"><b>Marvel NOW! starts here.</b></span></p>
        <p><a href="https://comicbookreadingorders.com/marvel/events/secret-wars-ii-reading-order/">Secret Wars II</a> (1985)</p>
        <p><a href="https://comicbookreadingorders.com/marvel/events/civil-war-reading-order/">Civil War</a> (2006)<br />
        <span style="color: #0000ff;">Occurs during the same time as Secret Wars II.</span></p>
        """;

    private const string DcPage = """
        <h2><span><strong>Event Order</strong></span></h2>
        <p><a class="dc-class" href="https://comicbookreadingorders.com/dc/events/zatannas-search-reading-order/">Zatanna&#8217;s Search</a> (1964)</p>
        <p><a class="dc-class" href="https://comicbookreadingorders.com/dc/events/jack-kirbys-fourth-world-reading-order/">Jack Kirby’s Fourth World </a>(1970)</p>
        <p><strong><span style="color: #0000ff;">The New 52 starts here.</span></strong></p>
        <p><a class="dc-class" href="https://comicbookreadingorders.com/dc/events/crisis-on-infinite-earths-reading-order/">Crisis on Infinite Earths</a> (1985)</p>
        """;

    [Fact]
    public void ParseTimeline_ReadsLinkedAndPlainEntriesInPageOrder_SkippingTheIntroAndEraMarkers()
    {
        var entries = ComicBookReadingOrdersSource.ParseTimeline(MarvelPage, "marvel");

        Assert.Equal(
            new[] { "Reed Richards and Sue Storm’s Wedding", "Kree-Skrull War", "Avengers/Defenders War", "Secret Wars II", "Civil War" },
            entries.Select(e => e.Name));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, entries.Select(e => e.Rank));
        Assert.Equal(new int?[] { 1965, 1971, 1973, 1985, 2006 }, entries.Select(e => e.Year));
        Assert.All(entries, e => Assert.Equal("marvel", e.ListKey));
        Assert.Null(entries[0].Url);                                    // older entries have no page of their own
        Assert.Equal("https://comicbookreadingorders.com/marvel/events/avengers-defenders-war-reading-order/", entries[2].Url);
    }

    [Fact]
    public void ParseTimeline_DecodesEntitiesAndTrimsNamesWithTheYearOutsideTheLink()
    {
        var entries = ComicBookReadingOrdersSource.ParseTimeline(DcPage, "dc");

        Assert.Equal(new[] { "Zatanna’s Search", "Jack Kirby’s Fourth World", "Crisis on Infinite Earths" }, entries.Select(e => e.Name));
        Assert.Equal(new[] { "dc", "dc", "dc" }, entries.Select(e => e.ListKey));
    }

    [Fact]
    public void ParseTimeline_APageWithoutTheHeading_StillReadsTheEntries_AndAnEmptyPageGivesNone()
    {
        Assert.Single(ComicBookReadingOrdersSource.ParseTimeline("<p><a href=\"https://comicbookreadingorders.com/dc/events/x-reading-order/\">X</a> (2001)</p>", "dc"));
        Assert.Empty(ComicBookReadingOrdersSource.ParseTimeline("<html><body>maintenance</body></html>", "dc"));
    }

    // ---------- fitting a reading order to dates ----------

    private static EventSpan Span(int id, int? start, int? end = null) => new(id, $"E{id}", start, end ?? start);

    private static Dictionary<int, EventSpan> Spans(params EventSpan[] spans) => spans.ToDictionary(s => s.EventId);

    private static (string, int, int)[] Ranks(params int[] idsInOrder) => idsInOrder.Select((id, i) => ("marvel", id, i + 1)).ToArray();

    [Fact]
    public void ApplyReadingOrder_AnAlreadyOrderedList_KeepsEveryOwnDate()
    {
        var spans = Spans(Span(1, 198401), Span(2, 198601), Span(3, 198801));

        var result = EventChronology.ApplyReadingOrder(spans, Ranks(1, 2, 3));

        Assert.Equal(new int?[] { 198401, 198601, 198801 }, new[] { 1, 2, 3 }.Select(id => result[id].SortKey));
        Assert.Equal(new int?[] { 1, 2, 3 }, new[] { 1, 2, 3 }.Select(id => result[id].Rank));
    }

    [Fact]
    public void ApplyReadingOrder_AnEventListedAfterALaterOne_IsPlacedWithIt()
    {
        // CBRO lists Second Clone Saga (1994) after Age of Apocalypse (1995).
        var spans = Spans(Span(1, 199305), Span(2, 199502), Span(3, 199409), Span(4, 199601));

        var result = EventChronology.ApplyReadingOrder(spans, Ranks(1, 2, 3, 4));

        Assert.Equal(199305, result[1].SortKey);
        Assert.Equal(result[2].SortKey, result[3].SortKey);                 // pooled: they sit together …
        Assert.True(result[2].Rank < result[3].Rank);                       // … in CBRO's order
        Assert.Equal(199601, result[4].SortKey);
    }

    [Fact]
    public void ApplyReadingOrder_OneMisdatedEvent_IsOutvotedAndDoesNotDragTheRestLate()
    {
        // The third event is only owned as a 2015 collected edition of a 1986 story.
        var spans = Spans(Span(1, 198401), Span(2, 198501), Span(3, 201501), Span(4, 198701), Span(5, 198801));

        var result = EventChronology.ApplyReadingOrder(spans, Ranks(1, 2, 3, 4, 5));

        Assert.True(result[3].SortKey < 198801);
        Assert.Equal(198701, result[4].SortKey);
        Assert.Equal(198801, result[5].SortKey);
        Assert.Equal(201501, spans[3].Start);                               // its own date is untouched (the map still shows it)
    }

    [Fact]
    public void ApplyReadingOrder_AnUndatedRankedEvent_TakesThePlaceOfTheOneBefore_AndAFirstOneHasNone()
    {
        var spans = Spans(Span(1, null), Span(2, 198501), Span(3, null), Span(4, 198701));

        var result = EventChronology.ApplyReadingOrder(spans, Ranks(1, 2, 3, 4));

        Assert.Null(result[1].SortKey);
        Assert.Equal(198501, result[3].SortKey);
        Assert.Equal(198701, result[4].SortKey);
    }

    [Fact]
    public void ApplyReadingOrder_LeavesUnrankedEventsAlone_AndRanksListsSeparately()
    {
        var spans = Spans(Span(1, 198501), Span(2, 198401), Span(3, 199001), Span(4, 198901));

        var result = EventChronology.ApplyReadingOrder(spans, new[] { ("marvel", 1, 1), ("marvel", 2, 2), ("dc", 3, 1), ("dc", 4, 2) });

        Assert.Null(result.GetValueOrDefault(99)?.SortKey);
        Assert.Equal(result[1].SortKey, result[2].SortKey);                 // marvel pair pooled
        Assert.Equal(result[3].SortKey, result[4].SortKey);                 // dc pair pooled, independently
        Assert.NotEqual(result[1].SortKey, result[3].SortKey);
    }

    [Fact]
    public void Order_UsesTheSortKey_ThenTheRank_ThenTheName()
    {
        var events = new[]
        {
            new EventSpan(1, "Late by date, early by rank", 199901, 199901, SortKey: 198001, Rank: 2),
            new EventSpan(2, "Unranked, between", 198501, 198501),
            new EventSpan(3, "Same key, earlier rank", 198001, 198001, SortKey: 198001, Rank: 1),
        };

        var order = EventChronology.Order(events, Array.Empty<(int, int, RelationType)>());

        Assert.Equal(new[] { 3, 1, 2 }, order);
    }

    [Fact]
    public void Order_WithoutAnyRanks_BehavesAsBefore()
    {
        var events = new[] { new EventSpan(1, "B", 199001, 199001), new EventSpan(2, "A", 198001, 198001) };

        Assert.Equal(new[] { 2, 1 }, EventChronology.Order(events, Array.Empty<(int, int, RelationType)>()));
    }

    [Fact]
    public void LoadSpans_HoldsRankedEventsToTheCbroOrder_AndAnEventSubsetIsPlacedTheSameAsTheLibrary()
    {
        int a = AddEvent("A", StoryEventOrigin.Provider, AddSeries("A", ("1", 1995, 2)));
        int b = AddEvent("B", StoryEventOrigin.Provider, AddSeries("B", ("1", 1994, 9)));
        int c = AddEvent("C", StoryEventOrigin.Provider, AddSeries("C", ("1", 1996, 1)));
        using (var context = NewContext())
        {
            foreach (var (id, rank) in new[] { (a, 1), (b, 2), (c, 3) })
            {
                context.EventOrderEvidence.Add(new EventOrderEvidence { StoryEventId = id, Provider = EventOrderProvider.Cbro, SourceId = $"marvel:{rank}", ListKey = "marvel", Rank = rank });
            }

            context.SaveChanges();
        }

        using var read = NewContext();
        var all = EventChronology.LoadSpans(read);
        var subset = EventChronology.LoadSpans(read, new[] { b });

        Assert.Equal(all[a].SortKey, all[b].SortKey);                       // A listed before B though dated after it: pooled
        Assert.Equal(all[b].SortKey, subset[b].SortKey);
        Assert.Single(subset);
        Assert.Equal(new[] { a, b, c }, EventChronology.Order(all.Values.ToList(), Array.Empty<(int, int, RelationType)>()));
    }

    [Fact]
    public void LoadSpans_WithNoEvidence_IsJustTheOwnDates()
    {
        int a = AddEvent("A", StoryEventOrigin.Provider, AddSeries("A", ("1", 1995, 2)));

        using var read = NewContext();
        var span = EventChronology.LoadSpans(read)[a];

        Assert.Equal(199502, span.Start);
        Assert.Null(span.SortKey);
        Assert.Null(span.Rank);
    }

    // ---------- the sweep ----------

    private sealed class FakeCbro : ICbroTimeline
    {
        public List<CbroTimelineEntry> Entries { get; } = new();

        public int Calls { get; private set; }

        public Exception? Throws { get; set; }

        public Task<IReadOnlyList<CbroTimelineEntry>> GetEntriesAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Throws is null ? Task.FromResult<IReadOnlyList<CbroTimelineEntry>>(Entries.ToList()) : Task.FromException<IReadOnlyList<CbroTimelineEntry>>(Throws);
        }
    }

    private sealed class FakeMetron : IMetronEventLists
    {
        public Dictionary<string, MetronEventListInfo> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Asked { get; } = new();

        public int ThrowAfter { get; set; } = int.MaxValue;

        public Task<MetronEventListInfo?> FindEventListAsync(string eventName, CancellationToken cancellationToken)
        {
            if (Asked.Count >= ThrowAfter)
            {
                throw new ComicVineException("Metron's daily limit is nearly used up.", 107);
            }

            Asked.Add(eventName);
            return Task.FromResult(Lists.GetValueOrDefault(eventName));
        }
    }

    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private Task<EventOrderSummary> Run(ICbroTimeline? cbro, IMetronEventLists? metron, int batch = 20, DateTime? at = null) =>
        EventOrderSweep.RunAsync(Factory, cbro, metron, batch, at ?? Now, CancellationToken.None);

    private EventOrderEvidence? Evidence(int eventId, EventOrderProvider provider)
    {
        using var context = NewContext();
        return context.EventOrderEvidence.SingleOrDefault(e => e.StoryEventId == eventId && e.Provider == provider);
    }

    [Fact]
    public async Task Cbro_MatchesEventsByName_StoringTheRankAndList_AndRecordsTheOthersAsNotFound()
    {
        int civil = AddEvent("Civil War", StoryEventOrigin.Provider, AddSeries("Civil War", ("1", 2006, 7)));
        int mine = AddEvent("My Own Event", StoryEventOrigin.User, AddSeries("Mine", ("1", 2020, 1)));
        var cbro = new FakeCbro();
        cbro.Entries.AddRange(ComicBookReadingOrdersSource.ParseTimeline(MarvelPage, "marvel"));

        var summary = await Run(cbro, null);

        Assert.Equal((2, 1), (summary.CbroChecked, summary.CbroMatched));
        var row = Evidence(civil, EventOrderProvider.Cbro)!;
        Assert.Equal(("marvel", 5, "Civil War"), (row.ListKey, row.Rank, row.SourceName));
        Assert.Equal("https://comicbookreadingorders.com/marvel/events/civil-war-reading-order/", row.SourceId);
        var none = Evidence(mine, EventOrderProvider.Cbro)!;
        Assert.Null(none.SourceId);
        Assert.Null(none.Rank);
    }

    [Fact]
    public async Task Cbro_IsNotFetchedAgainWithinAWeek_UnlessANewEventAppears()
    {
        AddEvent("Civil War", StoryEventOrigin.Provider, AddSeries("Civil War", ("1", 2006, 7)));
        var cbro = new FakeCbro();
        cbro.Entries.AddRange(ComicBookReadingOrdersSource.ParseTimeline(MarvelPage, "marvel"));
        await Run(cbro, null);

        await Run(cbro, null, at: Now.AddDays(3));
        Assert.Equal(1, cbro.Calls);

        AddEvent("Secret Wars II", StoryEventOrigin.Provider, AddSeries("SW2", ("1", 1985, 7)));
        await Run(cbro, null, at: Now.AddDays(3));
        Assert.Equal(2, cbro.Calls);

        await Run(cbro, null, at: Now.AddDays(11));
        Assert.Equal(3, cbro.Calls);
    }

    [Fact]
    public async Task Cbro_SameNameInTwoEras_IsToldApartByYear_AndNotGuessedWithoutOne()
    {
        int old = AddEvent("Secret Wars", StoryEventOrigin.Provider, AddSeries("Secret Wars", ("1", 1984, 5)));
        int later = AddEvent("Secret Wars", StoryEventOrigin.Provider, AddSeries("Secret Wars 2015", ("1", 2015, 7)));
        int unsure = AddEvent("Secret Wars", StoryEventOrigin.Provider, AddSeries("Secret Wars Unknown", "1"));
        var cbro = new FakeCbro();
        cbro.Entries.Add(new CbroTimelineEntry("marvel", 11, "Secret Wars", 1984, null));
        cbro.Entries.Add(new CbroTimelineEntry("marvel", 80, "Secret Wars", 2015, null));

        await Run(cbro, null);

        Assert.Equal(11, Evidence(old, EventOrderProvider.Cbro)!.Rank);
        Assert.Equal(80, Evidence(later, EventOrderProvider.Cbro)!.Rank);
        Assert.Null(Evidence(unsure, EventOrderProvider.Cbro)!.Rank);
    }

    [Fact]
    public async Task Cbro_AnUnreadableSite_IsReported_NotThrown_AndKeepsWhatWasStored()
    {
        int civil = AddEvent("Civil War", StoryEventOrigin.Provider, AddSeries("Civil War", ("1", 2006, 7)));
        var cbro = new FakeCbro();
        cbro.Entries.AddRange(ComicBookReadingOrdersSource.ParseTimeline(MarvelPage, "marvel"));
        await Run(cbro, null);

        cbro.Throws = new ReadingListSourceException("Comic Book Reading Orders", "down");
        var summary = await Run(cbro, null, at: Now.AddDays(30));

        Assert.Contains("could not be read", summary.Problem);
        Assert.Equal(5, Evidence(civil, EventOrderProvider.Cbro)!.Rank);

        cbro.Throws = null;
        cbro.Entries.Clear();                                              // an empty page is not "no event is listed"
        await Run(cbro, null, at: Now.AddDays(31));
        Assert.Equal(5, Evidence(civil, EventOrderProvider.Cbro)!.Rank);
    }

    [Fact]
    public async Task Metron_StoresTheListAndItsNeighbours_AndAsksAtMostABatch_OldestFirst()
    {
        int planet = AddEvent("Planet Hulk", StoryEventOrigin.Provider, AddSeries("Hulk", ("92", 2006, 4)));
        int war = AddEvent("World War Hulk (2007)", StoryEventOrigin.Provider, AddSeries("WWH", ("1", 2007, 6)));
        AddEvent("Third", StoryEventOrigin.Provider, AddSeries("Third", ("1", 2008, 1)));
        var metron = new FakeMetron();
        metron.Lists["Planet Hulk"] = new MetronEventListInfo("77", "Planet Hulk", null, "World War Hulk");

        var summary = await Run(null, metron, batch: 2);

        Assert.Equal((2, 1), (summary.MetronChecked, summary.MetronMatched));
        Assert.Equal(new[] { "Planet Hulk", "World War Hulk (2007)" }, metron.Asked.OrderBy(n => n));  // the client strips the year itself
        var row = Evidence(planet, EventOrderProvider.MetronList)!;
        Assert.Equal(("77", "World War Hulk", null), (row.SourceId, row.NextName, row.PreviousName));
        Assert.Null(Evidence(war, EventOrderProvider.MetronList)!.SourceId);                           // asked, nothing found
    }

    [Fact]
    public async Task Metron_IsNotAskedAgainWithinThirtyDays_AndTheRestOfTheBatchGoesNext()
    {
        AddEvent("One", StoryEventOrigin.Provider, AddSeries("One", ("1", 2001, 1)));
        AddEvent("Two", StoryEventOrigin.Provider, AddSeries("Two", ("1", 2002, 1)));
        AddEvent("Three", StoryEventOrigin.Provider, AddSeries("Three", ("1", 2003, 1)));
        var metron = new FakeMetron();

        await Run(null, metron, batch: 2);
        Assert.Equal(2, metron.Asked.Count);

        await Run(null, metron, batch: 2, at: Now.AddDays(1));
        Assert.Equal(3, metron.Asked.Count);                                // only the one never asked

        await Run(null, metron, batch: 2, at: Now.AddDays(2));
        Assert.Equal(3, metron.Asked.Count);

        await Run(null, metron, batch: 5, at: Now.AddDays(40));
        Assert.Equal(6, metron.Asked.Count);                                // all three again
    }

    [Fact]
    public async Task Metron_ALimitOrLoginHold_StopsTheBatchQuietly_AndKeepsWhatWasDone()
    {
        int one = AddEvent("One", StoryEventOrigin.Provider, AddSeries("One", ("1", 2001, 1)));
        AddEvent("Two", StoryEventOrigin.Provider, AddSeries("Two", ("1", 2002, 1)));
        var metron = new FakeMetron { ThrowAfter = 1 };
        metron.Lists["One"] = new MetronEventListInfo("5", "One", null, null);

        var summary = await Run(null, metron);

        Assert.Equal(1, summary.MetronChecked);
        Assert.Contains("Metron stopped early", summary.Problem);
        Assert.Equal("5", Evidence(one, EventOrderProvider.MetronList)!.SourceId);
    }

    [Fact]
    public async Task NoSources_DoesNothing()
    {
        AddEvent("One", StoryEventOrigin.Provider, AddSeries("One", ("1", 2001, 1)));

        var summary = await Run(null, null);

        Assert.Equal("reading orders up to date", summary.Text);
        using var context = NewContext();
        Assert.Empty(context.EventOrderEvidence);
    }
}

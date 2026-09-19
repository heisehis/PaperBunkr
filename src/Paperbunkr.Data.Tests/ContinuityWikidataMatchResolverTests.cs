using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ContinuityWikidataMatchResolver"/> (docs/superpowers/specs/2026-09-17-
/// storyevent-continuity-autopopulate-design.md, Phase 2) against a real SQLite database and a fake
/// <see cref="HttpMessageHandler"/> standing in for Wikidata - no real network calls.
/// </summary>
public class ContinuityWikidataMatchResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ContinuityWikidataMatchResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuitywikidata_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static int SeedSeriesWithCharacters(PaperbunkrDbContext context, string seriesName, params string[] characters)
    {
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", Characters = string.Join(", ", characters) };
        context.Issues.Add(issue);
        context.SaveChanges();
        CharacterResolver.SyncFromIssue(context, issue.Id);

        return series.Id;
    }

    /// <summary>Fake Wikidata: "Spider-Man"/"Iron Man" both resolve to comics-character items linking to Earth-616; "Not A Hero" resolves to a real item that fails the P31 filter.</summary>
    private static WikidataClient FakeWikidata() => new(new HttpClient(new StubHandler((request, _) =>
    {
        // Uri.ToString() decodes percent-escapes back to plain text (a real .NET Uri gotcha -
        // AbsoluteUri would keep "Iron%20Man" escaped, ToString() gives back "Iron Man"), so match
        // against the decoded, human-readable form here.
        string url = request.RequestUri!.ToString();
        if (url.Contains("wbsearchentities"))
        {
            if (url.Contains("Spider-Man")) return JsonResponse("""{ "search": [ { "id": "Q79037", "label": "Spider-Man" } ] }""");
            if (url.Contains("Iron Man")) return JsonResponse("""{ "search": [ { "id": "Q48539", "label": "Iron Man" } ] }""");
            if (url.Contains("Not")) return JsonResponse("""{ "search": [ { "id": "Q1000", "label": "Not A Hero" } ] }""");
            // Series-title search targets for the work-level non-canon check (IsWikidataFlaggedNonCanonAsync).
            if (url.Contains("Speeding Bullets")) return JsonResponse("""{ "search": [ { "id": "Q9001", "label": "Speeding Bullets" } ] }""");
            if (url.Contains("Just Imagine")) return JsonResponse("""{ "search": [ { "id": "Q9002", "label": "Just Imagine" } ] }""");
            // Location-tag fallback search targets (TryResolveLocationUniverseAsync) - values not in the hardcoded whitelist.
            if (url.Contains("Earth-9407")) return JsonResponse("""{ "search": [ { "id": "Q9003", "label": "Earth-9407" } ] }""");
            if (url.Contains("Not A Real Place")) return JsonResponse("""{ "search": [ { "id": "Q9004", "label": "Not A Real Place" } ] }""");
            return JsonResponse("""{ "search": [] }""");
        }

        if (url.Contains("Q9001")) return JsonResponse("""{ "entities": { "Q9001": { "labels": { "en": { "value": "Speeding Bullets" } }, "claims": { "P123": [ { "mainsnak": { "datavalue": { "value": { "id": "Q194712" } } } } ] } } } }"""); // Elseworlds imprint
        if (url.Contains("Q9002")) return JsonResponse("""{ "entities": { "Q9002": { "labels": { "en": { "value": "Just Imagine" } }, "claims": { "P136": [ { "mainsnak": { "datavalue": { "value": { "id": "Q16657177" } } } } ] } } } }"""); // alternate history comics genre
        if (url.Contains("Q9003")) return JsonResponse("""{ "entities": { "Q9003": { "labels": { "en": { "value": "Earth-9407" } }, "claims": { "P31": [ { "mainsnak": { "datavalue": { "value": { "id": "Q92343145" } } } } ] } } } }"""); // DC Multiverse world class - fallback hit
        if (url.Contains("Q9004")) return JsonResponse("""{ "entities": { "Q9004": { "labels": { "en": { "value": "Not A Real Place" } }, "claims": { "P31": [ { "mainsnak": { "datavalue": { "value": { "id": "Q515" } } } } ] } } } }"""); // Q515 = city - not a universe class, fallback miss
        if (url.Contains("Q79037")) return JsonResponse("""{ "entities": { "Q79037": { "labels": { "en": { "value": "Spider-Man" } }, "claims": { "P31": [ { "mainsnak": { "datavalue": { "value": { "id": "Q1114461" } } } } ], "P1080": [ { "mainsnak": { "datavalue": { "value": { "id": "Q2246088" } } } } ] } } } }""");
        if (url.Contains("Q48539")) return JsonResponse("""{ "entities": { "Q48539": { "labels": { "en": { "value": "Iron Man" } }, "claims": { "P31": [ { "mainsnak": { "datavalue": { "value": { "id": "Q1114461" } } } } ], "P1080": [ { "mainsnak": { "datavalue": { "value": { "id": "Q2246088" } } } } ] } } } }""");
        if (url.Contains("Q1000")) return JsonResponse("""{ "entities": { "Q1000": { "labels": { "en": { "value": "Not A Hero" } }, "claims": { "P31": [ { "mainsnak": { "datavalue": { "value": { "id": "Q5" } } } } ] } } } }"""); // Q5 = human, not comics character
        if (url.Contains("Q2246088")) return JsonResponse("""{ "entities": { "Q2246088": { "labels": { "en": { "value": "Earth-616" } }, "claims": {} } } }""");

        return JsonResponse("""{ "entities": {} }""");
    })));

    [Fact]
    public async Task SingleResolvedCharacter_ProducesWeakSuggestion()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithCharacters(context, "Amazing Spider-Man", "Spider-Man");

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Q2246088", suggestion.WikidataQid);
        Assert.Equal("Earth-616", suggestion.UniverseLabel);
        Assert.Equal(FormatSignalStrength.Weak, suggestion.Strength);
    }

    [Fact]
    public async Task TwoAgreeingCharacters_ProducesStrongSuggestion()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithCharacters(context, "Avengers", "Spider-Man", "Iron Man");

        var suggestion = Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        Assert.Equal(FormatSignalStrength.Strong, suggestion.Strength);
    }

    [Fact]
    public async Task OnSuggestionFound_FiresOncePerSuggestion_MatchingFinalResult()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithCharacters(context, "Amazing Spider-Man", "Spider-Man");

        var found = new List<ContinuitySuggestion>();
        var result = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None, onSuggestionFound: found.Add);

        var suggestion = Assert.Single(found);
        Assert.Equal(result.Single(), suggestion);
    }

    [Fact]
    public async Task CharacterFailingP31Filter_ProducesNoSuggestion_AndIsNegativeCached()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithCharacters(context, "Mystery Series", "Not A Hero");

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        Assert.Empty(suggestions);
        Assert.Single(context.ContinuityCharacterLookupNegativeCaches);
    }

    // --- Elseworlds/non-canon exclusion (docs/superpowers/specs/2026-09-17-storyevent-continuity-
    // autopopulate-design.md follow-up) - a one-off alternate-timeline story must never be
    // suggested for a mainstream Continuity even when its characters would otherwise vote for one. ---

    [Fact]
    public async Task LocallyMarkedNonCanonSeries_ByName_IsExcluded_WithoutAnyNetworkCall()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        // "Spider-Man" alone would normally produce a real Weak suggestion (see the first test in
        // this file) - the marker in the series name must short-circuit before that ever runs.
        SeedSeriesWithCharacters(context, "Amazing Spider-Man: Elseworlds Special", "Spider-Man");

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        Assert.Empty(suggestions);
        // No character lookup ever ran, so no negative-cache row exists either - proves the local
        // marker check ran first and skipped the series entirely, not just filtered its result.
        Assert.Empty(context.ContinuityCharacterLookupNegativeCaches);
    }

    [Fact]
    public async Task LocallyMarkedNonCanonSeries_BySeriesGroup_IsExcluded()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Amazing Spider-Man" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Characters = "Spider-Man", SeriesGroup = "What If" };
        context.Issues.Add(issue);
        context.SaveChanges();
        CharacterResolver.SyncFromIssue(context, issue.Id);

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task WikidataFlaggedElseworldsImprint_IsExcluded_EvenWithoutLocalMarker()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        // Neither the series name nor SeriesGroup carries a local marker - only the fake Wikidata
        // work item's own P123 (publisher) claim names the Elseworlds imprint (Q194712).
        SeedSeriesWithCharacters(context, "Speeding Bullets", "Spider-Man");

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task WikidataFlaggedAlternateHistoryGenre_IsExcluded()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithCharacters(context, "Just Imagine", "Spider-Man");

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        Assert.Empty(suggestions);
    }

    // --- Location-tag universe matching (user-recommended follow-up) - a Location value that
    // literally names a universe designation is a more explicit, higher-confidence signal than
    // voting on characters, and should short-circuit that path entirely. ---

    private static void SeedSeriesWithLocationAndNoCharacters(PaperbunkrDbContext context, string seriesName, string location, out int seriesId)
    {
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", Locations = location });
        context.SaveChanges();
        seriesId = series.Id;
    }

    [Fact]
    public async Task LocationTagInWhitelist_ProducesStrongSuggestion_WithoutAnyCharacterVoting()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        // No Characters at all - if this produces a suggestion, it can only be from the Location
        // tag, proving the whitelist match works standalone without character voting.
        SeedSeriesWithLocationAndNoCharacters(context, "Amazing Spider-Man", "Earth-616", out _);

        var suggestion = Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        Assert.Equal("Q2246088", suggestion.WikidataQid);
        Assert.Equal("Earth-616", suggestion.UniverseLabel);
        Assert.Equal(FormatSignalStrength.Strong, suggestion.Strength);
        Assert.Contains("Location tag", suggestion.Reason);
    }

    [Fact]
    public async Task LocationTagUnknown_FallsBackToWikidataSearch_AndMatchesDcMultiverseWorldClass()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithLocationAndNoCharacters(context, "Some Series", "Earth-9407", out _);

        var suggestion = Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        Assert.Equal("Q9003", suggestion.WikidataQid);
        Assert.Equal(FormatSignalStrength.Strong, suggestion.Strength);
    }

    [Fact]
    public async Task LocationTagFallbackMiss_FallsThroughToCharacterVoting()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Amazing Spider-Man" };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", Locations = "Not A Real Place", Characters = "Spider-Man" });
        context.SaveChanges();
        CharacterResolver.SyncFromIssue(context, context.Issues.Single().Id);

        // "Not A Real Place" resolves to a real Wikidata item (Q9004) but fails the universe-class
        // check (it's a city) - the resolver should fall through to the normal character path
        // rather than treating "found *some* Wikidata item" as good enough.
        var suggestion = Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        Assert.Equal("Q2246088", suggestion.WikidataQid);
        Assert.Equal(FormatSignalStrength.Weak, suggestion.Strength); // single-character vote, not a location match
    }

    [Fact]
    public async Task LocationTagMatch_DismissedPair_IsExcluded()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithLocationAndNoCharacters(context, "Amazing Spider-Man", "Earth-616", out int seriesId);
        ContinuityWikidataMatchResolver.Dismiss(context, seriesId, "Q2246088");

        Assert.Empty(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));
    }

    [Fact]
    public async Task LocationTagFandomKeyEntry_ProducesStrongSuggestion_WithNoWikidataQid()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        // "Earth-928" has no real Wikidata item (confirmed during design) - this is the
        // fandom-key-only path, no network call needed for the match itself.
        SeedSeriesWithLocationAndNoCharacters(context, "Spider-Man 2099", "Earth-928", out _);

        var suggestion = Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        Assert.Null(suggestion.WikidataQid);
        Assert.Equal("Earth-928", suggestion.FandomKey);
        Assert.Equal(FormatSignalStrength.Strong, suggestion.Strength);
        Assert.NotNull(suggestion.UniverseDescription);
    }

    [Fact]
    public async Task LocationTagFandomKeyAlias_ResolvesToSameKeyAsCanonicalForm()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        // "Earth-Z" is the common alias for Earth-2149 (Marvel Zombies) - both must resolve to the
        // identical FandomKey so dismissal/dedup treats them as the same universe.
        SeedSeriesWithLocationAndNoCharacters(context, "Marvel Zombies", "Earth-Z", out _);

        var suggestion = Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        Assert.Equal("Earth-2149", suggestion.FandomKey);
    }

    [Fact]
    public async Task LocationTagFandomKeyMatch_DismissedPair_IsExcluded()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeriesWithLocationAndNoCharacters(context, "Spider-Man 2099", "Earth-928", out int seriesId);
        ContinuityWikidataMatchResolver.DismissFandom(context, seriesId, "Earth-928");

        Assert.Empty(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        ContinuityWikidataMatchResolver.RestoreFandom(context, seriesId, "Earth-928");
        Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));
    }

    [Fact]
    public async Task FullScan_CapsAtTwentySeriesPerRun()
    {
        // Real bug reported after v1 shipped: an uncapped full scan against a large library ran for
        // the length of the whole app session with no feedback. Every series here has no characters
        // at all, so this asserts the CAP itself (only 20 series get processed), not a Wikidata
        // outcome - if there were no cap, GetTopCharactersForSeries would just be called 30 times
        // returning empty lists (cheap), so this test would pass "by accident" without a real cap
        // unless we count something that only happens per-processed-series. CharacterAppearances are
        // seeded per series instead, and the negative cache count after the run proves how many
        // series's characters were actually looked up.
        using var context = new PaperbunkrDbContext(_dbOptions);
        for (int i = 0; i < 30; i++)
        {
            SeedSeriesWithCharacters(context, $"Series {i}", $"Not A Hero {i}");
        }

        await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None);

        // One negative-cache row per distinct character that failed the P31 filter - since every
        // series here uses a different Character row (SyncFromIssue creates one per series), the
        // count of negative-cached characters equals the count of series actually processed.
        Assert.Equal(20, context.ContinuityCharacterLookupNegativeCaches.Count());
    }

    [Fact]
    public async Task FullScan_CapToBoundedRunFalse_ProcessesEveryEligibleSeries()
    {
        // The uncap fix for the "re-clicking Check Wikidata drops earlier suggestions" bug -
        // capToBoundedRun: false must process all 30 series in one call, not stop at 20.
        using var context = new PaperbunkrDbContext(_dbOptions);
        for (int i = 0; i < 30; i++)
        {
            SeedSeriesWithCharacters(context, $"Series {i}", $"Not A Hero {i}");
        }

        await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None, capToBoundedRun: false);

        Assert.Equal(30, context.ContinuityCharacterLookupNegativeCaches.Count());
    }

    [Fact]
    public async Task DismissedSuggestion_IsExcludedFromFutureScans()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeriesWithCharacters(context, "Amazing Spider-Man", "Spider-Man");

        ContinuityWikidataMatchResolver.Dismiss(context, seriesId, "Q2246088");

        Assert.Empty(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));

        ContinuityWikidataMatchResolver.Restore(context, seriesId, "Q2246088");
        Assert.Single(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));
    }

    [Fact]
    public async Task SeriesAlreadyInAContinuity_IsExcludedFromScan()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeriesWithCharacters(context, "Amazing Spider-Man", "Spider-Man");

        var continuity = new Continuity { Name = "Earth-616", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Continuities.Add(continuity);
        context.SaveChanges();
        context.ContinuityMemberships.Add(new ContinuityMembership { ContinuityId = continuity.Id, SeriesId = seriesId, SortOrder = 0 });
        context.SaveChanges();

        Assert.Empty(await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None));
    }

    [Fact]
    public async Task OnlySeriesId_ScopesScanToOneSeries()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int targetSeriesId = SeedSeriesWithCharacters(context, "Amazing Spider-Man", "Spider-Man");
        SeedSeriesWithCharacters(context, "Invincible Iron Man", "Iron Man");

        var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, FakeWikidata(), CancellationToken.None, onlySeriesId: targetSeriesId);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(targetSeriesId, suggestion.Series.Id);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request, cancellationToken));
    }
}

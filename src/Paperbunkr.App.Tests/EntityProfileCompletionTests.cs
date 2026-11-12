using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

public class EntityProfileCompletionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pb_profiles_{Guid.NewGuid():N}.db");
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private PaperbunkrDbContext Context() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options);

    public EntityProfileCompletionTests()
    {
        using var context = Context();
        context.Database.EnsureCreated();
        context.Characters.Add(new Character { Id = 1, Name = "Storm" });
        context.ComicMetadataExternalIds.AddRange(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character, EntityId = 1, Provider = ComicProvider.Metron, ExternalId = "1" },
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character, EntityId = 1, Provider = ComicProvider.ComicVine, ExternalId = "2" });
        context.SaveChanges();
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private sealed class Source(Func<CancellationToken, Task<EntityProfile?>> fetch) : IEntityProfileSource
    {
        public Task<EntityProfile?> GetEntityProfileAsync(ComicMetadataEntityKind kind, string id, CancellationToken ct) => fetch(ct);
    }
    private EntityProfileCompletion Completion(Func<ComicProvider, IEntityProfileSource?> source, Func<string, CancellationToken, Task<bool>>? download = null) =>
        new(Context, (_, p) => source(p), download ?? ((_, _) => Task.FromResult(true)), () => Now);
    private static IEntityProfileSource Profile(string? image) => new Source(_ => Task.FromResult<EntityProfile?>(new(image, "Bio", new[] { "Ororo" })));
    private static IEntityProfileSource Miss() => new Source(_ => Task.FromResult<EntityProfile?>(null));
    private static IEntityProfileSource Failure() => new Source(_ => Task.FromException<EntityProfile?>(new HttpRequestException("Offline")));

    [Fact]
    public async Task PrefersMetron_AndKeepsCustomImage()
    {
        using (var context = Context()) { context.Characters.Single().CustomImagePath = "storm.jpg"; context.SaveChanges(); }
        var calls = new List<ComicProvider>();
        var result = await Completion(p => { calls.Add(p); return Profile("https://image"); }).CompleteAsync(ComicMetadataEntityKind.Character, 1, default);
        Assert.Equal(EntityProfileOutcome.Checked, result);
        Assert.Equal(new[] { ComicProvider.Metron }, calls);
        using var verify = Context();
        var character = verify.Characters.Single();
        Assert.Equal(Now, character.ImageCheckedAt);
        Assert.Equal(EntityImageSource.Custom, character.ImageSource);
        Assert.Equal("storm.jpg", character.CustomImagePath);
        Assert.Equal("Bio", character.Description);
        Assert.Equal("Ororo", character.Aliases);
    }

    [Fact]
    public async Task ComicVineFillsMissingAliases_WithoutReplacingTheMetronImageOrBio()
    {
        var downloads = new List<string>();
        var completion = Completion(provider => new Source(_ => Task.FromResult<EntityProfile?>(provider == ComicProvider.Metron
            ? new EntityProfile("https://metron", "Metron biography", Array.Empty<string>())
            : new EntityProfile("https://cv", "CV biography", new[] { "Ororo" }))), (url, _) => { downloads.Add(url); return Task.FromResult(true); });
        Assert.Equal(EntityProfileOutcome.Checked, await completion.CompleteAsync(ComicMetadataEntityKind.Character, 1, default));
        using var context = Context(); var entity = context.Characters.Single();
        Assert.Equal("Metron biography", entity.Description); Assert.Equal("Ororo", entity.Aliases);
        Assert.Equal("https://metron", entity.ImageUrl); Assert.Equal(new[] { "https://metron" }, downloads);
    }

    [Fact]
    public async Task FallbackCompletesAfterMetronFails()
    {
        Assert.Equal(EntityProfileOutcome.Checked, await Completion(p => p == ComicProvider.Metron ? Failure() : Profile("https://cv"))
            .CompleteAsync(ComicMetadataEntityKind.Character, 1, default));
        using var context = Context();
        Assert.Equal(EntityImageSource.ComicVine, context.Characters.Single().ImageSource);
    }

    [Theory]
    [InlineData(false, false, EntityProfileOutcome.Checked)]
    [InlineData(true, false, EntityProfileOutcome.Failed)]
    [InlineData(false, true, EntityProfileOutcome.Failed)]
    [InlineData(true, true, EntityProfileOutcome.Failed)]
    public async Task MissesStamp_ButPartialAndTotalFailuresRetry(bool metronFails, bool cvFails, EntityProfileOutcome expected)
    {
        var result = await Completion(p => (p == ComicProvider.Metron ? metronFails : cvFails) ? Failure() : Miss())
            .CompleteAsync(ComicMetadataEntityKind.Character, 1, default);
        Assert.Equal(expected, result);
        using var context = Context();
        Assert.Equal(expected == EntityProfileOutcome.Checked ? Now : (DateTime?)null, context.Characters.Single().ImageCheckedAt);
    }

    [Fact]
    public async Task FailedDownload_DoesNotStamp_EvenAfterAMiss()
    {
        var completion = Completion(p => p == ComicProvider.Metron ? Miss() : Profile("https://cv"), (_, _) => Task.FromResult(false));
        Assert.Equal(EntityProfileOutcome.Failed, await completion.CompleteAsync(ComicMetadataEntityKind.Character, 1, default));
        using var context = Context();
        Assert.Null(context.Characters.Single().ImageCheckedAt);
    }

    [Fact]
    public async Task ThirtyDayRecheck_AndNoCredentialsDoesNotStamp()
    {
        using (var context = Context()) { context.Characters.Single().ImageCheckedAt = Now.AddDays(-29); context.SaveChanges(); }
        var completion = Completion(_ => throw new Exception("Should not fetch"));
        Assert.Equal(EntityProfileOutcome.NotDue, await completion.CompleteAsync(ComicMetadataEntityKind.Character, 1, default));
        using (var context = Context()) { context.Characters.Single().ImageCheckedAt = Now.AddDays(-31); context.SaveChanges(); }
        Assert.Equal(EntityProfileOutcome.NoSource, await Completion(_ => null).CompleteAsync(ComicMetadataEntityKind.Character, 1, default));
        Assert.Equal(EntityProfileOutcome.Checked, await Completion(_ => Miss()).CompleteAsync(ComicMetadataEntityKind.Character, 1, default));
    }

    [Fact]
    public async Task CancellationPropagatesWithoutStamping()
    {
        var source = new Source(ct => Task.FromCanceled<EntityProfile?>(ct));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Completion(_ => source).CompleteAsync(ComicMetadataEntityKind.Character, 1, cts.Token));
        using var context = Context();
        Assert.Null(context.Characters.Single().ImageCheckedAt);
    }

    // ---- manga creators (AniList, then MangaDex, via the series they are credited on) and locations (ComicVine) ----

    private sealed class Staff(Func<ExternalMetadataProvider, string, string, EntityProfile?> find) : Paperbunkr.Data.Metadata.IMangaStaffSource
    {
        public List<(ExternalMetadataProvider Provider, string SeriesId, string Name)> Calls { get; } = new();
        public Task<EntityProfile?> FindAsync(ExternalMetadataProvider provider, string seriesExternalId, string personName, CancellationToken ct)
        {
            Calls.Add((provider, seriesExternalId, personName));
            return Task.FromResult(find(provider, seriesExternalId, personName));
        }
    }

    private int SeedMangaCreator(params (ExternalMetadataProvider Provider, string Id)[] links)
    {
        using var context = Context();
        var series = new Series { Name = "One Piece", ContentType = ContentType.Manga };
        var creator = new Creator { Name = "Oda Eiichiro" };
        var issue = new Issue { Series = series, Number = "1", Writer = "Oda Eiichiro" };
        context.AddRange(series, creator, issue);
        context.SaveChanges();
        context.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = issue.Id, Role = "Writer" });
        foreach (var (provider, id) in links) context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = series.Id, Provider = provider, ExternalId = id });
        context.SaveChanges();
        return creator.Id;
    }

    [Fact]
    public async Task MangaCreator_WithoutComicIds_GetsTheAniListStaffProfile_FromTheirLinkedSeries()
    {
        int creatorId = SeedMangaCreator((ExternalMetadataProvider.AniList, "21"), (ExternalMetadataProvider.MangaDex, "a1c7c817"));
        var staff = new Staff((provider, _, _) => provider == ExternalMetadataProvider.AniList
            ? new EntityProfile("https://s4.anilist.co/file/anilistcdn/staff/large/oda.png", "Creator of One Piece.", new[] { "尾田栄一郎" })
            : new EntityProfile("https://mangadex/oda.jpg", "MangaDex bio", Array.Empty<string>()));
        var completion = new EntityProfileCompletion(Context, (_, _) => null, (_, _) => Task.FromResult(true), () => Now, staff);

        var result = await completion.CompleteAsync(ComicMetadataEntityKind.Creator, creatorId, default);

        Assert.Equal(EntityProfileOutcome.Checked, result);
        Assert.Equal((ExternalMetadataProvider.AniList, "21", "Oda Eiichiro"), staff.Calls[0]);
        using var verify = Context();
        var creator = verify.Creators.Single(c => c.Id == creatorId);
        Assert.Equal(EntityImageSource.AniList, creator.ImageSource);
        Assert.Equal("https://s4.anilist.co/file/anilistcdn/staff/large/oda.png", creator.ImageUrl);
        Assert.Equal("Creator of One Piece.", creator.Description);
        Assert.Equal("尾田栄一郎", creator.Aliases);
        Assert.Equal(Now, creator.ImageCheckedAt);
    }

    [Fact]
    public async Task MangaCreator_NoAniListMatch_FallsBackToMangaDex_AndNoMatchAnywhereIsStamped()
    {
        int creatorId = SeedMangaCreator((ExternalMetadataProvider.AniList, "21"), (ExternalMetadataProvider.MangaDex, "a1c7c817"));
        var staff = new Staff((provider, _, _) => provider == ExternalMetadataProvider.MangaDex ? new EntityProfile("https://mangadex/oda.jpg", null, Array.Empty<string>()) : null);
        var completion = new EntityProfileCompletion(Context, (_, _) => null, (_, _) => Task.FromResult(true), () => Now, staff);

        Assert.Equal(EntityProfileOutcome.Checked, await completion.CompleteAsync(ComicMetadataEntityKind.Creator, creatorId, default));

        using (var verify = Context())
        {
            var creator = verify.Creators.Single(c => c.Id == creatorId);
            Assert.Equal(EntityImageSource.MangaDex, creator.ImageSource);
            Assert.Equal("https://mangadex/oda.jpg", creator.ImageUrl);
        }

        int other = SeedMangaCreator((ExternalMetadataProvider.AniList, "99"));
        var none = new EntityProfileCompletion(Context, (_, _) => null, (_, _) => Task.FromResult(true), () => Now, new Staff((_, _, _) => null));
        Assert.Equal(EntityProfileOutcome.Checked, await none.CompleteAsync(ComicMetadataEntityKind.Creator, other, default));
        using var check = Context();
        Assert.Equal(Now, check.Creators.Single(c => c.Id == other).ImageCheckedAt);
        Assert.Null(check.Creators.Single(c => c.Id == other).ImageUrl);
    }

    // ---- id discovery for entities with no provider id (2026-11-12: most of the user's unlinked creators got one this way) ----

    private sealed class IssueAwareSource(Func<int, ComicVineIssueDetails?> issue, Func<string, EntityProfile?> profile) : IEntityProfileSource, IComicVineIssueDetailsSource
    {
        public List<int> IssueCalls { get; } = new();
        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken ct) { IssueCalls.Add(issueId); return Task.FromResult(issue(issueId)); }
        public Task<EntityProfile?> GetEntityProfileAsync(ComicMetadataEntityKind kind, string id, CancellationToken ct) => Task.FromResult(profile(id));
    }

    private static ComicVineIssueDetails Details(params ComicVineCredit[] credits) => new(1, 1, "Series", "1", null, null, new(2024, 1, 1), new(2024, 1, 1), null,
        Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), credits);

    private int SeedUnlinkedCreator(string? metronIssueId)
    {
        using var context = Context();
        var series = new Series { Name = "Green Lanterns" };
        var issue = new Issue { Series = series, Number = "1", Writer = "Sam Humphries" };
        var creator = new Creator { Name = "Sam Humphries" };
        context.AddRange(series, issue, creator);
        context.SaveChanges();
        context.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = issue.Id, Role = "Writer" });
        if (metronIssueId is not null)
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.Metron, ExternalId = metronIssueId });
        context.SaveChanges();
        return creator.Id;
    }

    [Fact]
    public async Task UnlinkedCreator_GetsItsIdFromOneOfItsIssues_ThenItsProfile()
    {
        int creatorId = SeedUnlinkedCreator("58751");
        var metron = new IssueAwareSource(
            _ => Details(new ComicVineCredit("Sam Humphries", "Writer", CreatorExternalId: 1555)),
            id => id == "1555" ? new EntityProfile("https://static.metron.cloud/humphries.jpg", "Writer.", Array.Empty<string>()) : null);
        var completion = new EntityProfileCompletion(Context, (_, p) => p == ComicProvider.Metron ? metron : null, (_, _) => Task.FromResult(true), () => Now,
            new Staff((_, _, _) => null));

        var outcome = await completion.CompleteAsync(ComicMetadataEntityKind.Creator, creatorId, default);

        Assert.Equal(EntityProfileOutcome.Checked, outcome);
        Assert.Equal(new[] { 58751 }, metron.IssueCalls);
        using var verify = Context();
        Assert.Contains(verify.ComicMetadataExternalIds, e => e.EntityKind == ComicMetadataEntityKind.Creator && e.EntityId == creatorId && e.ExternalId == "1555");
        var creator = verify.Creators.Single(c => c.Id == creatorId);
        Assert.Equal("https://static.metron.cloud/humphries.jpg", creator.ImageUrl);
        Assert.Equal(EntityImageSource.Metron, creator.ImageSource);
    }

    [Fact]
    public async Task UnlinkedEntity_WithNoProviderIssue_IsNoSource_AndAFailedIssueLookupIsAFailure()
    {
        int noIssue = SeedUnlinkedCreator(null);
        var completion = new EntityProfileCompletion(Context, (_, _) => null, (_, _) => Task.FromResult(true), () => Now, new Staff((_, _, _) => null));
        Assert.Equal(EntityProfileOutcome.NoSource, await completion.CompleteAsync(ComicMetadataEntityKind.Creator, noIssue, default));

        int withIssue = SeedUnlinkedCreator("99");
        var offline = new IssueAwareSource(_ => throw new HttpRequestException("offline"), _ => null);
        var failing = new EntityProfileCompletion(Context, (_, p) => p == ComicProvider.Metron ? offline : null, (_, _) => Task.FromResult(true), () => Now,
            new Staff((_, _, _) => null));
        Assert.Equal(EntityProfileOutcome.Failed, await failing.CompleteAsync(ComicMetadataEntityKind.Creator, withIssue, default));
        using var verify = Context();
        Assert.Null(verify.Creators.Single(c => c.Id == withIssue).ImageCheckedAt); // retried next time
    }

    [Fact]
    public async Task ADownloadThatThrows_IsAFailure_NotACrash()
    {
        var completion = Completion(_ => Profile("https://image"), (_, _) => throw new InvalidOperationException("decoder unavailable"));

        var outcome = await completion.CompleteAsync(ComicMetadataEntityKind.Character, 1, default);

        Assert.Equal(EntityProfileOutcome.Failed, outcome);
        using var verify = Context();
        Assert.Null(verify.Characters.Single().ImageUrl);
        Assert.Null(verify.Characters.Single().ImageCheckedAt);
    }

    [Fact]
    public async Task Location_UsesItsComicVineId_AndNeverTheMangaSources()
    {
        int locationId;
        using (var context = Context())
        {
            var location = new Location { Name = "Westchester" };
            context.Locations.Add(location); context.SaveChanges(); locationId = location.Id;
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Location, EntityId = locationId, Provider = ComicProvider.ComicVine, ExternalId = "41226" });
            context.SaveChanges();
        }

        var staff = new Staff((_, _, _) => throw new InvalidOperationException("locations never ask manga sources"));
        var asked = new List<ComicProvider>();
        var completion = new EntityProfileCompletion(Context, (_, p) => { asked.Add(p); return Profile("https://comicvine/westchester.jpg"); }, (_, _) => Task.FromResult(true), () => Now, staff);

        Assert.True(EntityProfileCompletion.Supports(ComicMetadataEntityKind.Location));
        Assert.Equal(EntityProfileOutcome.Checked, await completion.CompleteAsync(ComicMetadataEntityKind.Location, locationId, default));

        Assert.Equal(new[] { ComicProvider.ComicVine }, asked);
        Assert.Empty(staff.Calls);
        using var verify = Context();
        var stored = verify.Locations.Single(l => l.Id == locationId);
        Assert.Equal(EntityImageSource.ComicVine, stored.ImageSource);
        Assert.Equal("https://comicvine/westchester.jpg", stored.ImageUrl);
    }
}

using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

public sealed class ComicSeriesSummaryBackfillTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("comic-summary-").FullName;
    private string DbPath => Path.Combine(_root, "library.db");

    private PaperbunkrDbContext Context()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={DbPath}").Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task FillsExistingLinkedComics_AndFallsBackToSecondProvider()
    {
        int target;
        using (var db = Context())
        {
            var comic = new Series { Name = "A", ContentType = ContentType.Comic };
            var existing = new Series { Name = "B", ContentType = ContentType.Comic, Summary = "My summary" };
            var manga = new Series { Name = "C", ContentType = ContentType.Manga };
            db.Series.AddRange(comic, existing, manga);
            db.SaveChanges();
            target = comic.Id;
            db.ComicMetadataExternalIds.AddRange(
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = comic.Id, Provider = ComicProvider.Metron, ExternalId = "11" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = comic.Id, Provider = ComicProvider.ComicVine, ExternalId = "22" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = existing.Id, Provider = ComicProvider.ComicVine, ExternalId = "33" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = manga.Id, Provider = ComicProvider.ComicVine, ExternalId = "44" });
            db.SaveChanges();
        }

        var metron = new FakeClient { Description = null };
        var comicVine = new FakeClient { Description = "The series synopsis" };
        var result = await ComicSeriesSummaryBackfill.RunAsync(Context,
            provider => provider == ComicProvider.Metron ? metron : comicVine);

        using var verify = Context();
        Assert.Equal("The series synopsis", verify.Series.Single(s => s.Id == target).Summary);
        Assert.Equal("My summary", verify.Series.Single(s => s.Name == "B").Summary);
        Assert.Null(verify.Series.Single(s => s.Name == "C").Summary);
        Assert.Equal(new[] { 11 }, metron.Requested);
        Assert.Equal(new[] { 22 }, comicVine.Requested);
        Assert.Equal(1, result.Filled);
    }

    [Fact]
    public async Task DoesNotOverwriteAnEditMadeWhileFetching()
    {
        int seriesId;
        using (var db = Context())
        {
            var series = new Series { Name = "A", ContentType = ContentType.Comic };
            db.Series.Add(series);
            db.SaveChanges();
            seriesId = series.Id;
            db.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
            {
                EntityKind = ComicMetadataEntityKind.Series, EntityId = seriesId,
                Provider = ComicProvider.ComicVine, ExternalId = "22",
            });
            db.SaveChanges();
        }

        var client = new FakeClient
        {
            Description = "Provider text",
            OnGet = () =>
            {
                using var db = Context();
                db.Series.Single(s => s.Id == seriesId).Summary = "User text";
                db.SaveChanges();
            },
        };

        var result = await ComicSeriesSummaryBackfill.RunAsync(Context, _ => client);
        using var verify = Context();
        Assert.Equal("User text", verify.Series.Single(s => s.Id == seriesId).Summary);
        Assert.Equal(0, result.Filled);
    }

    [Fact]
    public async Task ReportsUnlinkedUnavailableAndEmptyDescriptions_WithoutInventingText()
    {
        using (var db = Context())
        {
            var noLink = new Series { Name = "No link", ContentType = ContentType.Comic };
            var noCredential = new Series { Name = "No credential", ContentType = ContentType.Comic };
            var noDescription = new Series { Name = "No description", ContentType = ContentType.Comic };
            var locked = new Series { Name = "Locked", ContentType = ContentType.Comic, LockedFields = "Summary" };
            db.Series.AddRange(noLink, noCredential, noDescription, locked);
            db.SaveChanges();
            db.ComicMetadataExternalIds.AddRange(
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = noCredential.Id, Provider = ComicProvider.Metron, ExternalId = "11" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = noDescription.Id, Provider = ComicProvider.ComicVine, ExternalId = "22" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = locked.Id, Provider = ComicProvider.ComicVine, ExternalId = "33" });
            db.SaveChanges();
        }

        var client = new FakeClient { Description = null };
        var result = await ComicSeriesSummaryBackfill.RunAsync(Context,
            provider => provider == ComicProvider.ComicVine ? client : null);

        Assert.Equal((3, 0, 1, 1, 1),
            (result.Candidates, result.Filled, result.WithoutLink, result.WithoutCredentials, result.WithoutDescription));
        Assert.Equal(new[] { 22 }, client.Requested);
        using var verify = Context();
        Assert.All(verify.Series.ToList(), s => Assert.True(string.IsNullOrWhiteSpace(s.Summary)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeClient : IComicVineClient
    {
        public string? Description { get; init; }
        public Action? OnGet { get; init; }
        public List<int> Requested { get; } = new();
        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken)
        {
            Requested.Add(volumeId);
            OnGet?.Invoke();
            return Task.FromResult<ComicVineVolume?>(new ComicVineVolume(volumeId, "A", null, null, 0, null)
            {
                Description = Description,
            });
        }
        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComicVineVolume>>(Array.Empty<ComicVineVolume>());
        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComicVineIssue>>(Array.Empty<ComicVineIssue>());
    }
}

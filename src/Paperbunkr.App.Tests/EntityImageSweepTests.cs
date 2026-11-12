using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

public class EntityImageSweepTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pb_sweep_{Guid.NewGuid():N}.db");
    private PaperbunkrDbContext Context() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options);
    public EntityImageSweepTests()
    {
        using var context = Context();
        context.Database.EnsureCreated();
        var series = new Series { Name = "Uncanny" };
        var issue1 = new Issue { Series = series, Number = "1" };
        var issue2 = new Issue { Series = series, Number = "2" };
        for (int i = 1; i <= 23; i++)
        {
            context.Characters.Add(new Character { Id = i, Name = $"Character {i}" });
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character,
                EntityId = i, Provider = ComicProvider.Metron, ExternalId = i.ToString() });
        }
        context.CharacterAppearances.AddRange(new CharacterAppearance { CharacterId = 23, Issue = issue1 },
            new CharacterAppearance { CharacterId = 23, Issue = issue2 });
        context.SaveChanges();
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_path); }
    private sealed class Source(bool fail, List<string> calls) : IEntityProfileSource
    {
        public Task<EntityProfile?> GetEntityProfileAsync(ComicMetadataEntityKind kind, string id, CancellationToken ct)
        {
            calls.Add(id);
            return fail ? Task.FromException<EntityProfile?>(new HttpRequestException("Offline")) : Task.FromResult<EntityProfile?>(null);
        }
    }

    [Fact]
    public async Task MostAppearancesFirst_AndAtMostTwentyPerRun()
    {
        var calls = new List<string>();
        var completion = new EntityProfileCompletion(Context, (_, _) => new Source(false, calls));
        var result = await new EntityImageSweep(Context, completion).RunAsync(null, default);
        Assert.Equal(20, result.Checked);
        Assert.Equal(20, calls.Count);
        Assert.Equal("23", calls[0]);
        using var context = Context();
        Assert.Equal(3, EntityImageSweep.Candidates(context, 100).Count);
        Assert.False(result.StoppedOnFailure);
    }

    [Fact]
    public async Task StopsOnFirstProviderFailure_WithoutStamping()
    {
        var calls = new List<string>();
        var completion = new EntityProfileCompletion(Context, (_, _) => new Source(true, calls));
        var result = await new EntityImageSweep(Context, completion).RunAsync(null, default);
        Assert.True(result.StoppedOnFailure);
        Assert.Equal(0, result.Checked);
        Assert.Single(calls);
        using var context = Context();
        Assert.Equal(23, EntityImageSweep.Candidates(context, 100).Count);
    }
}

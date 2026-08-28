using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ContinuityResolver"/> (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase4a-continuity-design.md) against a real SQLite database - same rationale as
/// <see cref="MediaRelationResolverTests"/>: this reads real persisted rows and the implicit
/// skip-navigation join table, not pure in-memory objects.
/// </summary>
public class ContinuityResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ContinuityResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuity_test_{Guid.NewGuid():N}.db");
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

    private static int SeedSeries(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void GetOrCreate_NewName_CreatesRow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");

        Assert.Equal("Earth-616", continuity.Name);
        Assert.Single(context.Continuities);
    }

    [Fact]
    public void GetOrCreate_CaseInsensitiveMatch_ReturnsExistingRow_DoesNotDuplicate()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var first = ContinuityResolver.GetOrCreate(context, "Earth-616");

        var second = ContinuityResolver.GetOrCreate(context, "earth-616");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.Continuities);
    }

    [Fact]
    public void AddSeriesToContinuity_NewPairing_Succeeds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");

        bool added = ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);

        Assert.True(added);
        Assert.Single(ContinuityResolver.GetContinuities(context, seriesId));
    }

    [Fact]
    public void AddSeriesToContinuity_AlreadyMember_NoOp_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);

        bool addedAgain = ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);

        Assert.False(addedAgain);
        Assert.Single(ContinuityResolver.GetContinuities(context, seriesId));
    }

    [Fact]
    public void RemoveSeriesFromContinuity_ExistingMember_RemovesJoinRow_LeavesContinuityAndOtherMembersIntact()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Series A");
        int seriesBId = SeedSeries(context, "Series B");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, seriesAId, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, seriesBId, continuity.Id);

        ContinuityResolver.RemoveSeriesFromContinuity(context, seriesAId, continuity.Id);

        Assert.Empty(ContinuityResolver.GetContinuities(context, seriesAId));
        Assert.Single(ContinuityResolver.GetContinuities(context, seriesBId));
        Assert.NotNull(context.Continuities.Find(continuity.Id));
    }

    [Fact]
    public void GetOtherSeriesSharingContinuity_ExcludesQueriedSeries_IncludesOthers()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Series A");
        int seriesBId = SeedSeries(context, "Series B");
        int seriesCId = SeedSeries(context, "Series C"); // not in the continuity
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, seriesAId, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, seriesBId, continuity.Id);

        var result = ContinuityResolver.GetOtherSeriesSharingContinuity(context, seriesAId);

        var other = Assert.Single(result);
        Assert.Equal(seriesBId, other.Id);
        Assert.DoesNotContain(result, s => s.Id == seriesCId);
    }

    [Fact]
    public void GetOtherSeriesSharingContinuity_SharedAcrossTwoContinuities_Deduplicated()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Series A");
        int seriesBId = SeedSeries(context, "Series B");
        var continuityOne = ContinuityResolver.GetOrCreate(context, "Earth-616");
        var continuityTwo = ContinuityResolver.GetOrCreate(context, "Ultimate Universe");
        ContinuityResolver.AddSeriesToContinuity(context, seriesAId, continuityOne.Id);
        ContinuityResolver.AddSeriesToContinuity(context, seriesBId, continuityOne.Id);
        ContinuityResolver.AddSeriesToContinuity(context, seriesAId, continuityTwo.Id);
        ContinuityResolver.AddSeriesToContinuity(context, seriesBId, continuityTwo.Id);

        var result = ContinuityResolver.GetOtherSeriesSharingContinuity(context, seriesAId);

        Assert.Single(result);
    }

    [Fact]
    public void GetOtherSeriesSharingContinuity_NoMembership_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Series A");

        var result = ContinuityResolver.GetOtherSeriesSharingContinuity(context, seriesAId);

        Assert.Empty(result);
    }

    [Fact]
    public void DeletingContinuity_ClearsJoinRows_LeavesSeriesIntact()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);

        context.Continuities.Remove(context.Continuities.Single());
        context.SaveChanges();

        Assert.Empty(context.Continuities);
        Assert.NotNull(context.Series.Find(seriesId));
    }

    [Fact]
    public void Merge_MovesUniqueSeries_DedupesOverlap_DeletesSource()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "Series A");
        int b = SeedSeries(context, "Series B");
        int shared = SeedSeries(context, "Series Shared");

        var source = ContinuityResolver.GetOrCreate(context, "Source");
        var target = ContinuityResolver.GetOrCreate(context, "Target");
        ContinuityResolver.AddSeriesToContinuity(context, a, source.Id);
        ContinuityResolver.AddSeriesToContinuity(context, shared, source.Id);
        ContinuityResolver.AddSeriesToContinuity(context, b, target.Id);
        ContinuityResolver.AddSeriesToContinuity(context, shared, target.Id);

        int carried = ContinuityResolver.Merge(context, source.Id, target.Id);

        Assert.Equal(1, carried); // only Series A is new to the target
        Assert.Null(context.Continuities.Find(source.Id));
        var targetSeries = ContinuityResolver.GetSeriesInContinuity(context, target.Id).Select(s => s.Id).ToHashSet();
        Assert.Equal(new[] { a, b, shared }.ToHashSet(), targetSeries);
        Assert.NotNull(context.Series.Find(a)); // series themselves untouched
    }

    [Fact]
    public void SetMembershipNote_RoundTrips_BlankClears()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);

        ContinuityResolver.SetMembershipNote(context, continuity.Id, seriesId, "  flagship  ");
        Assert.Equal("flagship", context.ContinuityMemberships.Single().Note);

        ContinuityResolver.SetMembershipNote(context, continuity.Id, seriesId, "   ");
        Assert.Null(context.ContinuityMemberships.Single().Note);
    }

    [Fact]
    public void SetMembershipOrder_RewritesSortOrder_GetSeriesReflectsIt()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "Aaa");
        int b = SeedSeries(context, "Bbb");
        int c = SeedSeries(context, "Ccc");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, a, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, b, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, c, continuity.Id);

        ContinuityResolver.SetMembershipOrder(context, continuity.Id, new[] { c, a, b });

        Assert.Equal(new[] { c, a, b }, ContinuityResolver.GetSeriesInContinuity(context, continuity.Id).Select(s => s.Id));
    }

    [Fact]
    public void AddSeriesToContinuity_AppendsAtEndOfSortOrder()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "Zeta");   // name order would put this last
        int b = SeedSeries(context, "Alpha");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, a, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, b, continuity.Id);

        // Insertion order wins over name order once memberships are explicit.
        Assert.Equal(new[] { a, b }, ContinuityResolver.GetSeriesInContinuity(context, continuity.Id).Select(s => s.Id));
    }

    [Fact]
    public void Merge_SameId_IsNoOp()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var c = ContinuityResolver.GetOrCreate(context, "Only");
        Assert.Equal(0, ContinuityResolver.Merge(context, c.Id, c.Id));
        Assert.NotNull(context.Continuities.Find(c.Id));
    }

    [Fact]
    public void DeletingSeries_ClearsJoinRows_LeavesContinuityIntact()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);

        context.Series.Remove(context.Series.Single());
        context.SaveChanges();

        Assert.NotNull(context.Continuities.Find(continuity.Id));
        Assert.Empty(ContinuityResolver.GetSeriesInContinuity(context, continuity.Id));
    }
}

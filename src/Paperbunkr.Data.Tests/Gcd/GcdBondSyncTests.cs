using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.Gcd;

/// <summary><see cref="GcdBondSync"/> (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).</summary>
public class GcdBondSyncTests : GcdLibraryDb
{
    private List<MediaRelation> Relations()
    {
        using var context = NewContext();
        return context.MediaRelations.AsNoTracking().Include(m => m.Evidence).ToList();
    }

    [Theory]
    [InlineData(1, RelationType.Continuation)]
    [InlineData(2, RelationType.Continuation)]
    [InlineData(3, RelationType.Continuation)]
    [InlineData(4, RelationType.Continuation)]
    [InlineData(5, RelationType.Continuation)]
    [InlineData(6, RelationType.Related)]
    [InlineData(7, RelationType.Reboot)]
    public void Bond_BecomesRelation_NewerSeriesIsSource(int bondType, RelationType expected)
    {
        Dump.Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Bond(1, 2, bondType);
        var older = AddMatchedSeries("Incredible Hulk (1968)", 1);
        var newer = AddMatchedSeries("Hulk (2008)", 2);
        using var store = Dump.Open();

        var result = GcdBondSync.Run(Factory, store);

        var relation = Assert.Single(Relations());
        Assert.Equal((newer.Id, older.Id, expected), (relation.SourceSeriesId!.Value, relation.TargetSeriesId!.Value, relation.RelationType));
        var evidence = Assert.Single(relation.Evidence);
        Assert.Equal(RelationEvidenceProvider.Gcd, evidence.Provider);
        Assert.Equal("1", evidence.ProviderSourceId);
        Assert.Equal(new GcdBondSyncResult(1, 0), result);
    }

    [Fact]
    public void BondToSeriesNotOwned_MakesNoRelation()
    {
        Dump.Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Bond(1, 2, 2);
        AddMatchedSeries("Incredible Hulk (1968)", 1);
        using var store = Dump.Open();

        GcdBondSync.Run(Factory, store);

        Assert.Empty(Relations());
    }

    [Fact]
    public void Run_IsIdempotent()
    {
        Dump.Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Bond(1, 2, 2);
        AddMatchedSeries("Incredible Hulk (1968)", 1);
        AddMatchedSeries("Hulk (2008)", 2);
        using var store = Dump.Open();

        GcdBondSync.Run(Factory, store);
        var second = GcdBondSync.Run(Factory, store);

        Assert.Single(Relations());
        Assert.Equal(new GcdBondSyncResult(0, 0), second);
    }

    [Fact]
    public void UserRelation_GainsGcdEvidence_AndKeepsItsOwn_WhenTheBondGoes()
    {
        Dump.Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Bond(1, 2, 2);
        var older = AddMatchedSeries("Incredible Hulk (1968)", 1);
        var newer = AddMatchedSeries("Hulk (2008)", 2);
        using (var context = NewContext())
        {
            // Made by hand, the other way round.
            Assert.True(MediaRelationResolver.TryCreate(context, older.Id, newer.Id, RelationType.Continuation));
        }

        using (var store = Dump.Open())
        {
            GcdBondSync.Run(Factory, store);
            var relation = Assert.Single(Relations());
            Assert.Equal(new[] { RelationEvidenceProvider.User, RelationEvidenceProvider.Gcd }.Order(), relation.Evidence.Select(e => e.Provider).Order());
        }

        // The bond disappears (series re-matched elsewhere): only the GCD evidence goes.
        using (var context = NewContext())
        {
            context.Database.ExecuteSqlRaw($"UPDATE Series SET GcdSeriesId = NULL WHERE Id = {newer.Id}");
        }

        using (var store = GcdDataStore.TryOpen(Dump.ExtractPath)!)
        {
            var result = GcdBondSync.Run(Factory, store);
            var relation = Assert.Single(Relations());
            Assert.Equal(RelationEvidenceProvider.User, Assert.Single(relation.Evidence).Provider);
            Assert.Equal(new GcdBondSyncResult(0, 1), result);
        }
    }

    [Fact]
    public void GcdOnlyRelation_IsRemoved_WhenTheBondGoes()
    {
        Dump.Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Series(3, "Other", 2008, "Marvel").Bond(1, 2, 2);
        AddMatchedSeries("Incredible Hulk (1968)", 1);
        var newer = AddMatchedSeries("Hulk (2008)", 2);
        using var store = Dump.Open();
        GcdBondSync.Run(Factory, store);

        using (var context = NewContext())
        {
            context.Database.ExecuteSqlRaw($"UPDATE Series SET GcdSeriesId = 3 WHERE Id = {newer.Id}");
        }

        GcdBondSync.Run(Factory, store);

        Assert.Empty(Relations());
    }

    [Fact]
    public void DeletedGcdRelation_StaysDeleted()
    {
        Dump.Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Bond(1, 2, 2);
        AddMatchedSeries("Incredible Hulk (1968)", 1);
        AddMatchedSeries("Hulk (2008)", 2);
        using var store = Dump.Open();
        GcdBondSync.Run(Factory, store);

        using (var context = NewContext())
        {
            MediaRelationResolver.Remove(context, context.MediaRelations.Single().Id);
            Assert.Single(context.SeriesRelationDismissals);
        }

        var result = GcdBondSync.Run(Factory, store);

        Assert.Empty(Relations());
        Assert.Equal(new GcdBondSyncResult(0, 0), result);
    }

    [Fact]
    public void DeletingAUserOnlyRelation_LeavesNoDismissal()
    {
        var a = AddSeries("A", null);
        var b = AddSeries("B", null);
        using var context = NewContext();
        MediaRelationResolver.TryCreate(context, a.Id, b.Id, RelationType.Sequel);

        MediaRelationResolver.Remove(context, context.MediaRelations.Single().Id);

        Assert.Empty(context.SeriesRelationDismissals);
    }
}

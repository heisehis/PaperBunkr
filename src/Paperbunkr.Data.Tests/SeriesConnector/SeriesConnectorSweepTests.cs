using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.SeriesConnector;
using Paperbunkr.Data.Tests.StoryEventIdentity;

namespace Paperbunkr.Data.Tests.SeriesConnector;

/// <summary>
/// <see cref="SeriesConnectorSweep"/> end to end on a temp SQLite database (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §5):
/// saving, idempotence, pruning, the evidence it owns and the evidence it must never touch, dismissals and Metron associations. No network.
/// </summary>
public class SeriesConnectorSweepTests : IdentityTestDb
{
    private int Series(string name, params int[] years)
    {
        using var context = NewContext();
        var series = new Series { Name = name, Publisher = "DC Comics" };
        int number = 1;
        foreach (int year in years)
        {
            series.Issues.Add(new Issue { Number = (number++).ToString(), Year = year });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private List<MediaRelation> Relations()
    {
        using var context = NewContext();
        return context.MediaRelations.Include(m => m.Evidence).AsNoTracking().ToList();
    }

    [Fact]
    public void Run_SavesALaterVolume_WithTheReasonAsEvidence_AndASecondRunChangesNothing()
    {
        int old = Series("Superman (2011)", 2011, 2016);
        int @new = Series("Superman (2016)", 2016, 2018);

        var first = SeriesConnectorSweep.Run(Factory);
        var second = SeriesConnectorSweep.Run(Factory);

        Assert.Equal((1, 0), (first.Created, first.Removed));
        Assert.Equal((0, 0, 0), (second.Created, second.Updated, second.Removed));
        Assert.Equal("No new relations", second.Text);
        var relation = Assert.Single(Relations());
        Assert.Equal((@new, old, RelationType.Continuation), (relation.SourceSeriesId!.Value, relation.TargetSeriesId!.Value, relation.RelationType));
        var evidence = Assert.Single(relation.Evidence);
        Assert.Equal((RelationEvidenceProvider.Inferred, "Later volume"), (evidence.Provider, evidence.ProviderRelationType));
    }

    [Fact]
    public void Run_PrunesInferredEvidenceThatNoLongerHolds_AndDeletesTheEmptyRelation()
    {
        int a = Series("Superman (2011)", 2011, 2016);
        Series("Superman (2016)", 2016, 2018);
        SeriesConnectorSweep.Run(Factory);

        using (var context = NewContext())
        {
            context.Series.Single(s => s.Id == a).Name = "Superboy (2011)";
            context.SaveChanges();
        }

        var result = SeriesConnectorSweep.Run(Factory);

        Assert.Equal(1, result.Removed);
        Assert.Empty(Relations());
    }

    [Fact]
    public void Run_NeverTouchesGcdOrYourOwnEvidence_AndAddsInferredEvidenceBesideGcd()
    {
        int old = Series("Superman (2011)", 2011, 2016);
        int @new = Series("Superman (2016)", 2016, 2018);
        using (var context = NewContext())
        {
            var gcd = new MediaRelation { SourceSeriesId = @new, TargetSeriesId = old, RelationType = RelationType.Continuation };
            gcd.Evidence.Add(new RelationEvidence { Provider = RelationEvidenceProvider.Gcd, ProviderRelationType = "major_name_numbering_continues", Confidence = 0.95m });
            context.MediaRelations.Add(gcd);
            context.SaveChanges();
        }

        SeriesConnectorSweep.Run(Factory);
        var relation = Assert.Single(Relations());
        Assert.Equal(new[] { RelationEvidenceProvider.Inferred, RelationEvidenceProvider.Gcd }, relation.Evidence.Select(e => e.Provider).OrderBy(p => p));

        // The inferred half goes away when its reason does; the GCD half and the relation stay.
        using (var context = NewContext())
        {
            context.Series.Single(s => s.Id == old).Name = "Superboy (2011)";
            context.SaveChanges();
        }

        SeriesConnectorSweep.Run(Factory);
        Assert.Equal(RelationEvidenceProvider.Gcd, Assert.Single(Assert.Single(Relations()).Evidence).Provider);
    }

    [Fact]
    public void Run_SkipsYourPairs_AndDismissedPairs()
    {
        int old = Series("Superman (2011)", 2011, 2016);
        int @new = Series("Superman (2016)", 2016, 2018);
        int annual = Series("Superman Annual", 2017);
        using (var context = NewContext())
        {
            MediaRelationResolver.TryCreate(context, @new, old, RelationType.Sequel);
            MediaRelationResolver.DismissSeriesPair(context, annual, @new);
        }

        var result = SeriesConnectorSweep.Run(Factory);

        Assert.Equal(0, result.Created);
        Assert.Equal(RelationType.Sequel, Assert.Single(Relations()).RelationType);
    }

    [Fact]
    public void Remove_AnInferredRelation_DismissesThePair_SoTheSweepLeavesItGone()
    {
        Series("Superman (2011)", 2011, 2016);
        Series("Superman (2016)", 2016, 2018);
        SeriesConnectorSweep.Run(Factory);

        using (var context = NewContext())
        {
            MediaRelationResolver.Remove(context, context.MediaRelations.Single().Id);
            Assert.Single(context.SeriesRelationDismissals);
        }

        Assert.Equal(0, SeriesConnectorSweep.Run(Factory).Created);
        Assert.Empty(Relations());
    }

    [Fact]
    public void Run_ATieInThroughTwoEventsWithTheSameMainSeries_IsOneRelationWithTwoEvidenceRows()
    {
        var crisis = AddSeries("Dark Crisis", "1", "2");
        var jl = AddSeries("Justice League", "75", "76");
        AddEvent("Dark Crisis", StoryEventOrigin.Provider, new[] { crisis[0], jl[0] });
        AddEvent("Dark Crisis", StoryEventOrigin.User, new[] { crisis[1], jl[1] });

        SeriesConnectorSweep.Run(Factory);

        var relation = Assert.Single(Relations(), r => r.RelationType == RelationType.TieIn);
        Assert.Equal((jl[0].SeriesId, crisis[0].SeriesId), (relation.SourceSeriesId!.Value, relation.TargetSeriesId!.Value));
        Assert.Equal(2, relation.Evidence.Count);
        Assert.Equal(new[] { "Tie-in · #75", "Tie-in · #76" }, relation.Evidence.Select(e => e.ProviderRelationType).OrderBy(t => t));
    }

    [Fact]
    public void Run_MapsMetronAssociations_ByMetronId_ThenComicVineId_ThenGcdId()
    {
        int superman = Series("Superman", 2023);
        int byMetron = Series("Action Comics", 2023);
        int byComicVine = Series("Superman Unlimited", 2025);
        int byGcd = Series("Supergirl", 2025);
        using (var context = NewContext())
        {
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = byMetron, Provider = ComicProvider.Metron, ExternalId = "100" });
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = byComicVine, Provider = ComicProvider.ComicVine, ExternalId = "555" });
            context.Series.Single(s => s.Id == byGcd).GcdSeriesId = 777;
            context.SeriesAssociations.AddRange(
                new SeriesAssociation { SeriesId = superman, Provider = ComicProvider.Metron, ExternalSeriesId = "100", Name = "Action Comics" },
                new SeriesAssociation { SeriesId = superman, Provider = ComicProvider.Metron, ExternalSeriesId = "200", Name = "Superman Unlimited", ComicVineSeriesId = 555 },
                new SeriesAssociation { SeriesId = superman, Provider = ComicProvider.Metron, ExternalSeriesId = "300", Name = "Supergirl", GcdSeriesId = 777 },
                new SeriesAssociation { SeriesId = superman, Provider = ComicProvider.Metron, ExternalSeriesId = "400", Name = "Not in the library" });
            context.SaveChanges();
        }

        SeriesConnectorSweep.Run(Factory);

        var related = Relations().Where(r => r.RelationType == RelationType.Related).ToList();
        Assert.Equal(new[] { byMetron, byComicVine, byGcd }.OrderBy(i => i), related.Select(r => r.TargetSeriesId!.Value).OrderBy(i => i));
        Assert.All(related, r => Assert.Equal((RelationEvidenceProvider.Metron, "associated"), (r.Evidence.Single().Provider, r.Evidence.Single().ProviderRelationType)));
    }

    [Fact]
    public void MetronMapping_TwoLocalSeriesWithTheSameGcdId_IsNoMatch()
    {
        // A Metron or ComicVine id belongs to one local series (unique index); a GCD id can sit on both halves of a split series.
        int superman = Series("Superman", 2023);
        int a = Series("Action Comics", 2023);
        int b = Series("Action Comics (2023)", 2023);
        using (var context = NewContext())
        {
            foreach (var series in context.Series.Where(s => s.Id == a || s.Id == b))
            {
                series.GcdSeriesId = 777;
            }

            context.SeriesAssociations.Add(new SeriesAssociation { SeriesId = superman, Provider = ComicProvider.Metron, ExternalSeriesId = "100", Name = "Action Comics", GcdSeriesId = 777 });
            context.SaveChanges();
        }

        SeriesConnectorSweep.Run(Factory);

        Assert.DoesNotContain(Relations(), r => r.RelationType == RelationType.Related);
    }
}

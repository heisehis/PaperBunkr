using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
namespace Paperbunkr.Data.Tests;

public partial class RecommendationResolverTests
{
    [Fact] public void WeightsSumToOne_AndEraFallsToZeroAtFifteenYears()
    {
        Assert.Equal(1m, RecommendationResolver.Weights.Values.Sum());
        Assert.Equal(1m, RecommendationResolver.EraScore(2000, 2005));
        Assert.Equal(.5m, RecommendationResolver.EraScore(2000, 2010));
        Assert.Equal(0m, RecommendationResolver.EraScore(2000, 2015));
    }
    [Fact] public void UnlinkedCreatorsAndSemicolonCharactersBecomeCandidates_AndExplainMultipleReasons()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seed = SeedSeries(context, "Seed", "Adventure"); int target = SeedSeries(context, "Target", "Adventure");
        SeedIssue(context, seed, "Storm; Wolverine", "Claremont"); SeedIssue(context, target, "Storm, Wolverine", "Claremont");
        var candidate = Assert.Single(RecommendationResolver.GetRecommendations(context, seed));
        Assert.Equal(target, candidate.TargetSeriesId); Assert.Equal(1m, candidate.Signals.CharacterScore);
        Assert.Equal(1m, candidate.Signals.CreatorScore);
        Assert.Equal("same writer · 2 shared characters · shared genres", candidate.Explanation);
    }
    [Fact] public void WeightedTagsAndRolesUseWeightedOverlap_AndImprintIsStrongerThanPublisher()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seed = SeedSeries(context, "Seed"); int target = SeedSeries(context, "Target"); int publisherOnly = SeedSeries(context, "Publisher only");
        context.Series.Find(seed)!.Publisher = context.Series.Find(target)!.Publisher = context.Series.Find(publisherOnly)!.Publisher = "Marvel";
        context.Series.Find(seed)!.Imprint = context.Series.Find(target)!.Imprint = "MAX";
        int a = SeedIssue(context, seed, writer: "Alice", tags: "Mystery"); int b = SeedIssue(context, target, writer: "Alice", tags: "Mystery");
        context.IssueTags.Single(t => t.IssueId == a).Weight = IssueTagWeight.Core;
        context.IssueTags.Single(t => t.IssueId == b).Weight = IssueTagWeight.Incidental;
        context.Issues.Find(b)!.Writer = null; context.Issues.Find(b)!.Editor = "Alice"; context.SaveChanges();
        EntityIndexSync.SyncIssue(context, b);
        var results = RecommendationResolver.GetRecommendations(context, seed);
        var candidate = results.Single(r => r.TargetSeriesId == target);
        Assert.Equal(.25m, candidate.Signals.TagScore); Assert.Equal(.5m, candidate.Signals.CreatorScore);
        Assert.Equal(1m, candidate.Signals.PublisherScore);
        Assert.Equal(.5m, results.Single(r => r.TargetSeriesId == publisherOnly).Signals.PublisherScore);
    }
    [Fact] public void PoolCapsAtTwoHundred_AndExcludesPlaceholderOnlySeries()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seed = SeedSeries(context, "Seed", "Adventure");
        var targets = Enumerable.Range(1, 205).Select(i => new Series { Name = $"Candidate {i}", Genre = "Adventure", Issues = new() { new Issue { Number = "1" } } }).ToList();
        context.Series.AddRange(targets);
        var unowned = new Series { Name = "Unowned", Genre = "Adventure", Issues = new() { new Issue { IsPlaceholder = true } } };
        context.Series.Add(unowned); context.SaveChanges();
        var result = RecommendationResolver.GetRecommendations(context, seed, 1000);
        Assert.Equal(200, result.Count); Assert.DoesNotContain(result, r => r.TargetSeriesId == unowned.Id);
    }
    [Fact] public void PublisherSignalUsesEditedIssueMetadata_InsteadOfTheLegacySeriesValue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seed = SeedSeries(context, "Seed"), current = SeedSeries(context, "Current"), stale = SeedSeries(context, "Stale");
        context.Series.Find(seed)!.Publisher = context.Series.Find(stale)!.Publisher = "Old publisher";
        context.Series.Find(current)!.Publisher = "New publisher";
        context.Issues.First(i => i.SeriesId == seed).Publisher = "New publisher";
        context.SaveChanges();
        var candidate = Assert.Single(RecommendationResolver.GetRecommendations(context, seed));
        Assert.Equal(current, candidate.TargetSeriesId); Assert.Equal(.5m, candidate.Signals.PublisherScore);
    }
    [Fact, Trait("Speed", "Slow")]
    public void TenThousandIssueFixture_CompletesWithinTenSeconds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Enumerable.Range(0, 250).Select(n => new Series { Name = $"Series {n}", Genre = "Adventure",
            Issues = Enumerable.Range(1, 40).Select(i => new Issue { Number = i.ToString(), Year = 1980 + n % 30,
                Tags = new() { new IssueTag { Value = "Mystery", Field = IssueTagField.Tags, Weight = IssueTagWeight.Core } } }).ToList() }).ToList();
        context.Series.AddRange(series); context.SaveChanges(); context.ChangeTracker.Clear();
        var timer = Stopwatch.StartNew();
        var results = RecommendationResolver.GetRecommendations(context, series[0].Id, 1000);
        timer.Stop();
        Assert.Equal(200, results.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"10k-issue lookup took {timer.Elapsed}");
    }
}

using Paperbunkr.App.Services;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// <see cref="ContinuityTimelineViewModel"/> (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md; the
/// redesign's histogram, era progress and folding from docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md). The era
/// bucketing, reduced-confidence, open-in-reader and inferred-age tests are carried over from the old screen through the continuity
/// scope; the old series-family / library scopes aren't (no view reached them since 2026-08-28).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityTimelineViewModelTests : ContinuityScreenTestBase
{
    private static ContinuityTimelineViewModel ContinuityTimeline(int continuityId, Action<int>? goToReader = null)
    {
        var timeline = new ContinuityTimelineViewModel(goToReader ?? (_ => { }));
        timeline.LoadForContinuity(continuityId);
        return timeline;
    }

    [Fact]
    public void Eras_OnlyNonEmpty_YearOrdered_WithCountsAndProgress()
    {
        int flash = SeedSeries("The Flash", ("1", 1965), ("2", 1962), ("350", 1990));
        using (var context = PaperbunkrDb.CreateContext())
        {
            MarkRead(context.Issues.Single(i => i.Number == "2").Id);
        }

        var timeline = ContinuityTimeline(SeedContinuity("Earth-1", flash));

        Assert.Equal(new[] { "Silver Age", "Modern Age" }, timeline.Sections.Select(s => s.Label));
        var silver = timeline.Sections[0];
        Assert.Equal(new[] { "1962", "1965" }, silver.Issues.Select(i => i.YearLabel));
        Assert.Equal("Silver Age · 1962–1965", silver.HeaderLabel);
        Assert.Equal("2 issues · 1 read", silver.CountsLabel);
        Assert.Equal(0.5, silver.ReadFraction);
        Assert.True(silver.IsSilver);
        Assert.Contains("Earth-1", timeline.Title);
    }

    [Fact]
    public void Histogram_HasASlotPerYear_EvenEmptyOnes()
    {
        int series = SeedSeries("X", ("1", 2000), ("2", 2000), ("3", 2003));

        var timeline = ContinuityTimeline(SeedContinuity("C", series));

        Assert.Equal(new[] { 2000, 2001, 2002, 2003 }, timeline.YearBars.Select(b => b.Year));
        Assert.Equal(new[] { 2, 0, 0, 1 }, timeline.YearBars.Select(b => b.Count));
        Assert.Equal(1.0, timeline.YearBars[0].Fraction);
        Assert.Equal("2000 · 2 issues", timeline.YearBars[0].Tooltip);
        Assert.True(timeline.YearBars[1].IsEmpty);
        Assert.All(timeline.YearBars, b => Assert.Equal(48, b.BarWidth));              // four years: broad bars
        Assert.Equal(new[] { "2000", "2001", "2002", "2003" }, timeline.YearBars.Select(b => b.AxisLabel));
    }

    [Theory]
    [InlineData(3, 48, 1)]
    [InlineData(12, 32, 1)]
    [InlineData(25, 18, 5)]
    [InlineData(50, 10, 10)]
    [InlineData(90, 7, 10)]
    public void Histogram_BarsWidenAndLabelsThickenOnShortSpans(int years, double width, int step) =>
        Assert.Equal((width, step), ContinuityTimelineViewModel.BarSizing(years));

    [Fact]
    public void Histogram_LabelsTheFirstYear_AndThenEveryStep()
    {
        int series = SeedSeries("X", ("1", 1998), ("2", 2011));

        var timeline = ContinuityTimeline(SeedContinuity("C", series));

        Assert.Equal(new[] { "1998", "2000", "2005", "2010" }, timeline.YearBars.Select(b => b.AxisLabel).OfType<string>());
    }

    [Fact]
    public void ClickingAYear_UnfoldsItsEra_AndAsksForItsFirstCover()
    {
        int series = SeedSeries("X", ("1", 1965), ("2", 1990));
        var timeline = ContinuityTimeline(SeedContinuity("C", series));
        var modern = timeline.Sections.Single(s => s.IsModern);
        modern.ToggleFoldCommand.Execute(null);
        TimelineIssueCard? jumped = null;
        timeline.JumpRequested += card => jumped = card;

        timeline.JumpToYearCommand.Execute(timeline.YearBars.Single(b => b.Year == 1990));

        Assert.False(modern.IsFolded);
        Assert.Equal(1990, jumped!.Year);
    }

    [Fact]
    public void Folds_LastForTheSession_AndResetOnAnotherContinuity()
    {
        int series = SeedSeries("X", ("1", 1965));
        int continuityId = SeedContinuity("C", series);
        int other = SeedContinuity("D", series);
        var timeline = ContinuityTimeline(continuityId);

        timeline.Sections.Single().ToggleFoldCommand.Execute(null);
        timeline.LoadForContinuity(continuityId);
        Assert.True(timeline.Sections.Single().IsFolded);

        timeline.LoadForContinuity(other);
        Assert.False(timeline.Sections.Single().IsFolded);
    }

    [Fact]
    public void AnIssueInTheDisputedWindow_HasTheReducedConfidenceBadge()
    {
        int series = SeedSeries("Crisis Era", ("1", 1982));

        var card = ContinuityTimeline(SeedContinuity("C", series)).Sections.Single().Issues.Single();

        Assert.True(card.IsReducedConfidence);
        Assert.Contains("Bronze Age", card.ConfidenceReason);
    }

    [Fact]
    public void ClickingAnIssue_OpensTheReader()
    {
        int series = SeedSeries("The Flash", ("1", 1990));
        int? opened = null;
        var timeline = ContinuityTimeline(SeedContinuity("C", series), id => opened = id);
        var card = timeline.Sections.Single().Issues.Single();

        timeline.OpenIssueCommand.Execute(card);

        Assert.Equal(card.IssueId, opened);
    }

    [Fact]
    public void InferredAges_AcceptWritesTheLabel_AndRemovesTheRow()
    {
        int series = SeedSeries("The Flash", ("1", 1965));
        var timeline = ContinuityTimeline(SeedContinuity("C", series));

        var row = Assert.Single(timeline.InferredAges);
        Assert.Equal("Silver Age", row.AgeLabel);
        row.AcceptCommand.Execute(null);
        Drain();

        Assert.Empty(timeline.InferredAges);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("Silver (1956-69)", context.Issues.Single().BookAge);
    }

    [Fact]
    public void EventScope_LaysOutTheEventsMembers()
    {
        int issue = SeedIssue("X-Men", "1", "Regular", 1995);
        int eventId = SeedEvent("Age of Apocalypse", null, null, issue);
        var timeline = new ContinuityTimelineViewModel(_ => { });

        timeline.LoadForEvent(eventId);

        Assert.Equal("Modern Age", Assert.Single(timeline.Sections).Label);
        Assert.Contains("Age of Apocalypse", timeline.Title);
        Assert.Equal(ComicAge.Modern, timeline.Sections[0].Era);
    }
}
